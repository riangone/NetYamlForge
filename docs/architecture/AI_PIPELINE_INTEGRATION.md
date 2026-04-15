# AI Pipeline 整合指南

本文档说明如何将 Multi-AI Harness 整合到 NetYamlForge 项目中。

## 概述

通过方案 A（Harness 作为 NetYamlForge 的 AI 后端），我们实现了以下功能：

### 核心能力

1. **多角色 AI 协作** - Planner → Generator → Evaluator 完整 Pipeline
2. **成本优化** - 智能选择最经济的 AI（Qwen 用于生成，Claude 用于规划/评估）
3. **自动化流程** - 从提示词到代码生成的全自动流程
4. **质量保障** - AI 自动评估和反馈循环

## 架构

```
NetYamlForge (.NET)
    ↓ 调用
AiPipelineService
    ↓ 执行
harness_wrapper.py (Python)
    ↓ 调用
pipeline_executor.py
    ↓ 调度
多个 AI CLI (Qwen, Claude, Gemini, etc.)
```

## 快速开始

### 1. 安装依赖

确保以下依赖已安装：

```bash
# Python 3.8+
python3 --version

# Harness 项目
ls /home/ubuntu/ws/harness-new/webui/app/pipeline_executor.py

# AI CLI 工具
qwen --version
claude --version  # 可选
gemini --version  # 可选
```

### 2. 配置

在 `appsettings.json` 中添加配置：

```json
{
  "AiPipeline": {
    "HarnessDirectory": "/home/ubuntu/ws/harness-new",
    "HarnessHttpEndpoint": null,
    "HarnessWorkDirectory": "/tmp/nyf-harness",
    "PythonExecutable": "python3",
    "DefaultTimeoutSeconds": 3600
  }
}
```

### 3. 使用 CLI 命令

```bash
# 基本用法
dotnet run -- --ai-generate --prompt="为 Task 实体创建 CRUD 页面"

# 指定项目
dotnet run -- --ai-generate --prompt="生成用户管理功能" --project=myapp

# 指定目标目录
dotnet run -- --ai-generate --prompt="创建订单系统" --target-dir=/path/to/project

# 自定义超时
dotnet run -- --ai-generate --prompt="复杂功能" --timeout=7200
```

### 4. 使用 WebUI

1. 启动 NetYamlForge 应用
2. 访问 `/ai-pipeline` 页面
3. 输入任务描述并提交
4. 查看执行结果和生成的文件
5. 将生成的文件应用到项目中

## API 使用

### 通过代码调用

```csharp
// 注入服务
var pipelineService = serviceProvider.GetRequiredService<AiPipelineService>();

// 执行 Pipeline
var result = await pipelineService.ExecutePipelineAsync(
    prompt: "生成 TODO 应用",
    projectName: "todo-app",
    targetProjectDir: "/path/to/project",
    timeout: 3600
);

if (result.Success)
{
    Console.WriteLine($"成功！生成了 {result.GeneratedFiles.Count} 个文件");
}
```

### AI 辅助重构

```csharp
var refactorService = serviceProvider.GetRequiredService<AiRefactoringService>();

// 重构代码
var result = await refactorService.RefactorAsync(
    filePaths: new List<string> { "Services/OldService.cs" },
    refactorType: RefactorType.CleanCode,
    additionalInstructions: "使用依赖注入和异步模式"
);
```

### 自动生成测试

```csharp
var testGenService = serviceProvider.GetRequiredService<AiTestGenerationService>();

// 为实体生成测试
var result = await testGenService.GenerateEntityTestsAsync(
    projectName: "my-app",
    entityName: "User",
    testFramework: TestFramework.XUnit
);
```

### 自动生成文档

```csharp
var docService = serviceProvider.GetRequiredService<AiDocumentationService>();

// 生成 README
var readmeResult = await docService.GenerateReadmeAsync(
    projectName: "my-app",
    projectDir: "/path/to/project"
);

// 生成 API 文档
var apiDocResult = await docService.GenerateApiDocumentationAsync(
    projectName: "my-app",
    projectDir: "/path/to/project"
);
```

## YAML 配置扩展

可以在 `project.yaml` 中声明 AI 生成规则：

```yaml
aiGeneration:
  rules:
    - name: generate-crud
      trigger: on-create
      pipeline:
        mode: full
        promptTemplate: |
          为实体 {{ENTITY}} 生成完整的 CRUD 功能
      target:
        directory: src/
        filePattern: "{Entity}Controller.cs"
        overwrite: false
      context:
        entities:
          - task
          - project
        includeProjectStructure: true
```

## 高级功能

### HTTP API 模式

如果 Harness 作为独立服务运行：

```json
{
  "AiPipeline": {
    "HarnessHttpEndpoint": "http://localhost:10000"
  }
}
```

### 自定义 Prompt 模板

```csharp
var context = await contextAdapter.BuildContextAsync("my-project");
var prompt = $@"
请根据以下项目上下文完成任务：

{context}

## 任务
{taskDescription}

## 要求
- 遵循项目现有代码风格
- 包含必要的错误处理
- 添加单元测试
";
```

## 故障排除

### Harness 未找到

```
错误：Harness 目录不存在，请配置 AiPipeline:HarnessDirectory
```

**解决方案**：检查 `appsettings.json` 中的 `HarnessDirectory` 配置

### Python 未找到

```
错误：无法启动进程: python3
```

**解决方案**：
- 安装 Python 3.8+
- 或配置 `PythonExecutable` 为正确路径

### 超时

```
错误：执行超时 (3600秒)
```

**解决方案**：
- 增加 `timeout` 参数值
- 或检查 AI CLI 是否正常工作

## 最佳实践

1. **合理设置超时** - 复杂任务可能需要更长时间
2. **审查生成结果** - AI 生成的代码需要人工审核
3. **使用版本控制** - 在应用 AI 生成结果前先提交当前更改
4. **渐进式应用** - 先在测试项目上验证，再应用到生产项目
5. **成本监控** - 注意 AI CLI 的调用成本，特别是 Claude 等付费模型

## 安全考虑

- AI 生成的代码可能包含安全隐患，务必进行代码审查
- 不要在 prompt 中包含敏感信息（API 密钥、密码等）
- 生产环境建议使用 HTTP API 模式，隔离 Harness 服务

## 未来规划

- [ ] 支持更多 AI 后端（OpenAI API、Azure OpenAI 等）
- [ ] 改进上下文传递和项目理解
- [ ] 添加代码审查自动化
- [ ] 支持实时 AI 辅助编码

## 相关链接

- [NetYamlForge 主文档](../README.md)
- [Harness 设计文档](/home/ubuntu/ws/harness-new/DESIGN.md)
- [AI CLI 配置](../docs/ai-cli-config.md)
