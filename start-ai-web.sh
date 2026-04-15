#!/bin/bash
# NetYamlForge.AI.Web 启动脚本
# 用于启动独立的 AI 聊天服务

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/NetYamlForge.AI.Web"

echo "========================================="
echo "NetYamlForge.AI.Web 启动脚本"
echo "========================================="
echo ""

# 检查是否已经在运行
if curl -s --max-time 3 http://localhost:5005 > /dev/null 2>&1; then
    echo "⚠️  服务似乎已经在运行中"
    echo "   如果要重启，请先运行: pkill -f NetYamlForge.AI.Web"
    echo ""
    read -p "是否继续重启？(y/N) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "取消重启"
        exit 0
    fi
    
    echo "正在停止旧服务..."
    pkill -f "NetYamlForge.AI.Web" || true
    sleep 2
fi

# 构建项目
echo "1. 构建项目..."
cd "$SCRIPT_DIR"
dotnet build NetYamlForge.AI.Web/NetYamlForge.AI.Web.csproj --no-incremental
echo ""

# 启动服务
echo "2. 启动 AI.Web 服务..."
echo "   监听地址: http://localhost:5005"
echo "   按 Ctrl+C 停止服务"
echo ""
echo "========================================="
echo ""

cd "$PROJECT_DIR"
dotnet run
