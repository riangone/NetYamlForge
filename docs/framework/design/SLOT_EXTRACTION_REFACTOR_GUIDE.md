# 槽位提取 AI 化重构指南

## 修改的文件

**文件**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`

## 需要修改的位置

### 1. 替换 ExtractSlotValuesFromMessageAsync 方法

**位置**: 第 503 行开始

**原代码**: ~150 行硬编码正则表达式和字典

**新代码** (约 60 行):

```csharp
/// <summary>
/// AI を使用してメッセージからスロット値を抽出
/// 正規表現や辞書の代わりに LLM で自然言語理解を行う
/// </summary>
private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
{
    if (_slotFilling == null) return;

    try
    {
        // シナリオに応じて抽出するスロットを定義
        var slotsToExtract = scenario switch
        {
            "test_drive" => "vehicle_model, preferred_date, preferred_time, customer_name, customer_phone",
            "estimate" => "vehicle_model, grade, budget, customer_name, customer_phone",
            "appointment_service" => "service_type, preferred_date, preferred_time, customer_name, customer_phone",
            "trade_in" => "vehicle_model, vehicle_year, mileage, customer_name, customer_phone",
            _ => "customer_name, customer_phone"
        };

        // AI に抽出を依頼するプロンプト
        var extractionPrompt = $@"あなたは情報抽出アシスタントです。以下のメッセージから、指定されたスロットの値を抽出してください。

メッセージ: {message}

抽出するスロット: {slotsToExtract}

以下の JSON 形式のみで返してください。値がないスロットは null にしてください。
{{
  ""vehicle_model"": ""車種名"",
  ""preferred_date"": ""日付"",
  ""preferred_time"": ""時間"",
  ""customer_name"": ""名前"",
  ""customer_phone"": ""電話番号"",
  ""service_type"": ""サービス種類"",
  ""grade"": ""グレード"",
  ""budget"": ""予算"",
  ""vehicle_year"": ""車両年"",
  ""mileage"": ""走行距離""
}}

ルール:
- 日本語の日付表現（明日、来週、等）はそのまま抽出
- 時間表現（午前10時、午後2時、等）もそのまま抽出  
- 名前は敬語表現（です、と申します、等）を除いた部分のみを抽出
- 電話番号は数字とハイフンをそのまま抽出
- 見つからない値は null にしてください
- JSON のみ出力し、他の説明は不要です";

        var response = await CompleteAsync(extractionPrompt, temperature: 0.1f);
        
        // JSON をパースしてスロットを更新
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');
        
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var extracted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr);
            
            if (extracted != null)
            {
                var updated = false;
                foreach (var kvp in extracted)
                {
                    if (kvp.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = kvp.Value.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            await _slotFilling.UpdateSlotAsync(conversationId, kvp.Key, value, _projectName);
                            updated = true;
                            _logger.LogInformation("AIスロット抽出成功: Scenario={Scenario}, Slot={Slot}, Value={Value}",
                                scenario, kvp.Key, value);
                        }
                    }
                }
                
                if (updated)
                {
                    _logger.LogInformation("スロット更新完了: Scenario={Scenario}", scenario);
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "AIスロット抽出に失敗しました");
        // AI に失敗した場合は何もしない（既存のセッションを保持）
    }
}
```

### 2. 删除 LooksLikeNonNameText 方法

**位置**: 第 680-720 行左右

**操作**: 完全删除此方法（不再需要）

整个方法（约 40 行）可以删除：
```csharp
private static bool LooksLikeNonNameText(string text)
{
    // ... 整个方法体 ...
}
```

## 关键变化

### 之前（~200 行）
- ❌ 硬编码日期字典
- ❌ 硬编码时间字典  
- ❌ 硬编码车种字典
- ❌ 硬编码服务类型字典
- ❌ 3 个名字正则模式
- ❌ 名字过滤辅助方法
- ❌ 多个正则表达式

### 之后（~60 行）
- ✅ 简洁的 AI 提示词
- ✅ 自动理解各种表达
- ✅ 多语言支持
- ✅ 易于维护

## 代码减少统计

| 项目 | 之前 | 之后 | 减少 |
|------|------|------|------|
| ExtractSlotValuesFromMessageAsync | ~150 行 | ~60 行 | 60% ↓ |
| LooksLikeNonNameText | ~40 行 | 0 行 | 100% ↓ |
| **总计** | **~190 行** | **~60 行** | **68% ↓** |

## 验证步骤

修改后运行：

```bash
# 1. 构建
dotnet build NetYamlForge/NetYamlForge.csproj

# 2. 测试
dotnet test --filter "FullyQualifiedName~HybridIntentClassifierTests"

# 3. 运行应用测试
dotnet run --project NetYamlForge
```

## 测试用例

验证以下消息能正确提取信息：

| 消息 | 应提取 |
|------|--------|
| "田中です" | customer_name = "田中" |
| "山田と申します" | customer_name = "山田" |
| "明日の午前10時" | preferred_date = "明日", preferred_time = "午前10時" |
| "プリウスの試乗" | vehicle_model = "プリウス" |
| "090-1234-5678" | customer_phone = "090-1234-5678" |

---

*创建日期: 2026-04-08*
*状态: 待实施*
