import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createStore, HttpError } from './store.js';
import { parseTheme } from './theme-format.js';

export const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function createApp(options = {}) {
  const store = options.store || createStore({
    repo: process.env.THEMES_REPO || path.join(projectRoot, '.local-data/themes-repo'),
    subdir: process.env.THEMES_SUBDIR || 'server/themes',
    remote: process.env.THEMES_REMOTE || 'origin',
  });
  const dist = options.dist || path.join(projectRoot, 'dist');
  return http.createServer(async (req, res) => {
    const json = (status, value) => {
      res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
      res.end(JSON.stringify(value));
    };
    try {
      const url = new URL(req.url, 'http://localhost');
      const api = url.pathname.startsWith('/thememaker-api/');
      if (api && !['GET', 'HEAD'].includes(req.method)) {
        // Public editor, but do not allow another website to submit on the user's behalf.
        if (req.headers.origin && req.headers.origin !== `http://${req.headers.host}` &&
            req.headers.origin !== `https://${req.headers.host}`) throw new HttpError(403, '请从主题编辑器页面提交');
        if (!/^application\/json(?:;|$)/i.test(req.headers['content-type'] || '')) throw new HttpError(415, '请使用 JSON 请求');
      }
      async function body() {
        let size = 0;
        const chunks = [];
        for await (const chunk of req) {
          size += chunk.length;
          if (size > 3 * 1024 * 1024) throw new HttpError(413, '请求过大');
          chunks.push(chunk);
        }
        try {
          const value = JSON.parse(Buffer.concat(chunks).toString('utf8'));
          if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error();
          return value;
        }
        catch { throw new HttpError(400, 'JSON 格式不正确'); }
      }
      if (req.method === 'GET' && url.pathname === '/thememaker-api/health') return json(200, { ok: true, busy: store.busy });
      if (req.method === 'GET' && url.pathname === '/thememaker-api/themes') return json(200, await store.list());
      if (req.method === 'POST' && url.pathname === '/thememaker-api/parse') {
        const input = await body();
        if (typeof input.source !== 'string' || Buffer.byteLength(input.source) > 2 * 1024 * 1024) throw new HttpError(400, '主题文件最大为 2 MB');
        try { return json(200, parseTheme(input.source)); }
        catch (error) { throw new HttpError(400, error.message); }
      }
      if (req.method === 'POST' && url.pathname === '/thememaker-api/push') return json(200, await store.retryPush());
      const match = url.pathname.match(/^\/thememaker-api\/themes\/([^/]+)$/);
      if (match && req.method === 'GET') return json(200, await store.read(decodeURIComponent(match[1])));
      if (match && req.method === 'PUT') {
        const input = await body();
        return json(200, await store.save(decodeURIComponent(match[1]), input.source, input.message));
      }
      if (api) throw new HttpError(404, '接口不存在');
      if (!['GET', 'HEAD'].includes(req.method)) throw new HttpError(405, '不支持此请求方法');
      if (url.pathname === '/' || url.pathname === '/thememaker') {
        res.writeHead(302, { Location: '/thememaker/' }); return res.end();
      }
      if (!url.pathname.startsWith('/thememaker/')) throw new HttpError(404, '页面不存在');
      const relative = decodeURIComponent(url.pathname.slice('/thememaker/'.length)) || 'index.html';
      const file = path.resolve(dist, relative);
      if (!file.startsWith(path.resolve(dist) + path.sep)) throw new HttpError(404, '页面不存在');
      const content = await fs.readFile(file);
      const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css', '.ico': 'image/x-icon', '.svg': 'image/svg+xml' };
      res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'X-Content-Type-Options': 'nosniff' });
      res.end(req.method === 'HEAD' ? undefined : content);
    } catch (error) {
      const status = error.status || (error.code === 'ENOENT' ? 404 : 500);
      // Git stderr can contain remote URLs/credentials; keep it out of public responses.
      const message = error.status ? error.message : error.code === 'ENOENT'
        ? '文件或本地主题库不存在；首次运行请执行 npm run setup:local'
        : '操作失败，请检查本机 Git 配置、仓库状态和服务器终端';
      if (status === 500) console.error(error.message);
      json(status, { error: message, ...error.details });
    }
  });
}

export function startServer() {
  const server = createApp();
  const host = process.env.HOST || '127.0.0.1';
  const port = Number(process.env.PORT || 4318);
  server.listen(port, host, () => console.log(`ThemeMaker: http://${host}:${port}/thememaker/`));
  // Let an in-flight commit/push finish and release its lock before systemd stops us.
  const shutdown = () => server.close(() => process.exit(0));
  process.once('SIGTERM', shutdown);
  process.once('SIGINT', shutdown);
  return server;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) startServer();
