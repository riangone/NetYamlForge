# R2-01 — JSON Schema 启动 fail-fast + CI lint 门禁

> 范围: `YamlSchemaValidator` / `YamlConfigStartupValidator` / `projects/**/*.yml` / CI
> 类型: 配置护栏 · 风险: 中（启动行为可能失败） · 依赖: 无
> **行为变化: 是** — 在 `strict` 模式下，非法 YAML 会让应用**启动即失败**（而非运行时 500）。

## 1. 现状（实测）

- 4 份 JSON Schema 已入库：`docs/framework/schemas/{entities,pages,project,dashboard}.schema.json`。
- `NetYamlForge/Services/YamlSchemaValidator.cs` **已存在**，基于 `JsonSchema.Net` 8.0.0，已实现
  `ValidateProjectYaml / ValidateUiPageYaml / ValidateEntityYaml / ValidateDashboardYaml`，
  违规抛 `InvalidOperationException`。**但目前是"可被调用的工具类"，未在启动时对全体 project 强制执行。**
- `Services/Validation/YamlConfigStartupValidator.cs`（`IHostedService`）已在启动扫描全项目，
  但只做**类型/Hook 引用**检查，且**只 Warn 不 Fail**。

> 结论：本项**不是从零实现校验器**，而是把已有的 `YamlSchemaValidator` 接入启动流程 + CI，并统一失败策略。

## 2. 目标

1. 启动时对**所有** `projects/**` 的 YAML（entities / pages / project.yaml / dashboard）逐文件跑 schema 校验。
2. 失败策略可配置：`Off` / `Warn`（默认，兼容现状）/ `Strict`（fail-fast）。
3. 提供**独立 CLI lint** 入口，供 CI 在不启动完整应用的情况下校验 `projects/**/*.yml`。
4. 报错信息包含**文件路径 + JSON Pointer 定位 + 具体违规原因**（`JsonSchema.Net` 的 `EvaluationResults` 已能给出）。

## 3. 设计

### 3.1 配置项（`appsettings.json`）
```jsonc
"Forge": {
  "SchemaValidation": {
    "Mode": "Warn",              // Off | Warn | Strict
    "FailFastOnStartup": false,  // Strict 时是否阻断启动（true=抛出中止 Host）
    "IncludeGlobs": [ "projects/**/*.yml", "projects/**/*.yaml" ],
    "ExcludeGlobs": [ "**/_disabled/**" ]
  }
}
```
绑定为 `SchemaValidationOptions`（`Services/Validation/`），经 `IOptions<>` 注入。

### 3.2 校验编排器 `SchemaValidationRunner`
新增 `Services/Validation/SchemaValidationRunner.cs`：
- 输入：项目根目录 + `SchemaValidationOptions`。
- 遍历 glob，按**文件所在目录/文件名约定**（`entities/` → entity schema，`pages/` → ui-page schema，`project.yaml` → project schema，`dashboard.yml` → dashboard schema）选对应 `YamlSchemaValidator` 方法。
- 聚合所有违规为 `IReadOnlyList<SchemaViolation>`（`FilePath`, `Pointer`, `Message`, `SchemaName`）。
- **不在此处决定 Warn/Fail**——只返回结果，由调用方（启动 or CLI）按 Mode 处置。

```csharp
public sealed record SchemaViolation(string FilePath, string Pointer, string Message, string SchemaName);

public sealed class SchemaValidationRunner
{
    public IReadOnlyList<SchemaViolation> ValidateAll(string projectsRoot, SchemaValidationOptions opt);
}
```
> `YamlSchemaValidator` 当前抛异常；为聚合报告，需新增**不抛异常、返回 `EvaluationResults`** 的重载（如 `TryValidateEntityYaml(...)`），供 Runner 收集全部违规而非首个即中断。保留旧抛异常方法不动，向后兼容。

### 3.3 接入启动
在 `YamlConfigStartupValidator`（已存在的 `IHostedService`）中调用 `SchemaValidationRunner`：
- `Mode=Off`：跳过。
- `Mode=Warn`：`_logger.LogWarning`（复用 `ForgeLog` EventId，新增 `SchemaViolation = new(4200,...)`），沿用第一轮结构化日志约定。
- `Mode=Strict` + `FailFastOnStartup=true`：抛 `OptionsValidationException` 中止 Host（`IHost.StartAsync` 失败）。
- `Mode=Strict` + `FailFastOnStartup=false`：`LogError` 但不阻断（用于生产灰度）。

### 3.4 CLI lint 入口（CI 用）
新增一个轻量入口，二选一：
- **首选**：给主程序加 `--validate-schemas` 启动参数，命中即"只跑 Runner → 打印报告 → `Environment.Exit(violations==0?0:1)`"，不进 Web 主循环。
- 备选：独立 `NetYamlForge.SchemaLint` 控制台工程（引用主工程的 Runner）。

CLI 输出建议 GitHub Actions 注解格式：
```
::error file=projects/foo/entities/order.yml,line=1::[entity] /columns/3/type: 'moneyy' is not a valid column type
```

### 3.5 CI 门禁（`.github/workflows/build-and-test.yml`）
在 build 之后、test 之前加一步：
```yaml
- name: Validate project YAML against schemas
  run: dotnet run --project NetYamlForge -c Release -- --validate-schemas
```
> 注意：schema 校验只覆盖 YAML **结构**；类型/Hook 引用检查仍归 `YamlConfigStartupValidator` 原逻辑，两者互补，不要合并。

## 4. 落地顺序（分 PR）

1. **PR-1**：`SchemaValidationOptions` + `YamlSchemaValidator` 增加 `TryValidate*` 非抛出重载（纯新增，零行为变化）。
2. **PR-2**：`SchemaValidationRunner` + 单元测试（喂已知合法/非法 YAML fixture，断言违规条数与 Pointer）。
3. **PR-3**：接入 `YamlConfigStartupValidator`，默认 `Warn`（**此时先不改任何现有部署行为**）。
4. **PR-4**：`--validate-schemas` CLI 入口 + CI step（CI 里可先设 `continue-on-error: true` 观察一轮，再转硬门禁）。
5. **PR-5**（可选）：将默认 Mode 切到 `Strict`，需先确认现网所有 `projects/**` 已零违规。

## 5. 边界与风险

- **现存违规兜底**：切 Strict 前必须先跑一遍全量 lint，把 `projects/**` 存量违规清零，否则会阻断启动。PR-3 的 Warn 模式就是为此收集清单。
- **schema 与代码漂移**：schema 是手写的，可能与实际 Model 不同步。建议 R2-04 之后补一个"schema ↔ Model 一致性"测试（反射 Model 属性名比对 schema `properties`），列为后续项，不阻塞本文档。
- **性能**：启动期校验数百个 YAML，`JsonSchema.Net` 需 `SchemaRegistry` 复用 + schema 只编译一次（`YamlSchemaValidator` 已用静态缓存 `_lock`，沿用即可）。

## 6. 验收标准

- [ ] `SchemaValidationOptions` 三态可配置，默认 `Warn`，可经环境变量覆盖
- [ ] `SchemaValidationRunner` 有单测：合法 fixture 0 违规、非法 fixture 命中预期 Pointer
- [ ] 启动接入后，非法 YAML 在 `Warn` 下产生结构化日志（含 `FilePath` + `Pointer`）
- [ ] `--validate-schemas` 返回正确退出码（0/1），输出 GH 注解格式
- [ ] CI 新增校验步骤，PR 引入非法 YAML 时 CI 变红
- [ ] 现有 441+ 测试全绿；默认配置下现网启动行为不变
