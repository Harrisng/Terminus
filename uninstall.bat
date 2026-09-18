@echo off
echo ====================================
echo Terminus Sleep Cycle Manager 卸載程序
echo ====================================
echo.

:: 檢查管理員權限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 請以管理員身份運行此卸載程序！
    echo 右鍵點擊此文件，選擇"以管理員身份運行"
    pause
    exit /b 1
)

:: 設置路徑
set INSTALL_DIR=C:\Program Files\Terminus
set STARTUP_DIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Terminus
set DESKTOP_SHORTCUT=%USERPROFILE%\Desktop\Terminus.lnk

echo 確定要卸載 Terminus 嗎？(Y/N)
set /p CONFIRM=
if /i not "%CONFIRM%"=="Y" (
    echo 取消卸載
    pause
    exit /b 0
)

:: 停止正在運行的程序
echo 正在停止 Terminus...
taskkill /F /IM Terminus.exe >nul 2>&1

:: 刪除註冊表項
echo 刪除註冊表項...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Terminus" /f >nul 2>&1
reg delete "HKCU\SOFTWARE\Terminus" /f >nul 2>&1

:: 刪除快捷方式
echo 刪除快捷方式...
if exist "%STARTUP_DIR%" rmdir /S /Q "%STARTUP_DIR%"
if exist "%DESKTOP_SHORTCUT%" del /F /Q "%DESKTOP_SHORTCUT%"

:: 刪除安裝目錄
echo 刪除程序文件...
if exist "%INSTALL_DIR%" (
    rmdir /S /Q "%INSTALL_DIR%"
)

:: 詢問是否刪除用戶數據
echo.
echo 是否刪除用戶數據（設置和緩存）？(Y/N)
set /p DELETE_DATA=
if /i "%DELETE_DATA%"=="Y" (
    echo 刪除用戶數據...
    if exist "%LOCALAPPDATA%\Terminus" rmdir /S /Q "%LOCALAPPDATA%\Terminus"
)

echo.
echo ====================================
echo 卸載完成！
echo ====================================
echo.
pause
