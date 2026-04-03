#!/usr/bin/env python3
import re

file_path = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AutoDealerChatService.cs"

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 定义旧方法（精确匹配）
old_method = '''    /// <summary>
    /// システムプロンプトに CLI 向けの tool_call 出力形式を追記します。
    /// </summary>
    private static string AppendCliToolCallInstructions(string systemPrompt, bool isStaff)
    {
        var sb = new StringBuilder(systemPrompt);
        sb.AppendLine();
        sb.AppendLine("## ツール呼び出しルール");
        sb.AppendLine("DB データが必要な場合は、以下の JSON**だけ**を出力してください（説明文・前後のテキスト一切不要）:");
        sb.AppendLine("{\\"tool_call\\":\\"query_data\\",\\"entity\\":\\"テーブル名\\",\\"filters\\":[{\\"field\\":\\"フィールド名\\",\\"op\\":\\"eq|like|gt|lt|gte|lte\\",\\"value\\":\\"値\\"}],\\"orderBy\\":{\\"field\\":\\"フィールド名\\",\\"dir\\":\\"asc|desc\\"},\\"top\\":20}");
        sb.AppendLine("DB が不要な場合は通常の日本語で回答してください。");
        if (!isStaff)
            sb.AppendLine("予約を作成する場合：{\\"tool_call\\":\\"create_appointment_request\\",\\"customer_name\\":\\"名前\\",\\"appointment_type\\":\\"test_drive|service|consultation\\"}");
        return sb.ToString();
    }'''

# 定义新方法
new_method = '''    /// <summary>
    /// システムプロンプトに CLI 向けの tool_call 出力形式を追記します。
    /// </summary>
    private static string AppendCliToolCallInstructions(string systemPrompt, bool isStaff)
    {
        var sb = new StringBuilder(systemPrompt);
        sb.AppendLine();
        sb.AppendLine("## 重要：ツール呼び出しルール");
        sb.AppendLine("ユーザーがデータ（顧客・車両・予約・リードなど）について尋ねた場合は、**必ず** 以下の JSON 形式**だけ**を出力してください。");
        sb.AppendLine("説明文・前後のテキスト・markdown コードブロック（```）は一切不要です。JSON 文字列のみを出力してください。");
        sb.AppendLine();
        sb.AppendLine("### DB クエリの実行形式");
        sb.AppendLine("{\\"tool_call\\":\\"query_data\\",\\"entity\\":\\"テーブル名\\",\\"filters\\":[{\\"field\\":\\"フィールド名\\",\\"op\\":\\"eq\\",\\"value\\":\\"値\\"}],\\"orderBy\\":{\\"field\\":\\"created_at\\",\\"dir\\":\\"desc\\"},\\"top\\":20}");
        sb.AppendLine();
        sb.AppendLine("### 件数カウント形式（「何件」「数」などの質問）");
        sb.AppendLine("{\\"tool_call\\":\\"query_data\\",\\"entity\\":\\"テーブル名\\",\\"action\\":\\"count\\",\\"filters\\":[],\\"top\\":5}");
        sb.AppendLine();
        sb.AppendLine("### 利用可能なエンティティ");
        sb.AppendLine("- customers: 顧客情報（今日連絡すべき顧客など）");
        sb.AppendLine("- vehicles: 車両在庫");
        sb.AppendLine("- service_appointments: サービス予約");
        sb.AppendLine("- sales_leads: 営業リード（新規顧客問い合わせ）");
        sb.AppendLine();
        sb.AppendLine("### 例：今日連絡すべき顧客を尋ねられた場合");
        sb.AppendLine("{\\"tool_call\\":\\"query_data\\",\\"entity\\":\\"customers\\",\\"filters\\":[{\\"field\\":\\"last_contact_date\\",\\"op\\":\\"lt\\",\\"value\\":\\"today\\"}],\\"orderBy\\":{\\"field\\":\\"tier_level\\",\\"dir\\":\\"desc\\"},\\"top\\":10}");
        sb.AppendLine();
        if (!isStaff)
        {
            sb.AppendLine("### 予約作成形式");
            sb.AppendLine("{\\"tool_call\\":\\"create_appointment_request\\",\\"customer_name\\":\\"名前\\",\\"appointment_type\\":\\"test_drive|service|consultation\\"}");
            sb.AppendLine();
        }
        sb.AppendLine("DB が不要な場合（挨拶・雑談など）は通常の日本語で回答してください。");
        return sb.ToString();
    }'''

if old_method in content:
    content = content.replace(old_method, new_method)
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("✓ 文件修改成功 - AppendCliToolCallInstructions")
else:
    print("✗ 未找到 AppendCliToolCallInstructions 方法")
    print("搜索方法签名...")
    idx = content.find("AppendCliToolCallInstructions")
    if idx >= 0:
        print(f"找到位置：{idx}")
        print("附近 200 字符:")
        print(content[idx:idx+200])

# 现在修改 TryParseCliTool 方法
old_parse = '''    /// <summary>
    /// CLI の応答が tool_call JSON かどうかを判定します。
    /// </summary>
    private static bool TryParseCliTool(string response, out string toolName, out string rawJson)
    {
        toolName = "";
        rawJson = "";
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("{")) return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("tool_call", out var tc))
            {
                toolName = tc.GetString() ?? "";
                rawJson = trimmed;
                return !string.IsNullOrWhiteSpace(toolName);
            }
        }
        catch (JsonException) { }
        return false;
    }'''

new_parse = '''    /// <summary>
    /// CLI の応答が tool_call JSON かどうかを判定します。
    /// コードブロック形式（```json ... ```）にも対応します。
    /// </summary>
    private static bool TryParseCliTool(string response, out string toolName, out string rawJson)
    {
        toolName = "";
        rawJson = "";
        var trimmed = response.Trim();
        
        // コードブロック形式を除去
        if (trimmed.StartsWith("```json"))
            trimmed = trimmed["```json".Length..].Trim();
        else if (trimmed.StartsWith("```"))
            trimmed = trimmed["```".Length..].Trim();
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3].Trim();
        
        if (!trimmed.StartsWith("{")) return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("tool_call", out var tc))
            {
                toolName = tc.GetString() ?? "";
                rawJson = trimmed;
                return !string.IsNullOrWhiteSpace(toolName);
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON parse error: {ex.Message}");
        }
        return false;
    }'''

if old_parse in content:
    content = content.replace(old_parse, new_parse)
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("✓ 文件修改成功 - TryParseCliTool")
else:
    print("✗ 未找到 TryParseCliTool 方法")
