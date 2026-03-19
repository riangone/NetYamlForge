# ==========================================
# NetYamlForge Windows サービスインストール (PowerShell)
# ==========================================
# 管理者権限で実行してください

param(
    [switch]$Uninstall,
    [switch]$NoPrompt
)

$SERVICE_NAME = "NetYamlForge"
$SERVICE_DISPLAY_NAME = "NetYamlForge Web Application"
$SERVICE_DESCRIPTION = "NetYamlForge - YAML 駆動型 ASP.NET Core MVC 低開発フレームワーク"

# スクリプトのディレクトリ
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$APP_EXE = Join-Path $SCRIPT_DIR "NetYamlForge.exe"

function Write-Header {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "NetYamlForge Windows サービス" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-Service {
    Write-Header
    Write-Host "インストール設定:" -ForegroundColor Yellow
    Write-Host "  サービス名：$SERVICE_NAME"
    Write-Host "  表示名：$SERVICE_DISPLAY_NAME"
    Write-Host "  アプリケーション：$APP_EXE"
    Write-Host ""

    # 管理者権限チェック
    if (-not (Test-Administrator)) {
        Write-Host "[エラー] 管理者権限が必要です。" -ForegroundColor Red
        Write-Host "PowerShell を「管理者として実行」してください。" -ForegroundColor Yellow
        if (-not $NoPrompt) { Pause }
        exit 1
    }

    Write-Host "[確認] 管理者権限を確認しました。" -ForegroundColor Green

    # 実行ファイル確認
    if (-not (Test-Path $APP_EXE)) {
        Write-Host "[エラー] アプリケーションが見つかりません：$APP_EXE" -ForegroundColor Red
        Write-Host "このスクリプトを NetYamlForge.exe と同じディレクトリに配置してください。" -ForegroundColor Yellow
        if (-not $NoPrompt) { Pause }
        exit 1
    }

    Write-Host "[確認] アプリケーションを確認しました。" -ForegroundColor Green
    Write-Host ""

    # 既存サービス確認
    $existingService = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "[警告] サービス '$SERVICE_NAME' が既に存在します。" -ForegroundColor Yellow
        
        if (-not $NoPrompt) {
            $overwrite = Read-Host "既存のサービスを上書きしますか？ (Y/N)"
            if ($overwrite -ne 'Y' -and $overwrite -ne 'y') {
                Write-Host "インストールをキャンセルしました。" -ForegroundColor Yellow
                Pause
                exit 0
            }
        }

        Write-Host "[処理] 既存のサービスを削除します..." -ForegroundColor Cyan
        Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe delete $SERVICE_NAME
        Start-Sleep -Seconds 2
    }

    # サービスインストール
    Write-Host "[処理] サービスをインストールします..." -ForegroundColor Cyan
    
    $binPath = "`"$APP_EXE`" --run-as-service"
    sc.exe create $SERVICE_NAME `
        binPath= $binPath `
        displayName= $SERVICE_DISPLAY_NAME `
        start= auto

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[エラー] サービスのインストールに失敗しました。" -ForegroundColor Red
        if (-not $NoPrompt) { Pause }
        exit 1
    }

    Write-Host "[成功] サービスをインストールしました。" -ForegroundColor Green

    # サービス説明設定
    sc.exe description $SERVICE_NAME $SERVICE_DESCRIPTION

    # サービスアカウント設定
    sc.exe config $SERVICE_NAME obj= LocalSystem

    # 回復アクション設定
    sc.exe failure $SERVICE_NAME `
        reset= 86400 `
        actions= restart/60000/restart/60000/restart/60000

    Write-Host "[成功] サービスの設定を完了しました。" -ForegroundColor Green
    Write-Host ""

    # サービス開始
    if (-not $NoPrompt) {
        $startService = Read-Host "サービスを開始しますか？ (Y/N)"
        if ($startService -eq 'Y' -or $startService -eq 'y') {
            Write-Host "[処理] サービスを開始しています..." -ForegroundColor Cyan
            Start-Service -Name $SERVICE_NAME
            if ($?) {
                Write-Host "[成功] サービスを開始しました。" -ForegroundColor Green
            } else {
                Write-Host "[警告] サービスの開始に失敗しました。" -ForegroundColor Yellow
            }
        }
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "インストール完了" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "操作コマンド:" -ForegroundColor Yellow
    Write-Host "  開始：Start-Service $SERVICE_NAME"
    Write-Host "  停止：Stop-Service $SERVICE_NAME"
    Write-Host "  削除：sc.exe delete $SERVICE_NAME"
    Write-Host ""
    Write-Host "ログの場所:" -ForegroundColor Yellow
    Write-Host "  $SCRIPT_DIR\logs\"
    Write-Host ""
    Write-Host "ブラウザでアクセス:" -ForegroundColor Yellow
    Write-Host "  http://localhost:5000"
    Write-Host ""

    if (-not $NoPrompt) { Pause }
}

function Uninstall-Service {
    Write-Header
    Write-Host "アンインストール設定:" -ForegroundColor Yellow
    Write-Host "  サービス名：$SERVICE_NAME"
    Write-Host ""

    # 管理者権限チェック
    if (-not (Test-Administrator)) {
        Write-Host "[エラー] 管理者権限が必要です。" -ForegroundColor Red
        Write-Host "PowerShell を「管理者として実行」してください。" -ForegroundColor Yellow
        if (-not $NoPrompt) { Pause }
        exit 1
    }

    # サービス存在確認
    $existingService = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if (-not $existingService) {
        Write-Host "[警告] サービス '$SERVICE_NAME' が見つかりません。" -ForegroundColor Yellow
        Write-Host "アンインストールは不要です。" -ForegroundColor Yellow
        if (-not $NoPrompt) { Pause }
        exit 0
    }

    if (-not $NoPrompt) {
        $confirm = Read-Host "サービス '$SERVICE_NAME' を削除しますか？ (Y/N)"
        if ($confirm -ne 'Y' -and $confirm -ne 'y') {
            Write-Host "アンインストールをキャンセルしました。" -ForegroundColor Yellow
            Pause
            exit 0
        }
    }

    # サービス停止
    Write-Host "[処理] サービスを停止しています..." -ForegroundColor Cyan
    Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    # サービス削除
    Write-Host "[処理] サービスを削除しています..." -ForegroundColor Cyan
    sc.exe delete $SERVICE_NAME

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[エラー] サービスの削除に失敗しました。" -ForegroundColor Red
        if (-not $NoPrompt) { Pause }
        exit 1
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "アンインストール完了" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "サービス '$SERVICE_NAME' を削除しました。" -ForegroundColor Green
    Write-Host ""
    Write-Host "必要に応じて、アプリケーションディレクトリも手動で削除してください。" -ForegroundColor Yellow
    Write-Host ""

    if (-not $NoPrompt) { Pause }
}

# メイン処理
if ($Uninstall) {
    Uninstall-Service
} else {
    Install-Service
}
