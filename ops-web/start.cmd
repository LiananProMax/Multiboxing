@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title 运维构建网站

where node >nul 2>nul
if errorlevel 1 (
  echo [错误] 未找到 node，请先安装 Node.js 18 以上版本。
  pause
  exit /b 1
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [警告] 未找到 dotnet，构建时会失败。请确认已安装 .NET SDK。
)

if not exist "node_modules" (
  echo 首次运行，正在安装依赖…
  call npm install
  if errorlevel 1 (
    echo [错误] npm install 失败。
    pause
    exit /b 1
  )
)

if not exist "dist\index.html" (
  echo 正在构建前端…
  call npm run build
  if errorlevel 1 (
    echo [错误] 前端构建失败。
    pause
    exit /b 1
  )
)

if "%PORT%"=="" set PORT=4170
echo 正在启动运维构建网站：http://127.0.0.1:%PORT%
start "" "http://127.0.0.1:%PORT%"
call npm start
pause
