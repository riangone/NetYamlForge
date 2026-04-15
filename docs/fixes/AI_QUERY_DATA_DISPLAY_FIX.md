# AI 查询聊天窗口数据显示修复报告

## 问题描述

在 AI 自然语言查询聊天窗口中，当用户查询数据时（例如："显示车辆库存"），虽然后端成功查询到了数据，但页面上只显示了 Markdown 格式化的文本摘要，**没有显示实际的数据表格**。

用户看到的消息类似：
```
非常抱歉之前的困扰。让我直接为您查询车辆库存数据：

該当件数：X 件
```

但具体的车辆数据（品牌、型号、价格等）没有显示出来。

## 根本原因

问题出在 `/wwwroot/js/ai-query-chat.js` 文件的 `addAssistantMessage` 函数中。

### 数据流分析

1. **后端** (`NaturalLanguageQueryHub.cs`):
   ```csharp
   await Clients.Caller.SendAsync("query_complete", new
   {
       data = data,           // ✓ 包含实际查询结果数据
       markdown = markdown,   // ✓ Markdown 格式化文本
       total = total,
       executionTimeMs = stopwatch.ElapsedMilliseconds
   });
   ```

2. **前端 SignalR 事件处理** (`ai-query-chat.js`):
   ```javascript
   connection.on('query_complete', (data) => {
       addAssistantMessage(data.markdown, data);  // ✓ 传递了完整数据
   });
   ```

3. **消息渲染** (`ai-query-chat.js` - `addAssistantMessage` 函数):
   ```javascript
   // ✗ 问题：只显示了 markdown 文本，没有显示 data.data 中的实际数据
   contentEl.innerHTML = `<div class="ai-query-message-text ai-query-markdown">${htmlContent}</div>`;
   ```

## 修复方案

### 修改文件

- `/home/ubuntu/ws/NetYamlForge/NetYamlForge/wwwroot/js/ai-query-chat.js`

### 修改内容

#### 1. 在 `addAssistantMessage` 函数中添加数据表格显示逻辑

```javascript
// 如果有数据，显示数据表格
if (data && data.data && Array.isArray(data.data) && data.data.length > 0) {
    const tableContainer = document.createElement('div');
    tableContainer.className = 'ai-query-data-table-container';
    tableContainer.style.cssText = 'margin-top: 1rem; overflow-x: auto;';

    const table = document.createElement('table');
    table.className = 'ai-query-data-table';
    table.style.cssText = 'width: 100%; border-collapse: collapse; font-size: 0.85rem;';

    // 创建表头
    const headers = Object.keys(data.data[0]);
    const thead = document.createElement('thead');
    const headerRow = document.createElement('tr');
    headers.forEach(header => {
        const th = document.createElement('th');
        th.textContent = header;
        th.style.cssText = 'background: #f3f4f6; padding: 0.5rem; text-align: left; font-weight: 600; border-bottom: 2px solid #e5e7eb;';
        headerRow.appendChild(th);
    });
    thead.appendChild(headerRow);
    table.appendChild(thead);

    // 创建表体
    const tbody = document.createElement('tbody');
    data.data.forEach(row => {
        const tr = document.createElement('tr');
        headers.forEach(header => {
            const td = document.createElement('td');
            td.style.cssText = 'padding: 0.5rem; border-bottom: 1px solid #e5e7eb;';
            const value = row[header];
            td.textContent = formatCellValue(value);
            tr.appendChild(td);
        });
        tbody.appendChild(tr);
    });
    table.appendChild(tbody);

    tableContainer.appendChild(table);
    contentEl.appendChild(tableContainer);
}
```

#### 2. 添加 `formatCellValue` 辅助函数

```javascript
// 格式化单元格值
function formatCellValue(value) {
    if (value === null || value === undefined) {
        return '-';
    }
    if (value instanceof Date) {
        return value.toLocaleDateString('zh-CN');
    }
    // 检查是否是日期时间字符串
    if (typeof value === 'string') {
        const dateMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})/);
        if (dateMatch) {
            return value.substring(0, 10).replace(/-/g, '/');
        }
    }
    // 数字格式化
    if (typeof value === 'number') {
        return value.toLocaleString('zh-CN');
    }
    // 布尔值
    if (typeof value === 'boolean') {
        return value ? '✓' : '✗';
    }
    // 字符串截断
    const str = String(value);
    if (str.length > 100) {
        return str.substring(0, 97) + '...';
    }
    return str;
}
```

## 修复效果

修复后，AI 查询结果将包含：

1. **Markdown 摘要**（保持原有功能）
   - 符合条件的记录数量
   - 每条记录的简洁摘要（包含主要信息、状态、日期等）

2. **数据表格**（新增功能）✨
   - 完整的字段数据表格
   - 格式化的日期、数字、布尔值
   - 可水平滚动（适应小屏幕）

3. **执行元信息**（保持原有功能）
   - 查询耗时
   - 结果总数

### 示例输出

用户查询："显示所有车辆库存"

修复前：
```
該当件数：15 件

- **2024 Toyota Camry** (販売中) — ¥3,500,000 — 12,500km
- **2023 Honda Civic** (商談中) — ¥2,800,000 — 8,200km
...
```

修复后：
```
該当件数：15 件

- **2024 Toyota Camry** (販売中) — ¥3,500,000 — 12,500km
- **2023 Honda Civic** (商談中) — ¥2,800,000 — 8,200km
...

┌─────────────────────────────────────────────────────────┐
│ vehicle_id │ brand  │ model  │ year │ price    │ status │
├─────────────────────────────────────────────────────────┤
│ 1          │ Toyota │ Camry  │ 2024 │ 3,500,000│ 販売中 │
│ 2          │ Honda  │ Civic  │ 2023 │ 2,800,000│ 商談中 │
│ ...                                                    │
└─────────────────────────────────────────────────────────┘

耗时：245ms | 结果：15条
```

## 测试建议

1. **基本查询测试**
   ```
   查询：显示所有产品
   预期：显示产品列表数据表格
   ```

2. **条件查询测试**
   ```
   查询：显示库存低于 10 的商品
   预期：只显示符合条件的数据表格
   ```

3. **空结果测试**
   ```
   查询：显示价格为负数的商品
   预期：显示"0 件"提示，不显示数据表格
   ```

4. **大数据量测试**
   ```
   查询：显示所有订单
   预期：数据表格可水平滚动，长文本被截断
   ```

5. **特殊字符测试**
   ```
   查询：显示包含特殊字符的数据
   预期：HTML 转义正确，无 XSS 风险
   ```

## 构建验证

```bash
# 构建主项目
dotnet build NetYamlForge/NetYamlForge.csproj

# 运行开发服务器
dotnet run --project NetYamlForge
```

## 注意事项

1. **性能考虑**：如果查询结果超过 100 条，建议在后端限制返回数量，避免前端渲染性能问题
2. **样式优化**：数据表格使用内联样式，后续可以考虑移到 CSS 文件中统一管理
3. **响应式设计**：表格容器设置了 `overflow-x: auto`，在小屏幕上可以水平滚动

## 相关文件

- `/wwwroot/js/ai-query-chat.js` - 主要修改文件
- `/Hubs/NaturalLanguageQueryHub.cs` - SignalR Hub（无需修改）
- `/Services/AI/QueryResultFormatter.cs` - Markdown 格式化（无需修改）
- `/Views/Dashboard/AIQuery.cshtml` - 页面视图（无需修改）
- `/Views/Shared/Components/AIQueryChat/AIQueryChat.cshtml` - 聊天组件视图（无需修改）

## 修复日期

2026-04-09
