# 日记子项目（diary-companion）设计稳定期与核心 UI 组件规范

本项目（`diary-companion`）已完成界面和底层交互逻辑的全面升级与重构，并进入设计稳定期。为了实现框架的高复用性与组件化，所有精致的定制化 UI 均已迁移至 **NetYamlForge 框架核心 UI 库**，日记子项目本身现完全采用声明式的 YAML 配置驱动。

---

## 📂 核心文件清单

### 1. 框架核心复用组件（通用 UI 库）
* [**`_SectionMoodBoard.cshtml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionMoodBoard.cshtml) (`mood_board`)
  * **功能**：分类计数看板。自动解析聚合数据，计算各分类百分比。
  * **亮点**：内置常用心情与天气的 Emoji 自动映射；支持根据配置中的 `description` 渲染精致的渐变色提示卡片（如“心灵小贴士”）。
* [**`_SectionFancyDiaryList.cshtml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionFancyDiaryList.cshtml) (`fancy_diary_list`)
  * **功能**：精致日记卡片流展示。
  * **亮点**：支持单条记录的详细模态弹窗（Modal）及 AJAX 异步删除微交互（包含淡出与缩放过渡动画）；自适应映射 `Title`、`Content`、`Weather`、`Mood`、`Sentiment` 及 `AiResponse` 等字段。
* [**`_SectionInteractiveForm.cshtml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionInteractiveForm.cshtml) (`interactive_form`)
  * **功能**：沉浸式表单交互组件。
  * **亮点**：
    * **自动定位**：在非编辑状态下，进入页面 300ms 后自动调用 HTML5 Geolocation API。
    * **自动气象同步**：定位成功后根据经纬度实时请求 **Open-Meteo API**，解析 WMO 气象代码，智能模拟点击并勾选匹配的天气 Emoji 按钮（如“晴朗”、“多云”等）。
    * **交互式输入**：为特定字段提供网格化 Emoji 选项，表单提交时触发全屏 Loading 遮罩动画，并精准将后端 API 校验失败消息呈现在对应字段下方。

### 2. 页面与路由配置
* [**`HomePage.yaml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/diary-companion/pages/HomePage.yaml)
  * **定义**：日记一览页的数据源及组件绑定。包含 `mood_board` 与 `fancy_diary_list`。
* [**`DiaryForm.yaml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/diary-companion/pages/DiaryForm.yaml)
  * **定义**：日记发布/修改页的表单项白名单配置（仅暴露用户必需的 `Title`、`Location`、`Weather`、`MoodBefore` 和 `Content` 字段，隐藏 AI 计算的属性）。
* [**`Settings.yaml`** / **`Settings.cshtml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/diary-companion/views/Settings.cshtml)
  * **定义**：独立个性化偏好设置页面（保存于 `localStorage`）。支持“隐藏 Header 导航栏”、“沉浸式全屏展示”、“自定义悬浮菜单位置（右下角/左下角/右上角/左上角）”等选项。

### 3. 底层布局与控制器扩展
* [**`_Layout.cshtml`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/diary-companion/views/_Layout.cshtml)
  * **防闪烁（FOUC）控制**：在 `<head>` 中注入早期脚本直接从 `localStorage` 读取偏好，使得 DOM 渲染前即绑定好对应的隐藏类。
  * **悬浮菜单与极客快捷键**：将页面底部悬浮动作条整合在此；并绑定了全局键盘监听（当未聚焦输入框时）：
    * <kbd>M</kbd>：开关侧边栏导航
    * <kbd>S</kbd>：跳转至设置页面
    * <kbd>H</kbd>：返回日记首页
* [**`DashboardController.cs`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Controllers/DashboardController.cs)
  * **默认路由重定向**：访问子项目根路径 `/diary-companion` 或 `/diary-companion/` 时，将自动检查并重定向至 `HomePage`，而不展示通用的仪表盘（同时保留显式访问 `/diary-companion/Dashboard` 仪表盘的能力）。
* [**`PageController.cs`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Controllers/PageController.cs)
  * **核心布局绑定**：在渲染 YAML 配置页面或自定义视图时，自动查找并优先加载项目专属布局文件，从而使个性化设置可在全站生效。

### 4. 数据库文件
* [**`diary-companion.db`**](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/diary-companion/database/diary-companion.db)
  * **类型**：SQLite 数据库。
  * **表结构**：包含 `DiaryEntry`（日记核心记录）与 `MoodInsight`（AI 情绪分析与建议反馈）两张核心表。

---

## 🛠 稳定期技术验证

1. **零闪烁全沉浸式体验**：
   个性化全屏在进入页面的一瞬间完成渲染，未出现任何 Layout 样式重排引起的视觉抖动。
2. **免 Key 气象对接健壮性**：
   在 HTTPS 或本地开发环境下，Geolocation 服务能顺畅运转，即使在网络断开或定位权限被禁用的情况下，表单也会优雅降级为手动选择模式。
3. **完全声明式开发模式**：
   后续子项目若需复用此风格，只需在对应页面的 YAML 配置文件中将组件类型指定为 `mood_board`、`fancy_diary_list` 或 `interactive_form`，并提供对应的数据源字段即可，无需再编写任何 Razor 视图代码。
