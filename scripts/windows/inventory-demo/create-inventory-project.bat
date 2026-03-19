@echo off
REM ============================================================================
REM NetYamlForge 在庫管理システム 自動生成バッチファイル
REM ============================================================================
REM 概要:
REM   PowerShell スクリプトを呼び出して在庫管理システムを自動生成します。
REM
REM 使用方法:
REM   このファイルをダブルクリックするか、コマンドプロンプトから実行：
REM   .\create-inventory-project.bat
REM
REM 前提条件:
REM   - .NET 10.0 SDK がインストールされていること
REM   - PowerShell 5.0 以上が利用可能であること
REM   - SQLite がインストールされていること (オプション)
REM
REM 著者：NetYamlForge Team
REM 更新日：2026-03-19
REM ============================================================================

chcp 65001 >nul
setlocal enabledelayedexpansion

echo.
echo ============================================================================
echo   NetYamlForge 在庫管理システム 自動生成スクリプト
echo ============================================================================
echo.

REM スクリプトディレクトリの取得
set "SCRIPT_DIR=%~dp0"

REM PowerShell の実行ポリシーを確認
echo [INFO] PowerShell の実行ポリシーを確認中...

REM 管理者権限で実行されているか確認
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [WARN] 管理者権限で実行されていません。管理者として再試行します...
    echo.
    
    REM 管理者権限で再実行
    powershell -Command "Start-Process cmd -ArgumentList '/c', '%~f0' -Verb RunAs"
    exit /b
)

REM PowerShell スクリプトのパス設定
set "PS_SCRIPT=%SCRIPT_DIR%New-InventoryProject.ps1"

REM PowerShell スクリプトが存在するか確認
if not exist "%PS_SCRIPT%" (
    echo [ERROR] PowerShell スクリプトが見つかりません：%PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

echo [INFO] PowerShell スクリプトを実行中...
echo.

REM PowerShell スクリプトの実行
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*

if %errorLevel% neq 0 (
    echo.
    echo [ERROR] スクリプトの実行に失敗しました。
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo   処理が完了しました。
echo ============================================================================
echo.

pause
