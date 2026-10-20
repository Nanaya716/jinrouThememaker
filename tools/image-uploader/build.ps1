param([string]$ApiKey, [string]$OutputName = 'ThemeImageUploader.exe')
$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
  $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
}
if (-not $ApiKey) {
  $defaults = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $toolRoot 'postimages-defaults.json') | ConvertFrom-Json
  $ApiKey = $defaults.apiKey
  if (-not $ApiKey) { throw '找不到默认图床 API Key，请通过 -ApiKey 指定。' }
}
if ($ApiKey -notmatch '^[a-zA-Z0-9_-]+$') { throw 'API Key 格式不正确。' }
$outputRoot = Join-Path $toolRoot 'dist'
$buildRoot = Join-Path $toolRoot '.build'
New-Item -ItemType Directory -Force -Path $outputRoot, $buildRoot | Out-Null
$source = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $toolRoot 'Uploader.cs')).Replace('__POSTIMAGES_API_KEY__', $ApiKey)
$generatedSource = Join-Path $buildRoot 'Uploader.g.cs'
[IO.File]::WriteAllText($generatedSource, $source, (New-Object Text.UTF8Encoding($false)))
$compiler = Join-Path $frameworkRoot 'csc.exe'
$exe = Join-Path $outputRoot $OutputName
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output `
  "/out:$exe" /reference:System.dll /reference:System.Core.dll `
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  /reference:System.Xml.dll /reference:System.Web.Extensions.dll `
  "/reference:$(Join-Path $frameworkRoot 'System.Net.Http.dll')" $generatedSource
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $toolRoot '使用说明.txt') -Destination $outputRoot -Force
Get-Item -LiteralPath $exe | Select-Object FullName, Length
