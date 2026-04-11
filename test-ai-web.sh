#!/bin/bash
# 测试 AI.Web 服务是否正常启动和运行

set -e

BASE_URL="http://localhost:5005"

echo "========================================="
echo "NetYamlForge.AI.Web 服务验证"
echo "========================================="
echo ""

# 1. 检查服务是否在运行
echo "1. 检查服务是否在运行..."
if curl -s --max-time 5 "$BASE_URL" > /dev/null 2>&1; then
    echo "✅ 服务正在运行"
else
    echo "❌ 服务未运行"
    exit 1
fi
echo ""

# 2. 检查健康状态（根路径）
echo "2. 检查健康状态..."
RESPONSE=$(curl -s --max-time 5 -o /dev/null -w "%{http_code}" "$BASE_URL")
if [ "$RESPONSE" = "200" ] || [ "$RESPONSE" = "404" ]; then
    echo "✅ 服务响应正常 (HTTP $RESPONSE)"
else
    echo "⚠️  服务响应异常 (HTTP $RESPONSE)"
fi
echo ""

# 3. 检查 SignalR Hubs 端点是否存在
echo "3. 检查 SignalR 端点..."
ENDPOINTS=(
    "/aiChatHub"
    "/aiProgressHub"
    "/aiDebateHub"
    "/nlQueryHub"
)

for endpoint in "${ENDPOINTS[@]}"; do
    RESPONSE=$(curl -s --max-time 5 -o /dev/null -w "%{http_code}" "$BASE_URL$endpoint")
    if [ "$RESPONSE" = "200" ] || [ "$RESPONSE" = "405" ]; then
        echo "✅ $endpoint (HTTP $RESPONSE)"
    else
        echo "⚠️  $endpoint (HTTP $RESPONSE)"
    fi
done
echo ""

# 4. 检查 API 控制器
echo "4. 检查 API 端点..."
API_ENDPOINTS=(
    "/api/ai/reports/preview?type=daily"
)

for endpoint in "${API_ENDPOINTS[@]}"; do
    RESPONSE=$(curl -s --max-time 5 -o /dev/null -w "%{http_code}" "$BASE_URL$endpoint")
    if [ "$RESPONSE" = "200" ] || [ "$RESPONSE" = "401" ]; then
        echo "✅ $endpoint (HTTP $RESPONSE)"
    else
        echo "⚠️  $endpoint (HTTP $RESPONSE)"
    fi
done
echo ""

# 5. 测试报告预览 API
echo "5. 测试报告预览 API..."
RESPONSE=$(curl -s --max-time 5 "$BASE_URL/api/ai/reports/preview?type=daily")
if echo "$RESPONSE" | grep -q "reportType"; then
    echo "✅ 报告预览 API 返回有效数据"
    echo "$RESPONSE" | head -c 200
else
    echo "⚠️  报告预览 API 响应异常"
    echo "$RESPONSE" | head -c 200
fi
echo ""
echo ""

echo "========================================="
echo "验证完成！"
echo "========================================="
