# jinrou.icu ThemeMaker 维护手册

## 先记住这几件事

- 网页：`https://www.jinrou.icu/thememaker/`，API：`/thememaker-api/`。
- 编辑器代码：`/srv/jinrou/thememaker`，来自 `Nanaya716/jinrouThememaker` 的 `main`。
- 实际编辑的主题：`/srv/jinrou/app/server/themes`，Git 历史属于 `Nanaya716/jinrou` 的 `nanayaBranch`，**不在编辑器仓库里**。
- 服务：`thememaker.service`；配置：`/srv/jinrou/shared/thememaker.env`；端口：`127.0.0.1:4318`。
- 游戏是另一个服务 `jinrou.service`。只更新编辑器时不运行游戏的 `update.bash`，不重启游戏。
- 没有账号系统，任何能打开网页的人都能修改主题，后保存覆盖先保存。提交身份统一使用服务器游戏仓库配置的 Git 身份；不记录玩家真实身份。

下面所有命令均在 `ssh root@8.137.83.4` 后执行。

部署时已确认常用的 `www.jinrou.icu` 解析到此服务器。裸域 `jinrou.icu` 当时解析到其他地址，访问异常；本文使用带 `www` 的网址，不修改 DNS。若以后出现「服务器健康但域名打不开」，先检查是否访问了错误的主机名，以及对应 DNS 记录。

## 日常编辑

打开主题 → 修改 → 填写说明 → 保存、提交并 push。成功后页面会显示提交号。游戏监听原主题目录自动加载，不需要重启；已有玩家已分配的头像/名称不会统一刷新。

第一次表单保存会统一源码格式并去除注释。未知的数据字段和角色 ID 保留；重复属性仅保留最后生效项，页面会提示。只允许数据主题和固定返回布尔值的 `isAvailable`，不能通过网页上传其他可执行逻辑。

图片在浏览器裁剪和上传。Postimages 是否允许浏览器读取响应由其跨域规则决定；失败时下载裁剪结果，打开 Postimages 自行上传后粘贴直链。改 Nginx 的跨域头不能替第三方图床授予权限。

## 查看运行状态、日志、重启

```bash
systemctl status thememaker --no-pager
journalctl -u thememaker -n 80 --no-pager
curl -fsS http://127.0.0.1:4318/thememaker-api/health
systemctl restart thememaker
```

配置修改后只需要 `systemctl restart thememaker`。修改 systemd unit 后先 `systemctl daemon-reload`。修改 Nginx 后先 `nginx -t`，通过后才 `systemctl reload nginx`。

重启/停止会等待正在执行的请求完成，最多 45 秒。不要用 `kill -9` 中断提交。

## 更新编辑器

先在本机的 thememaker 仓库提交并 push，再在服务器执行。选择没有人正在保存的时间；更新期间编辑器会短暂不可用，游戏继续运行。

```bash
cd /srv/jinrou/thememaker
export PATH=/opt/jinrou-node22/bin:$PATH
git status --short
```

如果有非预期修改，先处理，**不要直接 reset --hard**。工作区干净后：

```bash
backup=/srv/jinrou/shared/thememaker-backups/$(date +%Y%m%d-%H%M%S)
mkdir -p "$backup"
git rev-parse HEAD > "$backup/code-commit"
cp -a dist "$backup/dist"
cp -a /srv/jinrou/shared/thememaker.env "$backup/thememaker.env"
systemctl stop thememaker
git pull --ff-only origin main
npm ci
npm run build
systemctl start thememaker
curl -fsS http://127.0.0.1:4318/thememaker-api/health
```

任何步骤失败，先查看终端错误；修复后 `systemctl start thememaker`。代码回退见下文。若 unit 或 Nginx 模板发生变化，不自动覆盖线上配置：比较后单独更新并做语法检查。

## 查看主题历史、恢复一个主题

```bash
cd /srv/jinrou/app
git log --oneline -- server/themes/主题文件.coffee
git show 提交号 -- server/themes/主题文件.coffee
```

最简单的恢复方法：把旧文件下载后在编辑器导入，再保存，会产生新的恢复提交。终端恢复时先暂停网页写入，再只恢复一个文件：

```bash
systemctl stop thememaker
cd /srv/jinrou/app
git status --short
git restore --source=想恢复到的提交号 -- server/themes/主题文件.coffee
git add -- server/themes/主题文件.coffee
git commit -m '恢复某主题到指定版本' -- server/themes/主题文件.coffee
git push origin HEAD:nanayaBranch
systemctl start thememaker
```

工作区如果已有其他修改，先判断来源，不能把它们混进恢复操作。不要为恢复一个主题执行整个游戏仓库的 `git reset --hard` 或强制推送。主题回退不会恢复图床上已经删除或覆盖的图片。

## 页面显示 push 失败

文件和本地提交已经保留，游戏也已读取新文件。首先点「重试推送」。仍失败则检查：

```bash
cd /srv/jinrou/app
git status --short
git log -3 --oneline
git remote -v
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -T git@github.com
git push origin HEAD:nanayaBranch
```

GitHub 的 SSH 验证成功会输出 `Hi ...`，但该命令本身返回码通常为 1。私钥是服务器 `/root/.ssh/id_ed25519`，不要复制到网页、仓库或聊天中。origin 可以保留 HTTPS 拉取地址，但需要配置可写的 SSH push 地址：

```bash
git remote set-url --push origin git@github.com:Nanaya716/jinrou.git
```

本次部署将服务器已有公钥注册为 `jinrou` 仓库的可写 Deploy key，名称是 `jinrou-thememaker@8.137.83.4`。它只获得该仓库的权限，没有把本机 GitHub 登录凭据复制到服务器。可在 [GitHub 仓库 Deploy keys](https://github.com/Nanaya716/jinrou/settings/keys) 查看或撤销；撤销后在线保存仍会本地提交，但 push 将失败。GitHub 主机公钥由官方 `https://api.github.com/meta` 核对后加入服务器 known_hosts，仍保持严格主机验证。

如果是远程分支有新提交（non-fast-forward），先停编辑器并核对本地未推送提交，再处理：

```bash
systemctl stop thememaker
git fetch origin nanayaBranch
git log --oneline --left-right HEAD...origin/nanayaBranch
```

需要合并时可 `git rebase origin/nanayaBranch`，但只在弄清待推送提交后进行。冲突时人工处理；不要强制覆盖远程。完成 push 后再启动编辑器。整个过程不要求重启游戏。

## 页面一直说正在提交

可能是上一次进程异常退出留下锁。先看日志和健康接口；正常 push 最多约 30 秒。需要清锁时：

```bash
systemctl stop thememaker
pgrep -af 'git.*(/srv/jinrou/app|push|commit)'
cat /srv/jinrou/app/.git/thememaker.lock
```

确认没有存活的主题保存/Git 进程、文件和 Git 状态正常后，再执行：

```bash
rm -f /srv/jinrou/app/.git/thememaker.lock
systemctl start thememaker
```

不要顺手删除 `.git/index.lock`；它可能属于其他 Git 操作。

## 更新游戏项目时

主题提交和游戏代码使用同一个 `nanayaBranch`。因此本机改游戏前应先拉取最新提交；不要忽略线上产生的主题提交。在服务器手动更新游戏前，先停止编辑器，确认主题已提交并 push，再运行原来的游戏更新流程，最后启动编辑器。这能避免网页保存与 `git pull`/构建同时操作同一个仓库。

```bash
systemctl stop thememaker
cd /srv/jinrou/app
git status --short
git push origin HEAD:nanayaBranch
bash update.bash
systemctl start thememaker
```

游戏原有更新脚本会重启游戏，应自行选择合适时间。**仅更新 thememaker 时不要执行这一组命令。**

## 回退编辑器代码或下线编辑入口

回退只影响编辑器，不回退已发布主题。先停服务；根据上面备份目录的 `code-commit` 找到旧提交，在干净的编辑器工作区操作：

```bash
systemctl stop thememaker
cd /srv/jinrou/thememaker
git switch --detach 旧的编辑器提交号
export PATH=/opt/jinrou-node22/bin:$PATH
npm ci
npm run build
systemctl start thememaker
```

恢复正常跟踪时运行 `git switch main`，再按更新步骤操作。完全暂停在线修改只需 `systemctl stop thememaker`，游戏继续运行；编辑器路径会暂时返回 502。

首次部署还备份了原静态编辑器、Nginx 配置和主题目录，位于 `/srv/jinrou/shared/thememaker-backups/` 下相应时间目录。需要恢复旧静态编辑器时恢复该份 Nginx 配置，先 `nginx -t` 再 reload；不要动游戏的 `location /`。
