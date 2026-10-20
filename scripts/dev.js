import { spawn } from 'node:child_process';
import { startServer, projectRoot } from '../server/index.js';

const server = startServer();
const vite = spawn(process.execPath, ['node_modules/vite/bin/vite.js', '--host', '127.0.0.1'], {
  cwd: projectRoot, stdio: 'inherit', windowsHide: true,
});
function stop() { vite.kill(); server.close(); }
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
vite.on('exit', code => { server.close(); process.exitCode = code || 0; });
server.on('error', error => { console.error(error.message); vite.kill(); process.exitCode = 1; });
