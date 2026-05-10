#!/bin/bash
# NetYamlForge.AI.Web 启动脚本
# 支持热重载（Hot Reload）模式

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/NetYamlForge.AI.Web"
FORCE_RESTART=false
WATCH_MODE=true

# 处理参数
for arg in "$@"; do
    if [ "$arg" == "--no-watch" ]; then
        WATCH_MODE=false
    fi
    if [ "$arg" == "--force" ]; then
        FORCE_RESTART=true
    fi
done

echo "========================================="
echo "NetYamlForge.AI.Web 启动脚本"
echo "模式: $([ "$WATCH_MODE" = true ] && echo "热重载 (Watch)" || echo "标准 (Run)")"
echo "强制模式: $([ "$FORCE_RESTART" = true ] && echo "开启" || echo "关闭")"
echo "========================================="
echo ""

# 检查是否已经在运行
if curl -s --max-time 2 http://localhost:5005 > /dev/null 2>&1; then
    echo "⚠️  服务似乎已经在运行中"
    
    if [ "$FORCE_RESTART" = true ]; then
        echo "正在停止旧服务 (--force 模式)..."
        pkill -f "NetYamlForge.AI.Web" || true
        sleep 2
    else
        echo "   如果要重启，请先运行: pkill -f NetYamlForge.AI.Web"
        echo ""
        # 仅在标准输入连接到终端时尝试读取
        if [ -t 0 ]; then
            read -p "是否继续重启？(y/N) " -n 1 -r
            echo
            if [[ ! $REPLY =~ ^[Yy]$ ]]; then
                echo "取消重启"
                exit 0
            fi
            echo "正在停止旧服务..."
            pkill -f "NetYamlForge.AI.Web" || true
            sleep 2
        else
            echo "❌ 非交互模式且未指定 --force。请使用 --force 强制重启或先停止旧进程。"
            exit 1
        fi
    fi
fi

# 构建项目
echo "1. 构建项目..."
cd "$SCRIPT_DIR"
dotnet build NetYamlForge.AI.Web/NetYamlForge.AI.Web.csproj --no-incremental
echo ""

# 启动服务
echo "2. 启动 AI.Web 服务..."
echo "   监听地址: http://localhost:5005"
[ "$WATCH_MODE" = true ] && echo "   🔥 已开启热重载模式，保存文件后将自动应用更改"
echo "   按 Ctrl+C 停止服务"
echo ""
echo "========================================="
echo ""

cd "$PROJECT_DIR"

if [ "$WATCH_MODE" = true ]; then
    # 使用 dotnet watch run 实现热启动
    # --non-interactive 避免 watch 模式下的用户输入阻塞
    dotnet watch run --non-interactive
else
    dotnet run
fi
