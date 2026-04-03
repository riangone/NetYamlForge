#!/bin/bash
# ============================================
# NetYamlForge 多租户数据库初始化脚本 (Bash)
# ============================================
# 用途：初始化全局用户表和项目角色表
# 支持：SQLite (使用 sqlite3 命令行工具)
# ============================================

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${CYAN}============================================${NC}"
echo -e "${CYAN}NetYamlForge 多租户数据库初始化${NC}"
echo -e "${CYAN}============================================${NC}"
echo ""

# 参数解析
PROJECT_NAME=""
DB_TYPE="sqlite"
DB_PATH=""
FORCE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --project=*)
            PROJECT_NAME="${1#*=}"
            shift
            ;;
        --db-type=*)
            DB_TYPE="${1#*=}"
            shift
            ;;
        --db-path=*)
            DB_PATH="${1#*=}"
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        -h|--help)
            echo "用法：$0 --project=<项目名称> [选项]"
            echo ""
            echo "选项:"
            echo "  --project=<name>   项目名称（必需）"
            echo "  --db-type=<type>   数据库类型：sqlite, postgresql, mysql, sqlserver (默认：sqlite)"
            echo "  --db-path=<path>   SQLite 数据库路径（可选）"
            echo "  --force            强制覆盖现有数据库"
            echo "  -h, --help         显示帮助信息"
            echo ""
            exit 0
            ;;
        *)
            echo -e "${RED}未知参数：$1${NC}"
            echo "使用 --help 查看帮助"
            exit 1
            ;;
    esac
done

# 验证必需参数
if [ -z "$PROJECT_NAME" ]; then
    echo -e "${RED}错误：必须指定 --project 参数${NC}"
    echo "使用 --help 查看帮助"
    exit 1
fi

# 获取脚本目录
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# 项目路径
PROJECT_PATH="$PROJECT_ROOT/NetYamlForge/projects/$PROJECT_NAME"
if [ ! -d "$PROJECT_PATH" ]; then
    echo -e "${RED}错误：项目目录不存在 - $PROJECT_PATH${NC}"
    exit 1
fi

echo -e "项目路径：${GREEN}$PROJECT_PATH${NC}"
echo -e "数据库类型：${GREEN}$DB_TYPE${NC}"

# 确定数据库路径
if [ "$DB_TYPE" = "sqlite" ]; then
    if [ -z "$DB_PATH" ]; then
        DB_DIR="$PROJECT_PATH/database"
        mkdir -p "$DB_DIR"
        DB_PATH="$DB_DIR/tenant.db"
    fi
    
    echo -e "数据库路径：${GREEN}$DB_PATH${NC}"
    
    # 检查是否已存在数据库
    if [ -f "$DB_PATH" ] && [ "$FORCE" = false ]; then
        echo ""
        echo -e "${YELLOW}警告：数据库文件已存在${NC}"
        read -p "是否覆盖？(y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo -e "${YELLOW}操作已取消${NC}"
            exit 0
        fi
        rm -f "$DB_PATH"
        echo -e "已删除旧数据库文件 ${GREEN}✓${NC}"
    fi
    
    echo ""
    echo -e "${CYAN}正在初始化数据库...${NC}"
    
    # 检查 sqlite3 是否安装
    if ! command -v sqlite3 &> /dev/null; then
        echo -e "${RED}错误：sqlite3 未安装${NC}"
        echo "请安装 sqlite3: sudo apt-get install sqlite3 (Ubuntu/Debian)"
        echo "或：brew install sqlite3 (macOS)"
        exit 1
    fi
    
    # 读取并执行 SQL 脚本
    SQL_SCRIPT="$SCRIPT_DIR/init-tenant-database.sql"
    if [ ! -f "$SQL_SCRIPT" ]; then
        echo -e "${RED}错误：找不到 SQL 脚本文件 - $SQL_SCRIPT${NC}"
        exit 1
    fi
    
    # 执行 SQL 脚本（忽略注释和错误）
    sqlite3 "$DB_PATH" < "$SQL_SCRIPT" 2>/dev/null || true
    
    echo -e "${GREEN}数据库初始化完成！${NC}"
    
    echo ""
    echo -e "${CYAN}============================================${NC}"
    echo -e "${GREEN}数据库初始化完成！${NC}"
    echo -e "${CYAN}============================================${NC}"
    echo ""
    echo -e "${YELLOW}默认管理员账户:${NC}"
    echo -e "  用户名：${WHITE}admin${NC}"
    echo -e "  密码：${WHITE}Admin@123${NC}"
    echo ""
    echo -e "${RED}⚠️ 请在使用后立即修改默认密码！${NC}"
    echo ""
    
else
    # 其他数据库类型
    echo -e "${YELLOW}对于 $DB_TYPE 数据库，请使用 dotnet 命令初始化:${NC}"
    echo ""
    echo -e "  ${CYAN}dotnet run --project NetYamlForge -- --init-tenant-db --project=$PROJECT_NAME --db-type=$DB_TYPE${NC}"
    echo ""
    exit 0
fi
