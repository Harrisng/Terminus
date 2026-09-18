@echo off
echo ====================================
echo Terminus Sleep Cycle Manager 安裝程序
echo ====================================
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

echo.
echo 按任意鍵退出...
pause >nul
