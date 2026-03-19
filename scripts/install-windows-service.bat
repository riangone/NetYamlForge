@echo off
REM ==========================================
REM NetYamlForge Windows サービスインストール
REM ==========================================
REM 管理者権限で実行してください

setlocal enabledelayedexpansion

REM 設定
set SERVICE_NAME=NetYamlForge
set SERVICE_DISPLAY_NAME=NetYamlForge Web Application
set SERVICE_DESCRIPTION=NetYamlForge - YAML 駆動型 ASP.NET Core MVC 低開発フレームワーク

REM スクリプトのディレクトリを取得
set SCRIPT_DIR=%~dp0

REM アプリケーションディレクトリ（スクリプトと同じディレクトリ）
set APP_DIR=%SCRIPT_DIR%

REM 実行ファイル
set APP_EXE=%APP_DIR%NetYamlForge.exe

REM 確認
echo.
echo ========================================
echo NetYamlForge Windows サービスインストール
echo ========================================
echo.
echo インストール設定:
echo   サービス名：%SERVICE_NAME%
echo   表示名：%SERVICE_DISPLAY_NAME%
echo   アプリケーション：%APP_EXE%
echo.

REM 管理者権限チェック
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [エラー] 管理者権限が必要です。
    echo このスクリプトを右クリックして「管理者として実行」してください。
    pause
    exit /b 1
)

echo [確認] 管理者権限を確認しました。
echo.

REM 実行ファイルの存在確認
if not exist "%APP_EXE%" (
    echo [エラー] アプリケーションが見つかりません：%APP_EXE%
    echo.
    echo このスクリプトを NetYamlForge.exe と同じディレクトリに配置してください。
    pause
    exit /b 1
)

echo [確認] アプリケーションを確認しました。
echo.

REM 既存サービスの確認
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    echo [警告] サービス '%SERVICE_NAME%' が既に存在します。
    set /p OVERWRITE="既存のサービスを上書きしますか？ (Y/N): "
    if /i "!OVERWRITE!" neq "Y" (
        echo インストールをキャンセルしました。
        pause
        exit /b 0
    )
    
    echo [処理] 既存のサービスを削除します...
    sc stop %SERVICE_NAME% >nul 2>&1
    sc delete %SERVICE_NAME%
    timeout /t 2 /nobreak >nul
)

REM サービスインストール
echo [処理] サービスをインストールします...
sc create %SERVICE_NAME% ^
    binPath= "\"%APP_EXE%\" --run-as-service" ^
    displayName= "%SERVICE_DISPLAY_NAME%" ^
    start= auto

if %errorLevel% neq 0 (
    echo [エラー] サービスのインストールに失敗しました。
    pause
    exit /b 1
)

echo [成功] サービスをインストールしました。

REM サービスの説明を設定
sc description %SERVICE_NAME% "%SERVICE_DESCRIPTION%"

REM サービスアカウント設定（ローカルシステムアカウント）
sc config %SERVICE_NAME% obj= LocalSystem

REM 回復アクション（失敗時に自動再起動）
sc failure %SERVICE_NAME% ^
    reset= 86400 ^
    actions= restart/60000/restart/60000/restart/60000

echo [成功] サービスの設定を完了しました。
echo.

REM サービス開始
set /p START_SERVICE="サービスを開始しますか？ (Y/N): "
if /i "!START_SERVICE!" equ "Y" (
    echo [処理] サービスを開始しています...
    sc start %SERVICE_NAME%
    if %errorLevel% equ 0 (
        echo [成功] サービスを開始しました。
    ) else (
        echo [警告] サービスの開始に失敗しました。後で手動で開始してください。
    )
)

echo.
echo ========================================
echo インストール完了
echo ========================================
echo.
echo 操作コマンド:
echo   開始：net start %SERVICE_NAME%
echo   停止：net stop %SERVICE_NAME%
echo   削除：sc delete %SERVICE_NAME%
echo.
echo ログの場所:
echo   %APP_DIR%logs\
echo.
echo ブラウザでアクセス:
echo   http://localhost:5000
echo   (ポートは appsettings.json で設定)
echo.

pause
