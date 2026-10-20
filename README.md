# Coffee 主题文件生成器

Vue 编辑器与一个原生 Node.js 后端，无数据库、无账号系统。可以读取主题、编辑角色、整文件保存，并使用运行后端的机器上的 Git 身份完成 commit + push。后提交覆盖先提交；保存/推送期间拒绝其他提交，不做内容合并。

线上部署、更新、主题恢复和 Git 故障处理见 [维护手册](docs/maintenance.md)。部署模板位于 `deploy/`。

## 在本机运行

需要 Node.js 22+ 和 Git：

```powershell
cd E:\Code\jinrouThememaker
npm install
npm run setup:local
npm run dev
```

打开 <http://127.0.0.1:5173/thememaker/>。后端监听 `127.0.0.1:4318`。

`setup:local` 将相邻 `../jinrou/server/themes` 中的 `.coffee` 文件复制到忽略目录 `.local-data/themes-repo/server/themes`，初始化工作仓库和本机裸仓库 `.local-data/remote.git`，以本机裸仓库作为 origin。再次运行不会覆盖现有工作库。因此默认演示的保存、commit、push 都只影响本机副本，不修改游戏项目，也不连接生产服务器或 GitHub。可用 `npm run setup:local -- D:\other\themes` 指定初次复制的来源。

前端有 Vite 热更新；修改后端文件后重新启动 `npm run dev`。

## 配置真正的 Git 远程

确认本地流程后，可以修改**主题工作库**的 origin：

```powershell
git -C .local-data/themes-repo remote set-url origin <你的主题仓库地址>
git -C .local-data/themes-repo config user.name "你的 Git 名字"
git -C .local-data/themes-repo config user.email "你的 Git 邮箱"
```

也可以让后端使用既有仓库，以下环境变量均可选：

| 变量 | 默认值 | 用途 |
| --- | --- | --- |
| `THEMES_REPO` | `.local-data/themes-repo` 的绝对路径 | Git 工作仓库 |
| `THEMES_SUBDIR` | `server/themes` | 仓库内的主题子目录 |
| `THEMES_REMOTE` | `origin` | 推送目标 remote |
| `HOST` | `127.0.0.1` | 后端监听地址 |
| `PORT` | `4318` | 后端端口；开发代理使用同一值 |

例如 PowerShell 中先设置 `$env:THEMES_REPO='D:\my-theme-repo'`，再启动。主题目录需要真实存在，不能是符号链接；初始主题需要已经提交。Git remote、身份、SSH key/凭据由机器上的 Git 配置负责，不接受网页传入。推送当前分支到 remote 的同名分支，普通 push，不强制推送、不自动 pull/rebase；远程有新提交时会拒绝 push，需在终端处理。

若目标是游戏主项目仓库，在线保存产生的提交也会进入主项目历史，需要与今后的代码拉取流程协调。独立主题仓库则需要部署流程明确复制到运行目录并保留线上版本；本项目没有自动修改任何游戏部署脚本。

## 保存和恢复

- 点击「保存、提交并 push」：完整性检查 → 原子替换文件 → 仅提交该主题 → push。暂存区有其他内容、该主题已有未提交修改或仓库正在合并时，拒绝保存，避免混入人工操作。
- 提交锁覆盖文件写入、commit 和 push。同一个服务或共用一个仓库的其他服务正在提交时返回 409，稍后重试。进程异常退出留下 `.git/thememaker.lock` 时，确认没有服务在提交后才手动移除该锁。
- commit 失败会恢复文件并撤销该文件的暂存；push 失败会保留文件和本地提交，页面显示提交号，通过「重试推送」补推，不重复提交。相同文件内容不另建空提交。服务不会运行工作仓库的 commit hooks。
- 历史与恢复直接使用 Git，无额外管理界面。可用 `git log -- server/themes/文件.coffee` 查看；恢复某文件旧版本后只提交该文件并 push，保留恢复记录。
- 浏览器草稿自动保存。文件导入、读取使用静态语法解析，绝不 require/eval 上传文件；允许普通数据与固定返回布尔值的 `isAvailable`，拒绝其他执行代码、字符串插值和特殊原型属性。
- 保留现有角色 ID、其他数据字段、数组形式和 `openAvatar`。第一次表单保存会统一源码格式、移除注释；重复属性按游戏语义保留最后一项并显示提示，不改变当前生效值。自定义可执行逻辑不属于此编辑器支持的主题格式。

## 图片

网页只填写、预览图片直链，已移除网页原生上传、裁剪和图床 Key 设置。可用独立的 Windows [主题图像工具](tools/image-uploader/README.md) 在本机裁剪、上传并复制直链，再粘贴到角色的头像链接；也可直接填写其他图床的直链。

## 构建与检查

```powershell
npm test
npm run build
npm start
```

`npm start` 同时提供构建后的 `/thememaker/` 页面和 `/thememaker-api/` API，地址为 <http://127.0.0.1:4318/thememaker/>。若将来使用 Nginx，只需将这两个路径路由给此后端；本次本地开发不修改 Nginx。

测试在系统临时目录初始化独立工作仓库和本机裸仓库，覆盖提交、push、覆盖旧版本、并发拒绝、推送失败重试、提交失败恢复、路径限制和静态校验，不会推送到真实远程仓库。
