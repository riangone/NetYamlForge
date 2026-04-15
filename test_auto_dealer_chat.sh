#!/bin/bash
# 汽车销售 AI 聊天测试脚本 (修正版)

BASE_URL="http://localhost:5000"
PROJECT="auto-dealer-demo"
API_URL="$BASE_URL/$PROJECT/api/ai/chat"

echo "========================================="
echo "汽车销售 AI 聊天功能测试"
echo "========================================="
echo ""

# 测试 1: 开始会话
echo "测试 1: 开始会话 (StartSession)..."
SESSION_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_URL/session" \
  -H "Content-Type: application/json" \
  -d '{"guestSessionId": "test-guest-123", "channel": "web"}')

HTTP_CODE=$(echo "$SESSION_RESPONSE" | tail -n1)
BODY=$(echo "$SESSION_RESPONSE" | sed '$d')

echo "HTTP 状态码: $HTTP_CODE"
echo "响应内容: $BODY"

if [ "$HTTP_CODE" != "200" ]; then
    echo "❌ 测试 1 失败"
    exit 1
fi

CONV_ID=$(echo "$BODY" | grep -o '"conversationId":"[^"]*' | cut -d'"' -f4)
echo "✅ 会话已开始: $CONV_ID"
echo ""

# 测试 2: 发送顾客消息
echo "测试 2: 发送顾客消息 (SendMessage)..."
SEND_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_URL/session/$CONV_ID/message" \
  -H "Content-Type: application/json" \
  -d '{"message": "こんにちは、最新の SUV について教えてください"}' )

HTTP_CODE=$(echo "$SEND_RESPONSE" | tail -n1)
BODY=$(echo "$SEND_RESPONSE" | sed '$d')

echo "HTTP 状态码: $HTTP_CODE"
echo "响应内容: $BODY" | head -c 500

if [ "$HTTP_CODE" = "200" ]; then
    echo "✅ 测试 2 通过"
else
    echo "❌ 测试 2 失败"
    exit 1
fi

echo ""

# 测试 3: 获取更新
echo "测试 3: 获取 AI 回复 (GetUpdates)..."
sleep 2
UPDATES=$(curl -s "$API_URL/session/$CONV_ID/updates")
echo "更新内容: $UPDATES" | head -c 500
echo ""

echo "========================================="
echo "测试完成"
echo "========================================="
