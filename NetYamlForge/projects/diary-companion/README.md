# 📖 智能日记伴侣 (Smart Diary Companion) — 项目技术白皮书

智能日记伴侣（`diary-companion`）是基于 **NetYamlForge** 低代码应用框架开发的一款多语言、AI 驱动的沉浸式个人日记记录系统。该应用深度融合了大语言模型（LLM）的图文标注与情感评估技术，并提供精致灵动的磨砂玻璃质感 UI 交互，旨在为用户提供有温度的“智能心灵对话”体验。

---

## 📂 项目目录与代码架构

项目位于框架的 `projects/diary-companion` 目录下，由以下核心模块组成：

```text
diary-companion/
├── project.yaml                  # 项目定义与元数据配置
├── README.md                     # 本技术白皮书说明文档
├── config/                       # 配置中心
│   ├── layout.yml                # 项目级布局与侧边栏导航配置
│   ├── home-page.yml             # 主页控制面板信息配置
│   └── i18n.yml                  # 核心翻译文件（中、英、日、韩四国多语言）
├── database/                     # 数据库初始化/迁移脚本
├── entities/                     # 数据库与实体定义
│   ├── diary_entry.yml           # 日记实体 (DiaryEntry)，包含表结构定义与 Hook 绑定
│   └── mood_insight.yml          # 情绪分析统计实体 (MoodInsight)
├── pages/                        # 低代码页面元数据
│   ├── HomePage.yaml             # 首页布局配置
│   ├── DiaryForm.yaml            # 写日记表单配置（引用自定义 interactive_form 组件）
│   ├── Settings.yaml             # 设置页面配置
│   └── StarterOverview.yaml      # 起始过渡页配置
├── views/                        # 自定义 Razor 视图组件 (基于 DaisyUI)
│   ├── _Layout.cshtml            # 自定义主题骨架（支持顶栏多语言快速切换 🇨🇳🇺🇸🇯🇵🇰🇷）
│   ├── HomePage.cshtml           # 日记图表统计看板与瀑布流卡片列表页
│   ├── Settings.cshtml           # 偏好设置页面（全屏、沉浸式、默认 AI/UI 语言配置）
│   └── _ViewImports.cshtml       # 视图全局命名空间导入
└── Hooks/                        # 后端业务切面逻辑 (C# Hook Services)
    └── DiaryCompanionHooks.cs    # 包含图片缩略图、自动标注、多语言 AI 评估等核心逻辑
```

---

## 🌐 完善的多语言支持体系 (Multi-Language System)

应用提供完善的 **简体中文 (zh-CN)**、**英语 (en-US)**、**日语 (ja-JP)** 和 **韩语 (ko-KR)** 四国语言支持，分为界面本地化和 AI 情绪评估两个维度：

### 1. UI 界面显示语言切换
* **数据持久化**：用户既可以通过顶部导航栏中的国旗图标快速切换，也可以进入“系统偏好设置”页面修改。
* **实现逻辑**：
  * 设置提交时，表单通过 ASP.NET Core 的标准的标签助手（Tag Helper）动态且安全地 POST 到后端路由 `/Localization/SetLanguage`。
  * 后端写入加密的 `.AspNetCore.Culture` Cookie，并在用户已登录状态下，自动将偏好语言保存进数据库用户表中的 `PreferredLanguage`。
  * **简称兼容匹配**：前端加载时，针对如 `zh`、`en`、`ja`、`ko` 等常见简称执行了自动匹配补全（对应到 `zh-CN`、`en-US`、`ja-JP`、`ko-KR`），防止下拉框因格式不匹配而回退至默认值，彻底消除了“偏好设置中语言未生效”的假象。
* **本地化加载**：底层依托框架内置的 `YamlKeyLocalizer` 类读取 `config/i18n.yml`（局部）和系统全局的 `i18n.yml`，根据当前的 `CultureInfo.CurrentUICulture` 动态进行文本键值翻译。

### 2. 🤖 AI 评估语言绑定 (AI Evaluation Language)
* **默认配置**：在“偏好设置”中，用户可指定默认的 AI 评估语言，配置数据存储于浏览器的 `localStorage` 中 (`diary_preferences.aiLanguage`)。
* **自动填充**：在新建日记表单页面中，`_SectionInteractiveForm.cshtml` 脚本会自动检测并提取 `localStorage` 中的默认 AI 语言，对日记实体的 `AiLanguage` 下拉选框执行自动选中。
* **针对性评估**：后端 Hook 触发时，会获取当前日记记录所指定的 `AiLanguage`，将相应的定制 Prompt 投递给大语言模型（LLM）接口：
  * **zh-CN**: AI 伴侣用中文抚慰心灵，情绪输出为“积极”、“平和”、“消极”。
  * **en-US**: 强制使用纯英文进行回复，情绪映射为 "Positive", "Neutral", "Negative"。
  * **ja-JP**: 回答和情绪分类强制为日语（"ポジティブ", "ニュートラル", "ネガティブ"）。
  * **ko-KR**: 回答和情绪分类强制为韩语（"긍정", "중립", "부정"）。
* **多语言防错翻译网**：当底层大语言模型受限于系统全局中文指令污染而产生语言偏离时，Hook 内部设计了后置翻译管道。若检测到 AI 评估语言不是 `zh-CN` 且模型回复了非目标语言，会启动 LLM 翻译通道自动把 AI 寄语翻译为对应的目标语言（英语、日语或韩语），并去除多余引号与 Markdown 标记，保障最终呈现结果准确无误。

---

## ⚡ 核心业务与交互逻辑解析

### 1. 保存前数据拦截与增强钩子 (`DiaryCompanionHooks.cs`)
当用户保存日记时，`analyze_diary_mood` 钩子会在后台执行三项主要任务：
* **图片自动缩量与 WebP 压缩**：
  如果用户上传了 Base64 格式的大体积原图，后端会调用 `SixLabors.ImageSharp` 组件对图片进行等比缩小（最大边缘限制为 640px，以适配 Retina 屏），同时采用 `WebP` 编码（75 质量因子）压缩后保存至 `ImageThumbnailBase64`。这不仅保证了图片的高清晰度，更极大地减少了列表加载的流量开销。
* **AI 自动图片标注生成**：
  如果用户只上传了图片却未编写任何“图片标注”文字，AI 伴侣会在保存阶段提取图片并调用大模型进行多模态感知分析，自动生成 2-6 个字的精简标注（如“温暖的下午茶”、“雨中的街道”、“美味的晚餐”），并更新日记实体的 `ImageLabel` 字段，同时会自动将标注信息以 `[标注]` 的格式追加到日记标题以及内容的最下方。在编辑更新操作时，系统会自动清理旧的标注后缀，防止标注信息被重复堆叠追加。
* **情绪分析超时防护保护**：
  由于 LLM 云端接口可能存在不可抗力的网络延迟，为了避免阻塞用户的写日记请求，Hook 设计了 **8秒超时限制 (`Task.WhenAny`)**。一旦接口卡顿，在 8 秒内未返回，程序将自动将情绪标记为“平和”，并附带一条本地化的温暖系统寄语完成落库，保障请求立刻放行。

### 2. 沉浸式前端表单 (`_SectionInteractiveForm.cshtml`)
* **微观质感交互**：摒弃了框架通用的基础表单渲染，使用精美的 DaisyUI 主题卡片、沉浸式的 Emoji 心情/天气单选按钮网格。
* **天气与地理定位辅助**：
  * 点击“自动获取”地点时，会通过 HTML5 Geolocation 获取精确经纬度。
  * 之后，前端会并发请求 OpenStreetMap 的 Geocoding 接口实现汉化逆编码获得当前的市、区、街道。
  * 同时结合 Open-Meteo 天气预报 API 获得当地当前时刻的天气代码（WMO Code），转换为对应中文天气标签，并在表单的天气单选框中实现自动选中，极大地降低了用户打字记录的负担。
* **快捷按键支持 (极客无障碍)**：
  在非输入框聚焦时，用户可以通过在键盘上直接按下单个字母键以提升操作便利：
  * <kbd>M</kbd>：折叠/展开系统侧边导航栏。
  * <kbd>S</kbd>：一键跳转到日记的“系统偏好设置”页面。
  * <kbd>H</kbd>：一键快速返回日记首页列表。

---

## 🛠 开发调试与部署优化建议

### 1. 编译速度与超时优化
在对项目进行编译调试（如在控制台运行 `dotnet build`）时，为防止因长耗时执行而导致 Antigravity 插件单次超时拦截，建议将慢命令以**异步后台任务**的方式运行，即：
* 执行 `run_command` 时，将 `WaitMsBeforeAsync` 属性设置为 `500` 或 `1000` 毫秒，使其自动切换到后台任务，并在执行完成后让平台通过 `reactive wakeup` 异步唤醒，从而避免造成长时间同步等待带来的超时警告。

### 2. 本地化缓存刷新
当修改了 `projects/diary-companion/config/i18n.yml` 的词条时，需要注意 `YamlKeyLocalizer` 在运行时存在内存级缓存设计（保证访问性能）。可以尝试重启 Web 服务或调用热插拔重载以重新解析 YAML 词典。
