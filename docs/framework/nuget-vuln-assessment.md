# NuGet 依赖漏洞治理评估

> 生成基线：commit 8cefc8b（分支 nyf）。数据来源：`dotnet list package --vulnerable --include-transitive` 实测，非估算。
> 重要更正：早期口述中「SQLitePCLRaw 不存在 / ImageSharp 仅在 Tooling」的说法有误。实测显示两个漏洞同时波及 **core（NetYamlForge）、FormForge、Tooling、Tests** 四个工程。

## 一、实测漏洞清单

| 包 | 现版本 | 严重度 | Advisory | 引入方式 | 波及工程 |
|---|---|---|---|---|---|
| `SixLabors.ImageSharp` | 3.1.10 | **Moderate** | GHSA-rxmq-m78w-7wmc | **直接引用**（`NetYamlForge.csproj:44`），并经 Tooling/Tests 传递 | NetYamlForge / Tooling / Tests |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | **High** | GHSA-2m69-gcr7-jv3q | **传递依赖**，经 `Microsoft.Data.Sqlite 10.0.3`（`NetYamlForge.csproj:35`、`FormForge.csproj:10`）拉入 | NetYamlForge / FormForge / Tooling / Tests |

## 二、逐项处置方案

### 1. SixLabors.ImageSharp（Moderate）— 低风险，立即可修

- **修复版本可用**：nuget.org 上 `3.1.11`、`3.1.12` 均已发布，与现用 `3.1.10` **同主版本**，属补丁级升级，API 无破坏。
- **动作**：将 `NetYamlForge.csproj` 的 `SixLabors.ImageSharp` 直接引用 `3.1.10 → 3.1.12`。Tooling/Tests 通过传递依赖自动收敛。
- **风险**：极低。仅需回归图像相关批处理（PhotoAnnotator 等已迁至 projects，但 core 仍保留 ImageSharp 图像基础能力）。
- **验证**：`dotnet build` + 图像处理相关用例。

### 2. SQLitePCLRaw.lib.e_sqlite3（High）— 需验证，不宜盲目升级

- **关键约束**：`2.1.x` 系列的**最后一版就是 2.1.11，且它本身即为受影响版本**——在 2.1 线内**无修复版本可选**。修复只存在于 SQLitePCLRaw 重新对齐 SQLite 版本号后的 **`3.50.x` 主版本线**。
- **它是传递依赖**：由 `Microsoft.Data.Sqlite 10.0.3` 硬拉 `2.1.11`。因此有两条路径：
  - **路径 A（首选，等待/跟随上游）**：升级 `Microsoft.Data.Sqlite` 到引用了 `SQLitePCLRaw 3.50.x` 的补丁版本（如 `10.0.x` 后续修订）。最干净，避免手工钉死传递依赖。**需确认该补丁是否已发布**。
  - **路径 B（即时缓解）**：在受影响工程显式加一条直接引用覆盖传递版本：
    ```xml
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.50.3" />
    ```
    因跨主版本（2.1 → 3.50，含原生 `e_sqlite3` 库替换），**必须全量回归所有 SQLite 落库路径**（DynamicEntity、BatchJob 落库、Tenant 多租户库、FormForge）。
- **风险**：中。原生库主版本跳跃，需实测 CRUD/事务/迁移无回归。
- **建议**：先走路径 A 查上游补丁；若短期无补丁且 High 需尽快封堵，则走路径 B 并挂全量集成测试守护。

## 三、建议执行顺序（按 ROI）

1. **立即**：ImageSharp `3.1.10 → 3.1.12`（低风险、直接引用、`dotnet build` 即验证）。
2. **短期**：核实 `Microsoft.Data.Sqlite` 是否有引用 SQLitePCLRaw 3.50.x 的补丁版；有则走路径 A。
3. **兜底**：若 High 需紧急封堵且无上游补丁，走路径 B 显式覆盖 + 全量 SQLite 集成回归。
4. **守护**：在 CI 增加 `dotnet list package --vulnerable --include-transitive` 门禁，High 视为构建失败。

## 四、与本次瘦身重构的关系
两漏洞均为**存量依赖问题，与 CORE-SLIMMING 重构无因果**。此前 `dotnet build` 报出的 NU1902/NU1903 即此二者，不影响重构正确性；单独立项治理。
