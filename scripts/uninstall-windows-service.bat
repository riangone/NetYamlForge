@echo off
REM ==========================================
REM NetYamlForge Windows サービスアンインストール
REM ==========================================
REM 管理者権限で実行してください

setlocal enabledelayedexpansion

REM 設定
set SERVICE_NAME=NetYamlForge

REM 確認
echo.
echo ========================================
echo NetYamlForge Windows サービスアンインストール
echo ========================================
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

REM サービス存在確認
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% neq 0 (
    echo [警告] サービス '%SERVICE_NAME%' が見つかりません。
    echo アンインストールは不要です。
    pause
    exit /b 0
)

REM 確認
set /p CONFIRM="サービス '%SERVICE_NAME%' を削除しますか？ (Y/N): "
if /i "!CONFIRM!" neq "Y" (
    echo アンインストールをキャンセルしました。
    pause
    exit /b 0
)

REM サービス停止
echo [処理] サービスを停止しています...
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul

REM サービス削除
echo [処理] サービスを削除しています...
sc delete %SERVICE_NAME%

if %errorLevel% neq 0 (
    echo [エラー] サービスの削除に失敗しました。
    pause
    exit /b 1
)

echo.
echo ========================================
echo アンインストール完了
echo ========================================
echo.
echo サービス '%SERVICE_NAME%' を削除しました。
echo.
echo 必要に応じて、アプリケーションディレクトリも手動で削除してください。
echo.

pause
