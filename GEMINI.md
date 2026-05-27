# NetYamlForge AI 指令集

## 核心工作流：YAML 与 C# 同步 (定义即实现)
为防止“YAML 定义了功能但后台未实装”的 Bug，必须严格执行以下双重防错机制：

### 1. 强制修改规范
- **禁止“空定义”**：严禁在 YAML 中定义 `hooks` 或 `actions` 却不提供对应的 C# 实现。
- **自动脚手架 (Method 2)**：修改 YAML 后，**必须**立即执行以下命令生成实现桩代码：
  ```bash
  dotnet run -- --scaffold-missing-hooks --project=<项目名>
  ```
  该工具会自动在项目的 `Hooks/` 目录下补全缺失的 `.cs` 文件。

### 2. 自我验证要求 (Method 1)
- **启动日志检查**：在交付任务前，必须启动项目并检查控制台输出。
- **零警告准则**：确认 `yaml_config_summary` 中没有针对该项目的 `unimplemented_hook` 或 `unimplemented_action_handler` 警告。
- **手动验证**：如果日志中出现 `yaml_config_warn`，说明同步失败，必须回溯检查。

### 3. AI 任务执行 checklist
当你执行业务逻辑修改时，请按此顺序操作：
1.  **Research**: 读取 `projects/<project>/<entity>.yml` 确认现有定义。
2.  **Design**: 在 YAML 中增加或修改 `hooks` 或 `actions` 定义。
3.  **Scaffold**: 运行 `dotnet run -- --scaffold-missing-hooks --project=<project>`。
4.  **Implement**: 在生成的 `Hooks/*.cs` 或 `ActionHandlers.cs` 中填充业务逻辑。
5.  **Build**: 运行 `dotnet build` 确保无编译错误。
6.  **Verify**: 运行项目，确认无 `unimplemented` 警告日志。

## 参考文档
- **详细指南**：`docs/guides/YAML-CSHARP-CONSISTENCY-WORKFLOW.md`
- **防错原理**：`docs/guides/PREVENT-UNIMPLEMENTED-CONFIGS.md` (新)
