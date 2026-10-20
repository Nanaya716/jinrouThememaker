# 主题图像工具

独立 Windows 便携程序。使用系统 .NET Framework 4.8 和 WinForms，无第三方运行时或 NuGet 依赖；构建出的 exe 可以单独移动和分发。

```powershell
./build.ps1
./dist/ThemeImageUploader.exe
```

构建脚本从本目录 `postimages-defaults.json` 读取沿用的默认 Postimages Key，也支持 `./build.ps1 -ApiKey YOUR_KEY`，不依赖网页上传代码。生成的临时源码、exe 和测试产物不进入 Git。详细使用方法见 `使用说明.txt`。

若旧程序还在运行，可使用 `./build.ps1 -OutputName ThemeImageUploader-1.0.1.exe` 生成不同文件名的新版，无需关闭用户正在编辑的窗口。v1.0.1 修复了 .NET Framework 的 `FormUrlEncodedContent` 在图片 Base64 超过 URI 编码长度上限时报“无效的 URI: URI 字符串太长”的问题，改用不受该上限影响的 POST 表单编码。

v1.0.2 在自定义 Key 下增加“自取账号API链接”，使用系统默认浏览器打开 `https://postimages.org/login/api`。

图片在本机使用 GDI+ 解码、处理 EXIF 方向并裁剪为方形 PNG。上传使用 .NET HttpClient 直接调用现有 `https://api.postimage.org/1/upload`，读取 XML 中的 `links/hotlink`，不会经过网站服务器，也没有浏览器 CORS 限制。网络、图床额度和 Key 规则仍由 Postimages 决定。

只有点击上传才会发送裁剪图片；只打开、裁剪和保存文件不会发起网络请求。上传成功后复制直链，并将时间、文件名、输出尺寸、PNG SHA-256 和图床返回链接写入本机 JSONL。日志失败或剪贴板占用不会把已成功的上传误报成失败。

内置验证入口（不显示交互窗口）：

```powershell
# 合成图片验证取景、输出尺寸、透明度、响应校验、本地记录及界面渲染
$test = Start-Process ./dist/ThemeImageUploader.exe -ArgumentList '--self-test', 'C:\Temp\theme-image-test' -WindowStyle Hidden -Wait -PassThru
$test.ExitCode

# 同样的合成图片，真实上传到 Postimages；每运行一次会创建一张测试图片
$test = Start-Process ./dist/ThemeImageUploader.exe -ArgumentList '--upload-test', 'C:\Temp\theme-upload-test' -WindowStyle Hidden -Wait -PassThru
$test.ExitCode
```

指定目录中会产生验证 JSON、合成 PNG、本地测试记录和界面预览。验证入口不读取用户图片，也不向正式上传历史写入测试数据。

验证包含 256 / 512 / 1024 输出的大体积噪声 PNG，对超过旧编码器限制的 Base64 和含中文、空格、`+ / = & % #` 的表单内容检查完整往返；真实上传测试使用其中的 512 像素大图。
