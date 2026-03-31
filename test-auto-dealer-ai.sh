#!/bin/bash
# auto-dealer-ai-test.sh - AI 顧客数查询测试脚本

echo "======================================"
echo "auto-dealer-demo AI 测试脚本"
echo "======================================"
echo ""

# 测试 1: CLI 直接测试
echo "【测试 1】CLI 直接响应测试"
echo "问题：現在の顧客数は？"
echo "--------------------------------------"

RESPONSE=$(cd /home/ubuntu/ws/NetYamlForge && timeout 35 qwen -p "
あなたは自動車ディーラーの AI アシスタントです。

## ツール呼び出しルール
**ユーザーがデータ・件数・一覧を尋ねた場合は、必ず tool_call JSON だけを出力してください**

### 出力する JSON 形式
{\"tool_call\":\"query_data\",\"entity\":\"customers\",\"action\":\"count\",\"filters\":[],\"top\":1}

### 重要なポイント
- JSON**だけ**を出力（説明文・前後のテキストは一切不要）
- 件数質問には action:\"count\" を使用

ユーザーの質問：現在の顧客数は？
" 2>&1)

echo "CLI 响应:"
echo "$RESPONSE"
echo ""

# 检查响应是否包含 tool_call
if echo "$RESPONSE" | grep -q '"tool_call":"query_data"'; then
    echo "✅ 测试 1 通过：CLI 正确返回了 tool_call JSON"
else
    echo "❌ 测试 1 失败：CLI 没有返回 tool_call JSON"
fi

echo ""
echo "======================================"
echo "【测试 2】顾客数查询（带完整系统提示词）"
echo "--------------------------------------"

RESPONSE2=$(cd /home/ubuntu/ws/NetYamlForge && timeout 35 qwen -p "
あなたは自動車ディーラーの AI 業務アシスタントです。
リード管理・予約確認・在庫照会・顧客情報の照会など業務全般を支援します。
ユーザーがデータを尋ねた場合は **必ず query_data ツールを使用して** DB から最新情報を取得し、その結果に基づいて回答してください。

現在の日時：2026-03-31 17:00
営業時間：月〜土 9:00〜18:00

## query_data で利用可能なエンティティとフィールド
**vehicles** (車両在庫)
  フィールド：brand, model, grade, year, fuel_type, price, color, status
**service_appointments** (予約)
  フィールド：appointment_type, preferred_date, status
**sales_leads** (営業リード)
  フィールド：customer_id, status, vehicle_interest
**customers** (顧客)
  フィールド：name, phone, email, tier_level

## ツール呼び出しルール（最重要）
**ユーザーがデータ・件数・一覧を尋ねた場合は、必ず tool_call JSON だけを出力してください**

### 件数質問の例
- 「顧客数は？」「車両が何台ある？」「予約が何件？」→ action:\"count\" を使用

### 出力する JSON 形式
{\"tool_call\":\"query_data\",\"entity\":\"customers\",\"action\":\"count\",\"filters\":[],\"top\":1}

### 重要なポイント
- JSON**だけ**を出力（説明文・前後のテキスト・\`\`\`json マークは一切不要）
- 件数質問には action:\"count\" を使用（デフォルトは\"list\"）

ユーザーの質問：現在の顧客数は？
" 2>&1)

echo "CLI 响应:"
echo "$RESPONSE2"
echo ""

if echo "$RESPONSE2" | grep -q '"tool_call":"query_data"'; then
    echo "✅ 测试 2 通过：完整提示词下 CLI 正确返回 tool_call JSON"
else
    echo "❌ 测试 2 失败：完整提示词下 CLI 未能返回 tool_call JSON"
fi

echo ""
echo "======================================"
echo "【测试 3】其他查询测试"
echo "--------------------------------------"

echo "问题：利用可能な車両を一覧表示"
RESPONSE3=$(cd /home/ubuntu/ws/NetYamlForge && timeout 35 qwen -p "
あなたは自動車ディーラーの AI アシスタントです。
DB データが必要な場合は、以下の JSON**だけ**を出力してください:
{\"tool_call\":\"query_data\",\"entity\":\"vehicles\",\"filters\":[{\"field\":\"status\",\"op\":\"eq\",\"value\":\"available\"}],\"top\":20}

ユーザーの質問：利用可能な車両を一覧表示して
" 2>&1)

echo "CLI 响应:"
echo "$RESPONSE3"
echo ""

if echo "$RESPONSE3" | grep -q '"tool_call":"query_data"' && echo "$RESPONSE3" | grep -q '"entity":"vehicles"'; then
    echo "✅ 测试 3 通过：车辆查询也正确返回 tool_call JSON"
else
    echo "❌ 测试 3 失败：车辆查询未能正确返回"
fi

echo ""
echo "======================================"
echo "测试完成！"
echo "======================================"
echo ""
echo "总结:"
echo "- 如果所有测试都通过，说明 AI 能够正确理解并返回 tool_call JSON"
echo "- 如果部分测试失败，可能需要进一步优化系统提示词"
echo ""
echo "下一步:"
echo "1. 访问 http://localhost:5000/auto-dealer-demo 登录系统"
echo "2. 打开 AI 聊天界面"
echo "3. 输入'現在の顧客数は？'进行测试"
echo "4. 查看日志：tail -f NetYamlForge/logs/app-*.log | grep -i tool_call"
