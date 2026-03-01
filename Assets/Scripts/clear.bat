@echo off
:: 设置代码页为 UTF-8 避免中文乱码
chcp 65001 >nul

:: ==========================================
:: 自动提权逻辑 (UAC)
:: ==========================================
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :AdminStart
) else (
    echo [INFO] 正在请求管理员权限...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:AdminStart
title Windows 2026 现代化开发环境深度清理工具
echo [OK] 已获得管理员权限
echo ======================================================

:: 1. 清理远程桌面缓存 (RDP Cache)
echo 正在清理远程桌面缓存...
set "RDP_PATH=C:\Users\leipengcheng\AppData\Local\Microsoft\Terminal Server Client\Cache"
if exist "%RDP_PATH%" (
    del /f /s /q "%RDP_PATH%\*" 2>nul
    echo [完成] 清理了 RDP 缓存
)

:: 2. 清理 Edge 浏览器缓存
echo 正在清理 Edge 缓存...
set "EDGE_PATH=C:\Users\leipengcheng\AppData\Local\Microsoft\Edge\User Data"
del /f /s /q "%EDGE_PATH%\component_crx_cache\*" 2>nul
del /f /s /q "%EDGE_PATH%\GrShaderCache\*" 2>nul
del /f /s /q "%EDGE_PATH%\ShaderCache\*" 2>nul
echo [完成]

:: 3. 清理系统崩溃转储文件
echo 正在清理 CrashDumps...
del /f /s /q "%LOCALAPPDATA%\CrashDumps\*" 2>nul
echo [完成]

:: 4. 清理通用 .cache 目录
echo 正在清理 .cache 目录...
if exist "%LOCALAPPDATA%\.cache" (
    rd /s /q "%LOCALAPPDATA%\.cache" 2>nul
    echo [完成]
)

:: 5. 清理系统临时文件
echo 正在清理 Temp...
del /f /s /q "%temp%\*" 2>nul
del /f /s /q "C:\Windows\Temp\*" 2>nul
echo [完成]

:: 6. 清理 Windows 更新缓存
echo 正在清理 Windows 更新缓存...
net stop wuauserv >nul 2>&1
del /f /s /q "C:\Windows\SoftwareDistribution\Download\*" 2>nul
net start wuauserv >nul 2>&1
echo [完成]

echo ======================================================
echo 深度清理结束！
pause