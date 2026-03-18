# 通用 Entity Hooks 使用文档

本文档介绍 NetYamlForge 系统中的实体钩子（Entity Hooks）及其使用方法。

## 目录

- [概述](#概述)
- [框架通用 Hooks](#框架通用-hooks)
- [项目专用 Hooks](#项目专用-hooks)
- [使用示例](#使用示例)

## 概述

Entity Hooks 是在实体 CRUD 操作的生命周期中自动执行的回调函数。系统支持两种类型的 hooks：

1. **框架通用 Hooks**: 所有项目共享的通用功能
2. **项目专用 Hooks**: 每个项目独立的专用功能

### Hook 执行优先级

```
项目专用 Hook → 框架通用 Hook
```

当在 `entities.yml` 中配置 hook 时，系统会先查找项目专用的 hook，如果找不到再使用框架通用 hook。

### 配置位置

- **框架通用 Hooks**: `Services/Hooks/CommonHooks.cs`
- **项目专用 Hooks**: `projects/{项目名}/Hooks/*.cs`

### Hook 配置语法

```yaml
hooks:
  beforeCreate: "hook_name"              # 无参数
  beforeCreate: "hook_name:Param1"       # 单参数
  beforeCreate: "hook_name:Param1,Param2" # 多参数
  beforeCreate: "hook_name:Field:Value"  # 键值对参数
```

---

## 框架通用 Hooks

### `validate_email` - 邮箱格式验证

验证指定字段是否为有效的电子邮件地址格式。

**配置格式**: `validate_email:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "validate_email:Email,BackupEmail"
  beforeUpdate: "validate_email:Email,BackupEmail"
```

### `validate_phone` - 电话号码格式验证

验证指定字段是否为有效的电话号码格式（支持国际和国内格式）。

**配置格式**: `validate_phone:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "validate_phone:Phone,MobilePhone"
```

### `validate_url` - URL 格式验证

验证指定字段是否为有效的 URL 格式。

**配置格式**: `validate_url:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "validate_url:Website,ProfileUrl"
```

### `validate_regex` - 正则表达式验证

验证指定字段是否匹配指定的正则表达式模式。

**配置格式**: `validate_regex:Field:Pattern` 或 `validate_regex:Field1:Pattern1|Field2:Pattern2`

**示例**:
```yaml
hooks:
  # 验证产品代码格式：3 个大写字母 + 3 位数字
  beforeCreate: "validate_regex:Code:^[A-Z]{3}[0-9]{3}$"
  
  # 多个字段验证
  beforeCreate: "validate_regex:Phone:^[0-9]{10,11}|ZipCode:^[0-9]{5}$"
```

### `validate_range` - 数值范围验证

验证指定字段的数值是否在指定范围内。

**配置格式**: `validate_range:Field:Min:Max`

**示例**:
```yaml
hooks:
  # 年龄必须在 0-150 之间
  beforeCreate: "validate_range:Age:0:150"
  
  # 价格必须在 0.01-999999.99 之间
  beforeCreate: "validate_range:Price:0.01:999999.99"
```

### `validate_unique` - 唯一性验证

验证指定字段的值在数据库中是否唯一。

**配置格式**: `validate_unique:Field:Table`

**示例**:
```yaml
hooks:
  # 验证邮箱在 Customer 表中唯一
  beforeCreate: "validate_unique:Email:Customer"
  beforeUpdate: "validate_unique:Email:Customer"
```

### `validate_required` - 必填字段验证

验证指定字段是否已填写（非空）。

**配置格式**: `validate_required:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "validate_required:Name,Code"
```

---

## 数据转换类 Hooks

### `trim` - 去除空白

去除指定字段的前后空白字符。

**配置格式**: `trim:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "trim:Name,Email,Address"
  beforeUpdate: "trim:Name,Email,Address"
```

### `uppercase` - 转换为大写

将指定字段的字符串转换为大写。

**配置格式**: `uppercase:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "uppercase:CountryCode,ProductCode"
```

### `lowercase` - 转换为小写

将指定字段的字符串转换为小写。

**配置格式**: `lowercase:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "lowercase:Email,Username"
```

### `titlecase` - 首字母大写

将指定字段的字符串转换为首字母大写格式（Title Case）。

**配置格式**: `titlecase:Field1,Field2,...`

**示例**:
```yaml
hooks:
  beforeCreate: "titlecase:FirstName,LastName,City"
```

### `default` - 设置默认值

为指定字段设置默认值（仅当字段为空时）。

**配置格式**: `default:Field1:Value1|Field2:Value2`

**示例**:
```yaml
hooks:
  # 设置状态默认为 Active，国家默认为 USA
  beforeCreate: "default:Status:Active|Country:USA"
```

### `now` - 设置当前时间

将指定字段设置为当前日期时间。

**配置格式**: `now:Field1,Field2,...`

**示例**:
```yaml
hooks:
  # 创建时设置创建时间
  beforeCreate: "now:CreatedAt"
  
  # 更新时设置更新时间
  beforeUpdate: "now:UpdatedAt"
```

### `current_user` - 设置当前用户

将指定字段设置为当前操作用户名。

**配置格式**: `current_user:Field1,Field2,...`

**示例**:
```yaml
hooks:
  # 创建时设置创建人
  beforeCreate: "current_user:CreatedBy"
  
  # 更新时设置更新人
  beforeUpdate: "current_user:UpdatedBy"
```

---

## 审计日志与通知类 Hooks

### `audit_log` - 审计日志

记录操作内容到审计日志（无需参数）。

**示例**:
```yaml
hooks:
  afterCreate: "audit_log"
  afterUpdate: "audit_log"
```

### `webhook` - Webhook 通知

操作完成后向指定 URL 发送 POST 请求。

**配置格式**: `webhook:URL`

**示例**:
```yaml
hooks:
  # 创建后发送通知到外部系统
  afterCreate: "webhook:https://example.com/api/notify"
```

---

## 关联数据操作类 Hooks

### `update_count` - 更新关联计数

更新关联表中的计数值。

**配置格式**: `update_count:SourceEntity:SourceKey:TargetTable:TargetForeignKey`

**示例**:
```yaml
hooks:
  # Customer 创建时，增加其关联的 Order 计数
  afterCreate: "update_count:Customer:CustomerId:Orders:CustomerId"
```

### `update_related` - 更新关联记录

更新关联表中的指定字段。

**配置格式**: `update_related:SourceEntity:SourceKey:TargetTable:TargetFK:UpdateField:UpdateValue`

**示例**:
```yaml
hooks:
  # Customer 创建时，将其关联的 Order 状态设置为 Active
  afterCreate: "update_related:Customer:CustomerId:Orders:CustomerId:Status:Active"
```

---

## 软删除 Hooks

### `soft_delete` - 软删除

将删除操作转换为设置删除标志和删除时间。

**配置格式**: `soft_delete:DeletedFlagColumn:DeletedAtColumn:DeletedByColumn`

**示例**:
```yaml
# 注意：软删除需要在实体定义中设置 softDelete: true
entities:
  customer:
    softDelete: true
    hooks:
      beforeDelete: "soft_delete:IsDeleted:DeletedAt:DeletedBy"
```

---

## 使用示例

### 完整示例：Customer 实体

```yaml
entities:
  customer:
    table: Customer
    key: CustomerId
    displayName: Customer
    softDelete: false
    
    # Hook 配置
    hooks:
      # 创建前：去除空白、验证邮箱、设置默认值和创建时间
      beforeCreate: "trim:Name,Email|validate_email:Email|default:Status:Active|now:CreatedAt|current_user:CreatedBy"
      
      # 创建后：记录审计日志
      afterCreate: "audit_log"
      
      # 更新前：验证邮箱唯一性、设置更新时间
      beforeUpdate: "validate_unique:Email:Customer|now:UpdatedAt|current_user:UpdatedBy"
      
      # 更新后：记录审计日志
      afterUpdate: "audit_log"
    
    columns:
      CustomerId:
        type: int
        identity: true
      Name:
        type: string
        required: true
      Email:
        type: email
        required: true
      Status:
        type: string
      CreatedAt:
        type: datetime
      CreatedBy:
        type: string
      UpdatedAt:
        type: datetime
      UpdatedBy:
        type: string
    
    forms:
      Name:
        type: string
        required: true
        editable: true
      Email:
        type: email
        required: true
        editable: true
      Status:
        type: string
        editable: true
```

### 组合使用多个 Hooks

可以在一个钩子点组合使用多个 hooks，使用 `|` 分隔：

```yaml
hooks:
  beforeCreate: "trim:Name,Email|validate_email:Email|lowercase:Email|now:CreatedAt"
```

### 自定义 Hook

如果内置 hooks 不满足需求，可以创建自定义 hook：

### 方法 1: 添加到框架通用 Hooks

1. 在 `Services/Hooks/CommonHooks.cs` 中添加实现 `IEntityHook` 接口的类
2. 在 `Program.cs` 中注册：`builder.Services.AddSingleton<IEntityHook, YourCustomHook>();`
3. 在 `entities.yml` 中配置使用

```csharp
public class YourCustomHook : IEntityHook
{
    public string Name => "your_custom_hook";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 自定义前处理逻辑
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 自定义后处理逻辑
        return Task.CompletedTask;
    }
}
```

### 方法 2: 创建项目专用 Hooks（推荐）

创建项目专用的 hook 不会影响其他项目。详见 [项目专用 Hooks 使用指南](./PROJECT_HOOKS_GUIDE.md)。

1. 在项目目录下创建 `Hooks/` 目录：`projects/{项目名}/Hooks/`
2. 创建实现 `IEntityHook` 接口的类
3. 系统会自动加载并注册

```csharp
// projects/chinook/Hooks/MyCustomHook.cs
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.Chinook.Hooks;

public class MyCustomHook : IEntityHook
{
    public string Name => "my_custom_hook";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 仅 chinook 项目可用的自定义逻辑
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

---

## 注意事项

1. **事务处理**: `before*` hooks 在事务开始前执行，`after*` hooks 在同一事务内执行
2. **取消操作**: `before*` hooks 返回 `HookResult.Abort(message)` 可取消 CRUD 操作
3. **值修改**: `before*` hooks 可以修改 `ctx.Values` 来改变提交的数据
4. **性能考虑**: 避免在 hooks 中执行耗时操作，特别是 `before*` hooks
5. **错误处理**: `after*` hooks 中抛出异常会导致事务回滚
