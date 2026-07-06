# 03 — EntityMetadata / PageDefinition 拆文件

> 目标文件: `NetYamlForge/Models/EntityMetadata.cs`（637）、`NetYamlForge/Models/PageDefinition.cs`（561）
> 类型: **纯拆文件**（零行为变化） · 风险: 低 · 依赖: 无

## 1. 现状分析（重要校准）

**这两个文件"胖"，但里面几乎没有需要外移的业务逻辑。** 实测方法均为：
- I18n 展示助手：`GetLabel(fallback) => I18nText.Resolve(...)`（多处，第 155/193/225/401/444/533 行等）
- 计算属性：`IsCompositeKey`(305)、`GetPrimaryKeyColumns`(293)、`GetVisibleColumnNames`、`GetEffectivePageSize` 等
- 简单排序/过滤助手：`GetOrderedForms`(309)、`GetOrderedFilters`(329)、`GetFormFields`(363)

这些属于"配置模型的自然行为"（展示与投影），**保留在模型上是合理的**，不构成"Models 里混进业务逻辑"。因此本项**不是外移逻辑，而是把一个巨型文件按类型切成多个小文件**，降低单文件复杂度、改善可维护性与 diff 可读性。

> `EntityMetadata.cs` 一个文件里定义了 **~25 个类**（`ForeignKeyDefinition`、`JoinDefinition`、`FormDefinition`、`ColumnDefinition`、`EntityDefinition`、`ActionDefinition`、`ExportDefinition`、`SecurityDefinition` …）。这是主要问题。

## 2. 目标结构（同命名空间多文件，零调用方改动）

命名空间保持 `NetYamlForge.Models` 不变。按聚合边界切分：

```
Models/Entity/
  EntityDefinition.cs         // EntityDefinition + EntityConfigRoot + EntityLayoutDefinition
  ColumnDefinition.cs         // ColumnDefinition + IColumnDef + ThumbnailSizeDefinition + PagingDefinition
  FormDefinition.cs           // FormDefinition + FormLayoutDefinition + ConfirmationDefinition
  FilterDefinition.cs         // FilterDefinition + FilterLayoutDefinition
  RelationDefinition.cs       // ForeignKeyDefinition + JoinDefinition + EntityLinkDefinition
  ActionDefinition.cs         // ActionDefinition + ActionInputField + ActionHooksDefinition + EntityHooksDefinition
  ExportDefinition.cs         // ExportDefinition + PdfExportOptions + PdfColumnOptions
  SecurityDefinition.cs       // SecurityDefinition + PermissionsDefinition + RowLevelSecurityDefinition + ValidatorDefinition

Models/Page/
  PageDefinition.cs           // PageDefinition + CalendarUiDefinition + CalendarHolidayDefinition
  SectionDefinition.cs        // SectionDefinition + 相关 Section* 计算方法
  SectionColumnDef.cs         // SectionColumnDef + SectionActionDefinition
  SectionFormDef.cs           // SectionFormGroupDef + SectionFormFieldDef + FormSectionFieldDef + ExtraFieldDefinition
  PageFilterDefinition.cs     // PageFilterDefinition
  SectionHooksDefinition.cs   // SectionHooksDefinition（含 GetExpandedHooks）
```

> 目录名仅用于物理组织；**不改命名空间**（避免大范围 `using` 改动）。若团队规范要求命名空间随目录，则需同步全仓 `using`——默认**不推荐**，成本高收益低。

## 3. 操作步骤（机械、低风险）

1. 每个类**整段剪切**到目标文件（含 XML 注释、attributes）。
2. 新文件顶部补齐必要 `using`（`System.Collections.Generic`、`NetYamlForge.Localization` 等，按编译器提示补）。
3. 原文件删空或保留最核心的 `EntityDefinition`/`PageDefinition`。
4. **不改任何方法体**。`I18nText.Resolve` 调用、计算属性逻辑逐字保留。
5. `dotnet build` 逐步验证；一次搬一两个类，编译通过再继续。

## 4. 反面清单（禁止在本 PR 做）

- ❌ 不要把 `GetLabel`/`GetDisplayName` 改成扩展方法或搬到 service（那是无谓的破坏性改动）。
- ❌ 不要改类的可见性/命名。
- ❌ 不要顺手改 YAML 反序列化相关的 property attribute。

## 5. 测试策略

- 纯移动，现有反序列化测试（`SectionActionsDeserializationTests`、`YamlSchemaValidationTests`、`EntityHooksDefinitionTests` 等）即为护栏。
- 无需新增测试。

## 6. 验收标准

- [ ] `EntityMetadata.cs` / `PageDefinition.cs` 拆分后**无单文件 > 250 行**
- [ ] 命名空间不变，调用方零改动（`git diff` 只见文件新增/删除，不见引用改动）
- [ ] build + 全部反序列化测试全绿
