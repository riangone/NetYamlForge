# YAML 与 C# 实现同步工作流指南

## 1. 背景与问题
在 NetYamlForge 框架中，业务逻辑是通过 YAML 定义（如 `hooks` 和 `actions`）并由 C# 实现（`IEntityHook` 和 `IActionHandler`）承载的。
常见的开发痛点是：**在 YAML 中定义了钩子或动作，但忘记在后台编写对应的 C# 代码**，导致功能点击无响应或逻辑缺失。

为了解决此问题，框架内置了“定义即实现”的同步工作流。

---

## 2. 核心机制

### 2.1 启动验证 (YamlConfigStartupValidator)
应用在启动时会自动扫描所有子项目的 YAML 配置，并与已加载的 C# 类型进行对比。
- **功能**：检测所有已定义的 `hooks` 和 `action_handlers` 是否有对应的 C# 实现类。
- **反馈**：如果发现缺失实现，会在控制台输出 `[WARN]` 日志，例如：
  `yaml_config_warn event=unimplemented_hook project=blog entity=post hook=generate_slug`
- **建议**：在开发阶段，请密切关注启动日志中的 `yaml_config_summary`。

### 2.2 脚手架生成器 (--scaffold-missing-hooks)
这是一个命令行工具，可以根据 YAML 定义自动生成缺失的 C# 桩代码（Stubs）。
- **命令**：`dotnet run -- --scaffold-missing-hooks --project=<项目名>`
- **行为**：
    - 扫描指定项目的所有 YAML。
    - 找出所有未实现的 Hook 和 Action。
    - 在该项目的 `Hooks/` 目录下生成 `[ProjectName]MissingHooks.cs` 和 `[ProjectName]MissingActionHandlers.cs`。
- **安全性**：**该工具不会覆盖现有的自定义代码**。它只会将缺失的部分放入专门的“Missing”文件中。

---

## 3. 标准开发流程

### 场景 A：创建新功能/新项目
1.  **YAML 设计**：在 `projects/<name>/` 下编写 YAML，定义所需的 `hooks` 和 `actions`。
2.  **生成桩代码**：
    ```bash
    dotnet run -- --scaffold-missing-hooks --project=<name>
    ```
3.  **实现逻辑**：打开生成的 `.cs` 文件，将 `return HookResult.Success();` 替换为实际的业务代码。

### 场景 B：维护已有项目
1.  **检查缺失**：运行应用，查看是否有 `unimplemented_hook` 警告。
2.  **同步代码**：如果发现缺失，运行上述脚手架命令。
3.  **整理代码**：生成的桩代码可以保留在 `MissingHooks.cs` 中，也可以手动迁移到更具语义的文件名（如 `PostHooks.cs`）中。

---

## 4. 最佳实践
- **AI 助手准则**：当用户要求“增加一个发布按钮”或“增加自动生成编号”时，AI 应先修改 YAML，然后立即运行 `--scaffold-missing-hooks` 生成代码，最后填充逻辑。
- **文件组织**：虽然工具会生成 `MissingHooks.cs`，但建议随着项目成熟，将成熟的逻辑移至独立的类文件中。
- **CI 门禁**：建议在 CI 流程中检查启动日志，若存在 `unimplemented` 警告则阻止合并。

---

## 5. 常用命令参考
```bash
# 为 blog 项目补全缺失实现
dotnet run -- --scaffold-missing-hooks --project=blog

# 为所有项目运行验证（默认随应用启动执行）
dotnet run
```
