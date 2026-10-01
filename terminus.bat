@echo off
chcp 65001 >nul
echo ====================================
echo Terminus Sleep Cycle Manager 管理工具
echo ====================================
echo.
echo 1. 安裝
echo 2. 卸載
echo 3. 離開
echo.
set /p CHOICE=請選擇操作 (1/2/3):

if "%CHOICE%"=="1" goto install
if "%CHOICE%"=="2" goto uninstall
goto end

:install
echo.
:: 檢查管理員權限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 請以管理員身份運行此安裝程序！
    echo 右鍵點擊此文件，選擇"以管理員身份運行"
    pause
    exit /b 1
)

:: 設置安裝路徑
set INSTALL_DIR=C:\Program Files\Terminus
set STARTUP_DIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Terminus

echo 安裝路徑: %INSTALL_DIR%
echo.

:: 創建安裝目錄
if not exist "%INSTALL_DIR%" (
    echo 創建安裝目錄...
    mkdir "%INSTALL_DIR%"
)

:: 複製文件
echo 正在複製程序文件...
xcopy /E /I /Y "%~dp0publish\*" "%INSTALL_DIR%\"

:: 創建開始菜單快捷方式
echo 創建開始菜單快捷方式...
if not exist "%STARTUP_DIR%" mkdir "%STARTUP_DIR%"
powershell -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%STARTUP_DIR%\Terminus.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\Terminus.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'Terminus Sleep Cycle Manager'; $Shortcut.Save()"

:: 創建桌面快捷方式（可選）
echo 是否創建桌面快捷方式？(Y/N)
set /p CREATE_DESKTOP=
if /i "%CREATE_DESKTOP%"=="Y" (
    echo 創建桌面快捷方式...
    powershell -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%USERPROFILE%\Desktop\Terminus.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\Terminus.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'Terminus Sleep Cycle Manager'; $Shortcut.Save()"
)

:: 添加到開機啟動（可選）
echo.
echo 是否設置開機自動啟動？(Y/N)
set /p AUTO_START=
if /i "%AUTO_START%"=="Y" (
    echo 設置開機自動啟動...
    reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Terminus" /t REG_SZ /d "\"%INSTALL_DIR%\Terminus.exe\"" /f
)

:: 添加到系統路徑（可選）
echo.
echo 是否添加到系統環境變量 PATH？(Y/N)
set /p ADD_PATH=
if /i "%ADD_PATH%"=="Y" (
    echo 添加到系統 PATH...
    setx PATH "%PATH%;%INSTALL_DIR%" /M
)

echo.
echo ====================================
echo 安裝完成！
echo ====================================
echo.
echo 安裝位置: %INSTALL_DIR%
echo 開始菜單: %STARTUP_DIR%
echo.
echo 現在啟動 Terminus？(Y/N)
set /p RUN_NOW=
if /i "%RUN_NOW%"=="Y" (
    start "" "%INSTALL_DIR%\Terminus.exe"
)
goto end

:uninstall
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
    goto end
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

:end
echo.
pause
