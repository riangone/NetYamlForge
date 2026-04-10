# 测试 AI 聊天 Rich UI 组件功能

## 测试步骤

### 1. 数据库迁移

在测试之前，需要在数据库中执行以下 SQL 来添加 `components_json` 列：

```bash
# 进入项目目录
cd /home/ubuntu/ws/NetYamlForge

# 运行应用后，执行以下 SQL（如果是 SQLite）
sqlite3 projects/auto-dealer-demo/database/auto-dealer.db < projects/auto-dealer-demo/database/add_components_column.sql
```

或者直接运行应用，第一次启动时会自动创建新的数据库结构。

### 2. 启动应用

```bash
dotnet run --project NetYamlForge
```

### 3. 测试 Rich UI 组件

1. **打开浏览器并登录系统**
   - 访问 `http://localhost:5000`
   - 使用管理员账户登录

2. **打开 AI 客服聊天窗口**
   - 点击右下角的 🚗 AI 窓口 按钮
   - 确保你在 `auto-dealer-demo` 项目中

3. **测试各种 Rich UI 组件**

   发送以下消息来触发不同的组件：

   a. **车辆搜索** (`vehicle_search`)
      - 发送: "在庫にある車を探したい"
      - 预期: 显示车辆卡片轮播组件

   b. **预约功能** (`appointment_booking`)
      - 发送: "試乗予約をしたい"
      - 预期: 显示日期时间选择器组件

   c. **价格筛选** (`price_filter`)
      - 发送: "予算で車を探したい"
      - 预期: 显示价格范围滑块组件

   d. **品牌选择** (`brand_selection`)
      - 发送: "メーカーで車を選びたい"
      - 预期: 显示多选组件（丰田、本田、日产等）

   e. **帮助** (`help`)
      - 发送: "ヘルプ"
      - 预期: 显示文本建议组件

4. **验证组件渲染**
   - 组件应该在聊天消息下方正确显示
   - 点击组件按钮应该能触发相应的操作
   - 组件在用户交互后应该变为非活动状态（dismissed）

5. **验证历史恢复**
   - 刷新页面
   - 组件应该从数据库中正确恢复并显示

### 4. 检查浏览器控制台

打开浏览器开发者工具（F12），检查控制台是否有错误：
- 不应该有 JavaScript 错误
- 应该看到组件被正确解析和渲染的日志

### 5. 检查数据库

```sql
-- 查看保存的 components_json 数据
SELECT message_id, sender, content, components_json 
FROM ai_messages 
WHERE components_json IS NOT NULL 
ORDER BY timestamp DESC 
LIMIT 5;
```

## 预期结果

- ✅ AI 选择器正确显示并工作
- ✅ Rich UI 组件在聊天消息中正确渲染
- ✅ 用户可以选择/确认组件选项
- ✅ 组件数据正确保存到数据库
- ✅ 页面刷新后组件正确恢复

## 故障排除

如果组件没有显示：

1. **检查前端日志**
   ```javascript
   console.log('extra:', extra);
   console.log('components:', extra?.components);
   ```

2. **检查后端响应**
   在浏览器 Network 标签中检查 API 响应是否包含 `components` 字段

3. **检查数据库**
   确认 `components_json` 列已添加

4. **检查 CSS**
   确认 `ai-chat-components.css` 已正确加载
