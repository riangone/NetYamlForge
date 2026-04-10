#!/bin/bash
# 汽车销售 AI 聊天测试脚本

BASE_URL="http://localhost:5000"
CONV_ID="test-conv-$(date +%s)"

echo "========================================="
echo "汽车销售 AI 聊天功能测试"
echo "========================================="
echo ""

# 测试 1: 发送顾客消息
echo "测试 1: 发送顾客消息..."
RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/auto-dealer-chat/session/$CONV_ID/send" \
  -H "Content-Type: application/json" \
  -d "{\"message\": \"こんにちは、車を見たいです\"}")

HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
BODY=$(echo "$RESPONSE" | sed '$d')

echo "HTTP 状态码: $HTTP_CODE"
echo "响应内容: $BODY" | head -c 500
echo ""
echo ""

if [ "$HTTP_CODE" = "200" ]; then
    echo "✅ 测试 1 通过"
else
    echo "❌ 测试 1 失败"
fi

echo ""

# 测试 2: 获取更新
echo "测试 2: 获取 AI 回复..."
sleep 2
UPDATES=$(curl -s "$BASE_URL/api/auto-dealer-chat/session/$CONV_ID/updates")
echo "更新内容: $UPDATES" | head -c 500
echo ""
echo ""

echo "========================================="
echo "测试完成"
echo "========================================="
