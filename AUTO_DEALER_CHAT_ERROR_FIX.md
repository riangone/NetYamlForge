# 汽车销售 AI 聊天错误修复计划

## 问题描述
用户在使用汽车销售子系统 AI 聊天时收到错误：`エラー: メッセージの処理に失敗しました。`

## 根因分析

根据代码调查，错误来源：
- **控制器**: `AutoDealerChatController.cs` 第 125/243 行（catch-all 异常处理）
- **调用链**: Controller → Service → BaseChatService → LLM Provider

**关键发现**: `GenerateAiResponseAsync()` 内部已捕获所有异常并返回错误模板，**不会向上抛出**。因此异常很可能来自：
1. 数据库操作（`SaveMessageAsync` / `GetRecentMessagesAsync`）
2. Slot-filling 流程
3. 数据库 schema 不匹配

## 修复状态

### ✅ 已完成 - 数据库 Schema 修复

**问题**: `ai_messages` 表缺少 `components_json` 列
**修复**: 执行了 `ALTER TABLE ai_messages ADD COLUMN components_json TEXT;`
**验证**: 表结构现在包含所有必需的列

### 🔄 待测试 - 应用功能验证

需要重启应用并测试 AI 聊天功能是否恢复正常。

## 修复步骤

### 步骤 1: 增强错误日志记录
**文件**: `AutoDealerChatController.cs`

在 catch 块中添加详细的异常信息记录：
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "メッセージ処理エラー conv={Id}, Type={Type}, Message={Msg}", 
        conversationId, ex.GetType().Name, ex.Message);
    
    // 如果是内部异常，记录详细信息
    if (ex.InnerException != null)
    {
        _logger.LogError(ex.InnerException, "内部例外: {Message}", ex.InnerException.Message);
    }
    
    return StatusCode(500, new { 
        error = "メッセージの処理に失敗しました。",
        errorType = ex.GetType().Name,
        // 仅在开发模式下返回详细错误
#if DEBUG
        details = ex.Message 
#endif
    });
}
```

### 步骤 2: 验证数据库 Schema
运行以下命令检查数据库表结构：
```bash
sqlite3 NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db ".schema ai_messages"
```

预期应该包含这些列：
- `message_id` (PK)
- `conversation_id` (FK)
- `sender`
- `message_type` (TEXT, 默认 'text')
- `content` (TEXT)
- `intent`
- `entities_json`
- `confidence_score`
- `sentiment_score`
- `metadata_json`
- `components_json`
- `timestamp` (DATETIME) ⚠️ **注意：不是 `created_at`**

### 步骤 3: 修复数据库 Schema（如需要）
如果数据库缺少列，执行以下 SQL：
```sql
-- 添加缺失的列
ALTER TABLE ai_messages ADD COLUMN IF NOT EXISTS message_type VARCHAR(20) NOT NULL DEFAULT 'text';
ALTER TABLE ai_messages ADD COLUMN IF NOT EXISTS components_json TEXT;
ALTER TABLE ai_messages ADD COLUMN IF NOT EXISTS confidence_score DECIMAL(10,4);
ALTER TABLE ai_messages ADD COLUMN IF NOT EXISTS sentiment_score DECIMAL(10,4);

-- 如果存在错误的列名，需要重命名
-- ALTER TABLE ai_messages RENAME COLUMN created_at TO timestamp;
```

### 步骤 4: 增强数据库操作异常处理
**文件**: `AutoDealerChatService.cs`

在 `SaveMessageAsync` 和 `GetRecentMessagesAsync` 方法中添加更详细的错误日志：
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "メッセージ保存失敗 conv={ConvId}, sender={Sender}, exType={Type}",
        conversationId, sender, ex.GetType().Name);
    throw; // 重新抛出，让上层处理
}
```

### 步骤 5: 添加数据库连接验证
在服务初始化时验证数据库连接和表结构：
```csharp
public async Task<bool> ValidateDatabaseAsync()
{
    try
    {
        var sql = @"
            SELECT COUNT(*) FROM pragma_table_info('ai_messages') 
            WHERE name IN ('message_type', 'components_json', 'timestamp')";
        
        var count = await _db.ExecuteScalarAsync<int>(sql);
        
        if (count < 3)
        {
            _logger.LogWarning("データベーススキーマが古いです。必要な列が不足しています。");
            return false;
        }
        
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "データベース検証エラー");
        return false;
    }
}
```

## 测试步骤

1. **启动应用并查看日志**:
   ```bash
   dotnet run --project NetYamlForge
   ```

2. **发送测试消息**:
   ```bash
   curl -X POST http://localhost:5000/api/auto-dealer-chat/send \
     -H "Content-Type: application/json" \
     -d '{"conversationId": "test-conv-1", "message": "車を教えてください"}'
   ```

3. **检查应用日志**:
   ```bash
   tail -f logs/*.log | grep -i "エラー\|error\|例外"
   ```

4. **查看详细的异常信息**（开发模式下）

## 预防措施

1. **添加数据库迁移机制**: 在应用启动时自动检测并更新 schema
2. **健康检查端点**: 添加 `/api/auto-dealer-chat/health` 验证数据库和 AI 服务状态
3. **更好的错误提示**: 根据异常类型返回具体的错误消息（数据库错误 vs AI 错误 vs 网络错误）

## 相关文件

- `NetYamlForge/Controllers/Api/AutoDealerChatController.cs`
- `NetYamlForge/Services/AI/AutoDealerChatService.cs`
- `NetYamlForge/Services/AI/BaseChatService.cs`
- `NetYamlForge/Services/ChatHistoryService.cs`
- `NetYamlForge/projects/auto-dealer-demo/database/init.sql`
