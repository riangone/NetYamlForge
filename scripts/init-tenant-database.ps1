#!/usr/bin/env pwsh
# ============================================
# NetYamlForge 多租户数据库初始化脚本 (PowerShell)
# ============================================
# 用途：初始化全局用户表和项目角色表
# 支持：SQLite, PostgreSQL, MySQL, SQL Server
# ============================================

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectName,
    
    [Parameter(Mandatory = $false)]
    [ValidateSet('sqlite', 'postgresql', 'mysql', 'sqlserver')]
    [string]$DbType = 'sqlite',
    
    [Parameter(Mandatory = $false)]
    [string]$DbPath,
    
    [Parameter(Mandatory = $false)]
    [string]$ConnectionString,
    
    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# 错误处理
$ErrorActionPreference = 'Stop'

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "NetYamlForge 多租户数据库初始化" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# 获取项目路径
$projectPath = Join-Path $PSScriptRoot ".." "NetYamlForge" "projects" $ProjectName
if (-not (Test-Path $projectPath)) {
    Write-Host "错误：项目目录不存在 - $projectPath" -ForegroundColor Red
    exit 1
}

Write-Host "项目路径：$projectPath" -ForegroundColor Green
Write-Host "数据库类型：$DbType" -ForegroundColor Green

# 确定数据库连接字符串
if ($DbType -eq 'sqlite') {
    if ([string]::IsNullOrWhiteSpace($DbPath)) {
        $dbDir = Join-Path $projectPath "database"
        if (-not (Test-Path $dbDir)) {
            New-Item -ItemType Directory -Path $dbDir | Out-Null
        }
        $DbPath = Join-Path $dbDir "tenant.db"
    }
    $ConnectionString = "Data Source=$DbPath"
    Write-Host "数据库路径：$DbPath" -ForegroundColor Green
} else {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        Write-Host "错误：对于 $DbType 类型，必须提供 -ConnectionString 参数" -ForegroundColor Red
        exit 1
    }
}

# 检查是否已存在数据库
if ($DbType -eq 'sqlite' -and (Test-Path $DbPath) -and -not $Force) {
    Write-Host ""
    Write-Host "警告：数据库文件已存在" -ForegroundColor Yellow
    $overwrite = Read-Host "是否覆盖？(y/N)"
    if ($overwrite -ne 'y' -and $overwrite -ne 'Y') {
        Write-Host "操作已取消" -ForegroundColor Yellow
        exit 0
    }
    Remove-Item $DbPath -Force
    Write-Host "已删除旧数据库文件" -ForegroundColor Green
}

Write-Host ""
Write-Host "正在初始化数据库..." -ForegroundColor Cyan

try {
    # 加载 Dapper
    Add-Type -AssemblyName System.Data
    
    # 对于 SQLite，使用 Microsoft.Data.Sqlite
    if ($DbType -eq 'sqlite') {
        # 检查是否已安装 Sqlite NuGet 包
        try {
            Add-Type -AssemblyName Microsoft.Data.Sqlite
        } catch {
            Write-Host "正在安装 Microsoft.Data.Sqlite..." -ForegroundColor Yellow
            dotnet add package Microsoft.Data.Sqlite --source https://api.nuget.org/v3/index.json | Out-Null
            Add-Type -AssemblyName Microsoft.Data.Sqlite
        }
        
        $connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($ConnectionString)
        $connection.Open()
        
        Write-Host "SQLite 数据库连接成功" -ForegroundColor Green
        
        # 读取 SQL 脚本
        $sqlScriptPath = Join-Path $PSScriptRoot "init-tenant-database.sql"
        if (-not (Test-Path $sqlScriptPath)) {
            Write-Host "错误：找不到 SQL 脚本文件 - $sqlScriptPath" -ForegroundColor Red
            exit 1
        }
        
        $sqlScript = Get-Content $sqlScriptPath -Raw -Encoding UTF8
        
        # 分割 SQL 语句（按分号分隔）
        $statements = $sqlScript -split '(?m)^;\s*$'
        
        $executedCount = 0
        foreach ($statement in $statements) {
            $statement = $statement.Trim()
            if ([string]::IsNullOrWhiteSpace($statement)) {
                continue
            }
            
            # 跳过注释-only 语句
            if ($statement -match '^\s*--') {
                continue
            }
            
            try {
                $command = $connection.CreateCommand()
                $command.CommandText = $statement
                $command.ExecuteNonQuery() | Out-Null
                $executedCount++
            } catch {
                # 忽略已存在对象的错误
                if ($_.Exception.Message -match 'already exists|duplicate') {
                    continue
                }
                throw
            }
        }
        
        Write-Host "成功执行 $executedCount 条 SQL 语句" -ForegroundColor Green
        $connection.Close()
        
    } else {
        # 其他数据库类型，使用 Dapper
        Write-Host "对于 $DbType 数据库，请使用 dotnet 运行以下命令：" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  dotnet run --project NetYamlForge -- --init-tenant-db --project=$ProjectName --db-type=$DbType --connection-string=`"$ConnectionString`"" -ForegroundColor Cyan
        Write-Host ""
        exit 0
    }
    
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "数据库初始化完成！" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "默认管理员账户:" -ForegroundColor Yellow
    Write-Host "  用户名：admin" -ForegroundColor White
    Write-Host "  密码：Admin@123" -ForegroundColor White
    Write-Host ""
    Write-Host "⚠️ 请在使用后立即修改默认密码！" -ForegroundColor Red
    Write-Host ""
    
} catch {
    Write-Host ""
    Write-Host "错误：数据库初始化失败" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
}
