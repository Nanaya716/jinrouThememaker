import fs from 'node:fs/promises';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { projectRoot } from '../server/index.js';

const exec = promisify(execFile);
const root = path.join(projectRoot, '.local-data');
const repo = path.join(root, 'themes-repo');
const remote = path.join(root, 'remote.git');
const git = async (...args) => exec('git', args, { windowsHide: true });
if (await fs.access(path.join(repo, '.git')).then(() => true, () => false)) {
  console.log('本地主题库已存在，保留现有数据。');
} else {
  await fs.mkdir(path.join(repo, 'server/themes'), { recursive: true });
  const source = path.resolve(process.argv[2] || path.join(projectRoot, '../jinrou/server/themes'));
  const names = (await fs.readdir(source)).filter(name => name.endsWith('.coffee'));
  for (const name of names) await fs.copyFile(path.join(source, name), path.join(repo, 'server/themes', name));
  await git('init', '--bare', remote);
  await git('-C', repo, 'init', '-b', 'main');
  await git('-C', repo, 'config', 'user.name', 'ThemeMaker Local');
  await git('-C', repo, 'config', 'user.email', 'thememaker@localhost.invalid');
  await git('-C', repo, 'config', 'core.autocrlf', 'false');
  await git('-C', repo, 'remote', 'add', 'origin', remote);
  await git('-C', repo, 'add', 'server/themes');
  await git('-C', repo, 'commit', '-m', '导入本地主题副本');
  await git('-C', repo, 'push', '-u', 'origin', 'main');
  console.log(`已复制 ${names.length} 个主题到 ${repo}`);
  console.log('origin 指向本机裸仓库；提交和 push 均只影响本地副本。');
}
