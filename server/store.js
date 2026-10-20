import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import fs from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { parseTheme } from './theme-format.js';

const exec = promisify(execFile);
export class HttpError extends Error {
  constructor(status, message, details = {}) { super(message); this.status = status; this.details = details; }
}

export function createStore({ repo, subdir = 'server/themes', remote = 'origin', pushTimeout = 30000 }) {
  repo = path.resolve(repo);
  const directory = path.resolve(repo, subdir);
  if (!directory.startsWith(repo + path.sep)) throw new Error('主题目录必须位于 Git 工作区内');
  const relativeDir = path.relative(repo, directory).split(path.sep).join('/');
  let busy = false;
  const git = async (...args) => {
    const { stdout } = await exec('git', ['-C', repo, ...args], {
      timeout: pushTimeout, maxBuffer: 4 * 1024 * 1024, windowsHide: true,
      env: { ...process.env, GIT_TERMINAL_PROMPT: '0' },
    });
    return stdout.trim();
  };
  function filename(name) {
    if (typeof name !== 'string' || !/^[\p{L}\p{N}_][\p{L}\p{N}_. ()-]{0,119}\.coffee$/u.test(name) ||
        /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name)) {
      throw new HttpError(400, '文件名只能包含文字、数字、空格、括号、下划线、点和连字符，并以 .coffee 结尾');
    }
    return path.join(directory, name);
  }
  async function checkDirectory() {
    // Reject symlinks, including ancestors, so a theme cannot escape this workspace.
    const realRepo = await fs.realpath(repo);
    const realDirectory = await fs.realpath(directory);
    if (realDirectory !== path.join(realRepo, path.relative(repo, directory))) {
      throw new HttpError(400, '主题目录不能通过符号链接指向其他位置');
    }
  }
  async function regularFile(file) {
    const stat = await fs.lstat(file);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new HttpError(400, '只允许普通主题文件');
  }
  async function read(name) {
    const file = filename(name);
    await checkDirectory();
    await regularFile(file);
    const source = await fs.readFile(file, 'utf8');
    return { filename: name, source, ...parseTheme(source) };
  }
  async function list() {
    await checkDirectory();
    const names = (await fs.readdir(directory)).filter(name => name.endsWith('.coffee')).sort();
    return Promise.all(names.map(async name => {
      try { const theme = await read(name); return { filename: name, name: theme.data.name }; }
      catch { return { filename: name, name, unsupported: true }; }
    }));
  }
  async function exclusive(action) {
    if (busy) throw new HttpError(409, '正在保存或推送其他提交，请稍后重试');
    busy = true;
    let lock;
    try {
      const lockPath = await git('rev-parse', '--path-format=absolute', '--git-path', 'thememaker.lock');
      try { lock = await fs.open(lockPath, 'wx'); }
      catch (error) {
        if (error.code === 'EEXIST') throw new HttpError(409, '另一个主题服务正在提交，请稍后重试');
        throw error;
      }
      await lock.writeFile(`${process.pid}\n`);
      try { return await action(); }
      finally { await lock.close(); await fs.unlink(lockPath); }
    } finally { busy = false; }
  }
  async function push() {
    const branch = await git('symbolic-ref', '--quiet', '--short', 'HEAD');
    await git('push', '--porcelain', remote, `HEAD:refs/heads/${branch}`);
    return { commit: await git('rev-parse', 'HEAD'), pushed: true };
  }
  async function save(name, source, message = '') {
    return exclusive(async () => {
      const file = filename(name);
      if (typeof source !== 'string' || Buffer.byteLength(source) > 2 * 1024 * 1024) throw new HttpError(400, '主题文件最大为 2 MB');
      try { parseTheme(source); } catch (error) { throw new HttpError(400, error.message); }
      await checkDirectory();
      const relative = `${relativeDir}/${name}`;
      await git('check-ref-format', '--branch', await git('symbolic-ref', '--quiet', '--short', 'HEAD'));
      await git('remote', 'get-url', '--push', remote);
      // Do not accidentally commit another user's staged work or an unfinished merge.
      if (await git('diff', '--cached', '--name-only')) throw new HttpError(409, 'Git 暂存区有其他修改，请先处理');
      for (const marker of ['MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply']) {
        const markerPath = await git('rev-parse', '--path-format=absolute', '--git-path', marker);
        if (await fs.access(markerPath).then(() => true, () => false)) throw new HttpError(409, 'Git 有未完成的合并或变基，请先处理');
      }
      if (await git('status', '--porcelain', '--untracked-files=all', '--', relative)) {
        throw new HttpError(409, '该主题存在未提交的本地修改，请先提交或恢复');
      }
      let previous = null;
      let mode = 0o644;
      try {
        await regularFile(file);
        previous = await fs.readFile(file);
        mode = (await fs.stat(file)).mode & 0o777;
      } catch (error) { if (error.code !== 'ENOENT') throw error; }
      const unchanged = previous?.equals(Buffer.from(source));
      let committed = false;
      // Temporary files stay outside the watched theme directory, on the same filesystem.
      const temporary = path.join(path.dirname(directory), `.thememaker-${randomUUID()}.tmp`);
      try {
        if (!unchanged) {
          await fs.writeFile(temporary, source, { flag: 'wx', mode });
          await fs.rename(temporary, file);
          await git('add', '--', relative);
          const note = typeof message === 'string' ? message.replace(/[\r\n\0]/g, ' ').trim().slice(0, 200) : '';
          const hooksPath = await git('rev-parse', '--path-format=absolute', '--git-path', 'thememaker-no-hooks');
          await git('-c', `core.hooksPath=${hooksPath}`, 'commit', '-m', `主题：${name}${note ? ` — ${note}` : ''}`, '--', relative);
          committed = true;
        }
      } catch (error) {
        if (!committed) {
          await git('reset', '-q', 'HEAD', '--', relative);
          if (previous === null) await fs.rm(file, { force: true });
          else { await fs.writeFile(temporary, previous, { mode }); await fs.rename(temporary, file); }
        }
        throw error;
      } finally { await fs.rm(temporary, { force: true }); }
      const commit = await git('rev-parse', 'HEAD');
      try { return { ...await push(), unchanged: Boolean(unchanged) }; }
      catch {
        // A failed push must not erase a recorded local commit or pretend to have succeeded.
        throw new HttpError(502, '本地已保存并提交，但 push 失败；请检查远程仓库后点击重试推送', { saved: true, pushed: false, commit });
      }
    });
  }
  return { read, list, save, retryPush: () => exclusive(async () => {
    try { return await push(); }
    catch { throw new HttpError(502, 'push 失败，本地提交已保留；请检查远程仓库和 Git 凭据后重试'); }
  }), get busy() { return busy; } };
}
