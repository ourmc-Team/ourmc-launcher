<#
.SYNOPSIS
    OML Launcher 一键构建脚本
.DESCRIPTION
    清理 → 还原依赖 → 发布单文件自包含 exe → 打包到 release/ 目录
#>

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   OML Launcher - 构建脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查项目文件
if (-not (Test-Path "ourmclauncher.csproj")) {
    Write-Host "[错误] 未找到项目文件，请确保在项目根目录运行！" -ForegroundColor Red
    exit 1
}

# Step 1: 清理编译缓存
Write-Host "════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host "[1/4] 清理编译缓存..." -ForegroundColor Yellow
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }
if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
Write-Host "      ✓ 缓存已清理" -ForegroundColor Green
Write-Host ""

# Step 2: 还原 NuGet 包
Write-Host "[2/4] 还原 NuGet 依赖..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "还原 NuGet 包失败" }
Write-Host "      ✓ 依赖已还原" -ForegroundColor Green
Write-Host ""

# Step 3: 发布项目
Write-Host "[3/4] 发布项目 (Release, win-x64, 自包含)..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o ./publish
if ($LASTEXITCODE -ne 0) { throw "发布失败" }
Write-Host "      ✓ 发布完成" -ForegroundColor Green
Write-Host ""

# Step 4: 打包到 release/
Write-Host "[4/4] 打包发布文件..." -ForegroundColor Yellow
$exePath = "publish\ourmclauncher.exe"

if (-not (Test-Path $exePath)) {
    throw "未找到输出文件: $exePath"
}

$releaseDir = "release"
if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir | Out-Null }

Copy-Item $exePath (Join-Path $releaseDir "ourmclauncher.exe") -Force

@"
OML Launcher - 我们的世界启动器
=======================================
版本: 1.0.0
构建日期: $(Get-Date -Format "yyyy-MM-dd HH:mm")

使用说明:
  双击 ourmclauncher.exe 即可启动。
  已内置 .NET 8 运行时，无需额外安装。

系统要求:
  - Windows 10/11 64位
  - WebView2 Runtime（Win11 自带，Win10 通常已预装）
  - 4GB+ RAM
  - Java 8+（启动游戏需要）

官网: https://www.our-mc.cn
皮肤站: https://skin.our-mc.cn
"@ | Out-File (Join-Path $releaseDir "README.txt") -Encoding UTF8

Write-Host "      ✓ 打包完成" -ForegroundColor Green
Write-Host ""

# 完成
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  构建成功！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "输出文件:" -ForegroundColor White
Write-Host "  EXE:  release\ourmclauncher.exe（单文件，内置 .NET + 静态资源）" -ForegroundColor White
Write-Host "  说明: release\README.txt" -ForegroundColor White
Write-Host ""
Write-Host "文件大小: $('{0:N1} MB' -f ((Get-Item (Join-Path $releaseDir 'ourmclauncher.exe')).Length / 1MB))" -ForegroundColor White
Write-Host ""

$open = Read-Host "打开 release 目录？(Y/N)"
if ($open -eq "Y" -or $open -eq "y") {
    Invoke-Item $releaseDir
}

