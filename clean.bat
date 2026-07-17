@echo off
echo ========================================
echo   清理编译缓存
echo ========================================
echo.

if not exist "ourmclauncher.csproj" (
    echo [错误] 未找到项目文件，请确保在项目根目录运行此脚本！
    pause
    exit /b 1
)

echo 正在清理编译缓存...
if exist obj (
    rmdir /s /q obj
    echo     ✓ obj 目录已清理
)

if exist bin (
    rmdir /s /q bin
    echo     ✓ bin 目录已清理
)

echo.
echo ========================================
echo   清理完成！
echo ========================================
echo.
echo 现在可以安全地分享源码了
echo.
pause
