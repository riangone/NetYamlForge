# PDFSharp 字体设置指南

## 概要

NetYamlForge 使用 PDFSharp 6.x 生成 PDF 文件。为了支持日语显示并避免使用 TTC (TrueType Collection) 文件，我们采用 Google Fonts 的 Noto Sans JP 字体（TTF 格式）。

## 方案 1：手动下载 Google Fonts（推荐）

### 下载步骤

**1. 下载字体 CSS（带 User-Agent 头获取 TTF 格式）**

```bash
cd NetYamlForge/NetYamlForge/wwwroot/fonts

# Regular (400)
curl -sL -H "User-Agent: Mozilla/5.0" \
  "https://fonts.googleapis.com/css2?family=Noto+Sans+JP&display=swap" -o noto-sans-jp.css

# Bold (700)
curl -sL -H "User-Agent: Mozilla/5.0" \
  "https://fonts.googleapis.com/css2?family=Noto+Sans+JP:wght@700&display=swap"
```

**2. 从 CSS 中提取 TTF URL 并下载**

```bash
# Regular
curl -sL -o NotoSansJP-Regular.ttf \
  "https://fonts.gstatic.com/s/notosansjp/v56/-F6jfjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf"

# Bold
curl -sL -o NotoSansJP-Bold.ttf \
  "https://fonts.gstatic.com/s/notosansjp/v56/-F6jfjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf"
```

**3. 验证字体文件**

```bash
file *.ttf
# 应该显示：TrueType Font data
```

### 一键下载脚本

```bash
#!/bin/bash
FONT_DIR="NetYamlForge/NetYamlForge/wwwroot/fonts"
mkdir -p "$FONT_DIR"
cd "$FONT_DIR"

# Regular
curl -sL -o NotoSansJP-Regular.ttf \
  "https://fonts.gstatic.com/s/notosansjp/v56/-F6jfjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf"

# Bold
curl -sL -o NotoSansJP-Bold.ttf \
  "https://fonts.gstatic.com/s/notosansjp/v56/-F62fjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf"

echo "字体下载完成！"
ls -la *.ttf
```

## 方案 2：使用自动下载（需要网络）

应用程序会在首次运行时自动从 Google Fonts 下载字体文件。

字体文件会缓存在 `wwwroot/fonts/` 目录。

## 方案 3：使用系统字体

### Linux (Ubuntu/Debian)

安装 Noto CJK 字体：

```bash
sudo apt-get install -y fonts-noto-cjk
```

**注意**: 系统提供的可能是 TTC 格式，PDFSharp 处理 TTC 文件有限制，建议优先使用 TTF 格式。

### Windows

应用程序会自动使用系统字体：
- `C:\Windows\Fonts\ipaexg.ttf`
- `C:\Windows\Fonts\YuGothR.ttf`

### macOS

应用程序会自动使用系统字体：
- `/Library/Fonts/Arial Unicode.ttf`

## 验证

运行以下命令验证字体是否正常工作：

```bash
cd /path/to/NetYamlForge
dotnet test --filter "FullyQualifiedName~DocumentPdfSharpService"
```

预期输出：
```
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13
```

## 完整测试

运行完整测试套件：

```bash
dotnet test
```

预期输出：
```
Passed!  - Failed:     0, Passed:   306, Skipped:     0, Total:   306
```

## 故障排除

### 问题：测试失败 "No appropriate font found"

**原因**: FontResolver 未正确注册或字体文件损坏

**解决方案**:
1. 检查字体文件是否存在且有效
2. 删除缓存的字体文件并重新下载
3. 检查应用程序日志

### 问题：PDF 文字显示为方框

**原因**: 字体不支持日语字符

**解决方案**:
1. 确保使用 Noto Sans JP 或类似的 CJK 字体
2. 避免使用仅支持 Latin 的字体（如 Arial）

## 技术细节

### 为什么不使用 TTC 文件？

TTC (TrueType Collection) 文件包含多个字体，但 PDFSharp 6.x 在处理 TTC 文件时存在一些问题：
- 需要额外的提取步骤
- 某些 TTC 文件可能导致解析错误

因此，我们优先使用单独的 TTF 文件。

### FontResolver 实现

`DocumentPdfSharpService` 使用自定义的 `UniversalFontResolver` 来处理所有字体请求：
- 预加载字体数据到内存
- 拦截所有字体请求并返回预加载的数据
- 确保跨平台一致性

## 参考链接

- [PDFSharp Font Resolving](https://docs.pdfsharp.net/link/font-resolving.html)
- [Google Fonts - Noto Sans JP](https://fonts.google.com/noto/specimen/Noto+Sans+JP)
- [Noto Fonts GitHub](https://github.com/google/fonts)
