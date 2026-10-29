[CmdletBinding()]
param(
  [string]$DotnetPath = (Join-Path $env:LOCALAPPDATA 'LoliaBloomBuild\dotnet\dotnet.exe'),
  [switch]$PublishRelease
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
function Remove-OldFiles([string]$Path, [bool]$Recursive = $false) {
  for ($attempt = 0; $attempt -lt 15; $attempt++) {
    try { Remove-Item -LiteralPath $Path -Force -Recurse:$Recursive -ErrorAction Stop; return }
    catch { if ($attempt -eq 14) { throw "旧文件仍被占用，无法安全清理：$Path。请先关闭正在运行的 Lolia Bloom，再重新打包。" }; Start-Sleep -Seconds 1 }
  }
}
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw "找不到 .NET SDK：$DotnetPath。请先安装 .NET 10 SDK。" }
$version = ([xml](Get-Content -Raw Directory.Build.props)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Directory.Build.props 中的版本号无效：$version" }
$tag = "v$version"
$exeName = "LoliaBloom-$tag-win-x64.exe"
$zipName = "LoliaBloom-$tag-win-x64.zip"
$releaseOutput = Join-Path (Split-Path $root -Parent) 'LoliaBloom-Releases'
if (Test-Path -LiteralPath $releaseOutput) {
  $resolvedOutput = (Resolve-Path -LiteralPath $releaseOutput).Path
  if (-not $resolvedOutput.Equals($releaseOutput, [StringComparison]::OrdinalIgnoreCase)) { throw '发布输出目录路径校验失败' }
  Get-ChildItem -LiteralPath $resolvedOutput -Force | ForEach-Object { Remove-OldFiles $_.FullName $_.PSIsContainer }
}
New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
Get-ChildItem -LiteralPath $root -File -Filter 'LoliaBloom-v*-win-x64.exe' | ForEach-Object { Remove-OldFiles $_.FullName }
Get-ChildItem -LiteralPath $root -File -Filter 'LoliaBloom-v*-win-x64.zip' | ForEach-Object { Remove-OldFiles $_.FullName }
foreach ($name in @('dist','release','payload.zip','frpc-upstream.zip','upstream-client')) {
  $old = Join-Path $root $name
  if (Test-Path -LiteralPath $old) {
    $resolvedOld = (Resolve-Path -LiteralPath $old).Path
    if (-not $resolvedOld.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "拒绝清理项目外的旧构建文件：$name" }
    Remove-OldFiles $resolvedOld $true
  }
}
$stage = Join-Path $env:TEMP ('LoliaBloom-release-' + $tag + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$dist = Join-Path $stage 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
& $DotnetPath run --project Lolia.Tests/Lolia.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw '本地回归测试失败' }
& $DotnetPath publish Lolia.App/Lolia.App.csproj -c Release -r win-x64 --self-contained false -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Windows 主程序发布失败' }
& $DotnetPath publish Lolia.Service/Lolia.Service.csproj -c Release -r win-x64 --self-contained true -o (Join-Path $dist 'Service')
if ($LASTEXITCODE -ne 0) { throw 'Windows 后台服务发布失败' }
$frpZip = Join-Path $stage 'frpc-upstream.zip'
Invoke-WebRequest 'https://github.com/Lolia-FRP/lolia-frp/releases/download/v0.71.2/LoliaFrp_windows_amd64.zip' -OutFile $frpZip
$frpHash = (Get-FileHash -LiteralPath $frpZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($frpHash -ne '208a9a4c6d4d6f0a3c07cd2257c821b8cf3d7e49b809b34e3d5f2367dcb9127e') { throw 'FRP 官方压缩包 SHA256 校验失败' }
$upstream = Join-Path $stage 'upstream-client'
Expand-Archive -LiteralPath $frpZip -DestinationPath $upstream -Force
$frpc = Get-ChildItem -LiteralPath $upstream -Filter frpc.exe -Recurse | Select-Object -First 1
if (-not $frpc) { throw 'FRP 压缩包内未找到 frpc.exe' }
Copy-Item -LiteralPath $frpc.FullName -Destination (Join-Path $dist 'frpc.exe') -Force
& $DotnetPath run --project Lolia.Tests/Lolia.Tests.csproj -c Release -- --engine-test $frpZip (Join-Path $dist 'frpc.exe')
if ($LASTEXITCODE -ne 0) { throw 'FRP 更新包验证失败' }
Copy-Item install-service.ps1,install-service.cmd,uninstall-service.cmd,README.md,README.zh-CN.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $dist -Force
Copy-Item docs,licenses -Destination $dist -Recurse -Force
$layoutLog = Join-Path $stage 'layout-test.log'
$env:DOTNET_ROOT_X64 = Split-Path $DotnetPath -Parent
$layout = Start-Process (Join-Path $dist 'Lolia.App.exe') -ArgumentList ('--layout-self-test "' + $layoutLog + '"') -PassThru
if (-not $layout.WaitForExit(45000)) { $layout.Kill(); throw '最小窗口和最大化布局检查超时' }
if ($layout.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $layoutLog)) { throw '最小窗口和最大化布局检查失败，未生成成功日志' }
Get-Content $layoutLog
$checkinLog = Join-Path $stage 'checkin-test.log'
$checkin = Start-Process (Join-Path $dist 'Lolia.App.exe') -ArgumentList ('--checkin-self-test "' + $checkinLog + '"') -PassThru
if (-not $checkin.WaitForExit(120000)) { $checkin.Kill(); throw '签到与持久计时检查超时' }
if ($checkin.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $checkinLog)) { throw '签到与持久计时检查失败，未生成成功日志' }
Get-Content $checkinLog
$payload = Join-Path $stage 'payload.zip'
$zipPath = Join-Path $releaseOutput $zipName
$exePath = Join-Path $releaseOutput $exeName
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
Copy-Item -LiteralPath $zipPath -Destination $payload -Force
& $DotnetPath publish Lolia.Launcher/Lolia.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:PayloadPath="$payload" -o (Join-Path $stage 'release')
if ($LASTEXITCODE -ne 0) { throw '单文件启动器发布失败' }
Copy-Item (Join-Path $stage 'release/LoliaBloom.exe') -Destination $exePath -Force
& $DotnetPath run --project Lolia.Tests/Lolia.Tests.csproj -c Release -- --update-launch-test $exePath
if ($LASTEXITCODE -ne 0) { throw '外层 EXE 更新交接或同目录保存检查失败' }
$hashExe = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashZip = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Item -LiteralPath $exePath).Length -ge 99000000 -or (Get-Item -LiteralPath $zipPath).Length -ge 99000000) { throw '安装包仍接近或超过 100 MB，请进一步精简后再发布' }
$checksums = Join-Path $releaseOutput 'SHA256SUMS.txt'
@("$hashExe  $exeName", "$hashZip  $zipName") | Set-Content -LiteralPath $checksums -Encoding ascii
Write-Host "已完成并验证 $exeName、$zipName；均低于 99 MB。文件位于 $releaseOutput"
if ($PublishRelease) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '需要安装并登录 GitHub CLI（gh）后才能上传 Release。' }
  gh auth status
  if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI 尚未登录' }
  gh release view $tag *> $null
  if ($LASTEXITCODE -eq 0) { throw "Release $tag 已存在；拒绝覆盖已发布版本。" }
  gh release create $tag $exePath $zipPath $checksums --title "洛链绽放 · Lolia Bloom $tag" --notes 'Windows x64 单文件版和 ZIP 版。单文件版缺少 .NET 10 Desktop Runtime 时会打开微软官方下载页。EXE 使用 GitHub Release SHA256 校验，可通过软件内更新。'
  if ($LASTEXITCODE -ne 0) { throw 'GitHub Release 上传失败' }
}
Remove-Item -LiteralPath $stage -Recurse -Force
