import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { once } from 'node:events';
import { createStore } from '../server/store.js';
import { createApp } from '../server/index.js';
import { parseTheme } from '../server/theme-format.js';
import { serializeTheme } from '../src/lib/theme-format.js';

const exec = promisify(execFile);
const data = { name: '测试主题', lockable: true, vote: '保留提示', skins: { first: { name: '第一个角色', avatar: ['https://example.com/1.png'], prize: ['称号,含逗号'] } } };
const source = serializeTheme(data);
const clone = value => JSON.parse(JSON.stringify(value));
async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'thememaker-test-'));
  const repo = path.join(root, 'repo'), remote = path.join(root, 'remote.git');
  const git = async (...args) => (await exec('git', ['-C', repo, ...args], { windowsHide: true })).stdout.trim();
  await fs.mkdir(path.join(repo, 'server/themes'), { recursive: true });
  await exec('git', ['init', '--bare', remote], { windowsHide: true });
  await git('init', '-b', 'main');
  await git('config', 'user.name', 'Local Test');
  await git('config', 'user.email', 'test@localhost.invalid');
  await git('config', 'core.autocrlf', 'false');
  await git('remote', 'add', 'origin', remote);
  await fs.writeFile(path.join(repo, 'server/themes/test.coffee'), source);
  await git('add', '.'); await git('commit', '-m', 'initial'); await git('push', '-u', 'origin', 'main');
  t.after(async () => {
    const resolved = path.resolve(root);
    assert.equal(path.dirname(resolved), path.resolve(os.tmpdir()));
    assert.ok(path.basename(resolved).startsWith('thememaker-test-'));
    await fs.rm(resolved, { recursive: true, force: true });
  });
  return { root, repo, remote, git, store: createStore({ repo }), file: path.join(repo, 'server/themes/test.coffee') };
}

test('literal parsing preserves strings, arrays, first skin, unknown fields and special themes', () => {
  const input = clone(data);
  input.opening = '引号 " 和反斜线 \\ 与 #{process.exit()}\n下一行';
  input.custom = { count: -3, note: '额外数据' };
  const parsed = parseTheme(serializeTheme(input, false));
  assert.deepEqual(clone(parsed.data), input);
  assert.equal(parsed.available, false);
  assert.equal(parseTheme(serializeTheme({ name: '跨主题', lockable: true, openAvatar: true })).data.openAvatar, true);
});

test('rejects executable code, interpolation and prototype keys without executing them', () => {
  for (const input of [
    source + '\nprocess.exit()\n',
    source.replace('"测试主题"', '"#{process.exit()}"'),
    source.replace('return true', 'return process.exit()'),
    source.replace('name: "测试主题"', 'name: require("fs")'),
    source.replace('name: "测试主题"', '__proto__: {}\n    name: "测试主题"'),
  ]) assert.throws(() => parseTheme(input));
  const duplicate = parseTheme(source.replace('name: "测试主题"', 'name: "旧值"\n    name: "测试主题"'));
  assert.equal(duplicate.data.name, '测试主题');
  assert.equal(duplicate.warnings.length, 1);
});

test('save commits with server identity, pushes, and later stale content simply replaces it', async t => {
  const f = await fixture(t);
  const first = serializeTheme({ ...data, name: 'A 的版本' });
  const second = serializeTheme({ ...data, name: 'B 的版本' });
  const a = await f.store.save('test.coffee', first, 'A 修改');
  const b = await f.store.save('test.coffee', second, 'B 修改');
  assert.notEqual(a.commit, b.commit);
  assert.equal(await fs.readFile(f.file, 'utf8'), second);
  assert.equal(await f.git('rev-list', '--count', 'HEAD'), '3');
  assert.equal(await f.git('show', '-s', '--format=%an <%ae>', 'HEAD'), 'Local Test <test@localhost.invalid>');
  assert.equal((await exec('git', ['--git-dir', f.remote, 'rev-parse', 'main'])).stdout.trim(), b.commit);
  assert.equal(await f.git('status', '--porcelain'), '');
  const unchanged = await f.store.save('test.coffee', second);
  assert.equal(unchanged.unchanged, true);
  assert.equal(await f.git('rev-list', '--count', 'HEAD'), '3');
});

test('busy submissions and another backend sharing the repository are rejected', async t => {
  const f = await fixture(t);
  const pending = f.store.save('test.coffee', serializeTheme({ ...data, name: '并发版本' }));
  await assert.rejects(f.store.save('test.coffee', source), error => error.status === 409);
  await pending;
  const lock = await f.git('rev-parse', '--path-format=absolute', '--git-path', 'thememaker.lock');
  await fs.writeFile(lock, 'other process');
  try { await assert.rejects(createStore({ repo: f.repo }).save('test.coffee', source), error => error.status === 409); }
  finally { await fs.unlink(lock); }
});

test('failed push preserves a local commit and can retry without another commit', async t => {
  const f = await fixture(t);
  await f.git('remote', 'set-url', 'origin', path.join(f.root, 'missing.git'));
  let saved;
  await assert.rejects(f.store.save('test.coffee', serializeTheme({ ...data, name: '待推送版本' })), error => {
    saved = error.details; return error.status === 502 && saved.saved && !saved.pushed;
  });
  assert.equal(await f.git('rev-parse', 'HEAD'), saved.commit);
  await f.git('remote', 'set-url', 'origin', f.remote);
  const retried = await f.store.retryPush();
  assert.equal(retried.commit, saved.commit);
  assert.equal(await f.git('rev-list', '--count', 'HEAD'), '2');
});

test('commit failure restores the original file and clears its index entry', async t => {
  const f = await fixture(t);
  await f.git('config', 'user.name', '');
  await f.git('config', 'user.email', '');
  await assert.rejects(f.store.save('test.coffee', serializeTheme({ ...data, name: '失败版本' })));
  assert.equal(await fs.readFile(f.file, 'utf8'), source);
  assert.equal(await f.git('status', '--porcelain'), '');
  assert.equal(await f.git('rev-list', '--count', 'HEAD'), '1');
});

test('cannot commit unrelated staged work, overwrite uncommitted themes, or escape the theme directory', async t => {
  const f = await fixture(t);
  await fs.writeFile(path.join(f.repo, 'unrelated.txt'), 'keep'); await f.git('add', 'unrelated.txt');
  await assert.rejects(f.store.save('test.coffee', source), error => error.status === 409);
  assert.match(await f.git('diff', '--cached', '--name-only'), /unrelated/);
  await f.git('reset', '-q', 'HEAD', '--', 'unrelated.txt');
  await fs.appendFile(f.file, '# 本地修改\n');
  await assert.rejects(f.store.save('test.coffee', source), error => error.status === 409);
  for (const filename of ['../escape.coffee', '-option.coffee', 'CON.coffee', 'a/b.coffee']) {
    await assert.rejects(f.store.read(filename), error => error.status === 400);
  }
});

test('HTTP endpoints list, parse, save Unicode filenames, and reject unsafe requests', async t => {
  const f = await fixture(t);
  const app = createApp({ store: f.store });
  app.listen(0, '127.0.0.1'); await once(app, 'listening');
  t.after(() => new Promise(resolve => app.close(resolve)));
  const base = `http://127.0.0.1:${app.address().port}/thememaker-api`;
  const headers = { 'Content-Type': 'application/json' };
  assert.equal((await (await fetch(base + '/themes')).json())[0].filename, 'test.coffee');
  assert.equal((await fetch(base + '/parse', { method: 'POST', headers, body: JSON.stringify({ source }) })).status, 200);
  assert.equal((await fetch(base + '/themes/' + encodeURIComponent('中文主题 (1).coffee'), {
    method: 'PUT', headers, body: JSON.stringify({ source }),
  })).status, 200);
  assert.equal((await fetch(base + '/push', { method: 'POST', headers: { ...headers, Origin: 'https://other.example' }, body: '{}' })).status, 403);
  assert.equal((await fetch(base + '/parse', { method: 'POST', headers, body: '{' })).status, 400);
  assert.equal((await fetch(base + '/parse', { method: 'POST', headers, body: JSON.stringify({ source: source + '\nprocess.exit()' }) })).status, 400);
});
