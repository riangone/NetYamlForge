#!/bin/bash
# 测试 AI 顾客数查询 - 最终测试

echo "======================================"
echo "AI 顧客数查询 - 最终测试"
echo "======================================"
echo ""

# 测试 1: 直接测试 Qwen CLI 的 JSON 数组输出
echo "【测试 1】Qwen CLI JSON 数组输出测试"
echo "--------------------------------------"
cd /home/ubuntu/ws/NetYamlForge

RESPONSE=$(timeout 35 qwen --yolo --prompt "
あなたは自動車ディーラーの AI アシスタントです。
DB データが必要な場合は、以下の JSON**だけ**を出力してください:
{\"tool_call\":\"query_data\",\"entity\":\"customers\",\"action\":\"count\"}

顧客数は？
" --output-format json --model qwen2.5-coder:7b 2>&1)

echo "CLI 响应（前 500 字符）:"
echo "$RESPONSE" | head -c 500
echo ""
echo "..."
echo ""

# 检查是否包含 tool_call
if echo "$RESPONSE" | grep -q '"tool_call":"query_data"'; then
    echo "✅ 测试 1 通过：CLI 返回了 tool_call JSON"
else
    echo "❌ 测试 1 失败：CLI 没有返回 tool_call JSON"
fi

echo ""
echo "======================================"
echo "【测试 2】应用日志测试"
echo "--------------------------------------"
echo ""
echo "请访问：http://localhost:5000/auto-dealer-demo"
echo "1. 登录系统"
echo "2. 打开 AI 聊天窗口"
echo "3. 输入：現在の顧客数は？"
echo ""
echo "然后查看日志："
echo "tail -f /home/ubuntu/ws/NetYamlForge/NetYamlForge/logs/app-*.log | grep -i 'tool_call\\|CLI 応答'"
echo ""
echo "预期日志输出："
echo "  🔧 CLI が tool_call JSON を返した provider=qwen"
echo "  CLI 応答成功 provider=qwen"
echo ""
echo "======================================"
echo "测试完成！"
echo "======================================"
