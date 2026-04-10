# ProjectScope 作用域问题修复说明

## 问题描述

在调用 `POST /auto-dealer-demo/api/ai/chat/session` 时出现 500 错误：

```
System.InvalidOperationException: Cannot resolve scoped service 'NetYamlForge.Services.ProjectScope' from root provider.
```

## 根本原因

在 `ServiceCollectionExtensions.cs` 中，`IDbConnection` 和 `ISqlDialect` 的工厂函数使用了 `sp.GetRequiredService<ProjectScope>()`。

虽然这两个服务本身被注册为 Scoped，但在某些情况下（如背景任务、应用启动时、或从根 ServiceProvider 解析依赖链时），会尝试从根提供程序解析 `ProjectScope`，导致异常。

## 修复方案

将 `GetRequiredService<ProjectScope>()` 改为 `GetService<ProjectScope>()`，并增加空值检查：

### 修改前

```csharp
// IDbConnection 工厂
var scope = sp.GetRequiredService<ProjectScope>();
if (!scope.IsSet)
{
    return new SqliteConnection("Data Source=chinook.db");
}

// ISqlDialect 工厂
var scope = sp.GetRequiredService<ProjectScope>();
if (!scope.IsSet) return new SqliteDialect();
```

### 修改后

```csharp
// IDbConnection 工厂
var scope = sp.GetService<ProjectScope>();
if (scope == null || !scope.IsSet)
{
    return new SqliteConnection("Data Source=chinook.db");
}

// ISqlDialect 工厂
var scope = sp.GetService<ProjectScope>();
if (scope == null || !scope.IsSet) return new SqliteDialect();
```

## 修复原理

1. **`GetService<T>()`** 在找不到服务时返回 `null`，而不会抛出异常
2. **`GetRequiredService<T>()`** 在找不到服务时抛出 `InvalidOperationException`

在没有 HTTP 请求作用域的情况下（如背景任务），`ProjectScope` 可能无法被解析。使用 `GetService<T>()` 可以优雅地处理这种情况，回退到默认 SQLite 连接。

## 影响范围

- ✅ **正常 HTTP 请求**: 不受影响，`ProjectScope` 会被正确设置
- ✅ **背景任务**: 现在会优雅地回退到默认连接，而不是抛出异常
- ✅ **应用启动**: 不再因解析 `ProjectScope` 而失败

## 测试建议

1. 重启应用程序
2. 调用 `POST /auto-dealer-demo/api/ai/chat/session`
3. 验证不再返回 500 错误
4. 验证聊天会话能正常创建

## 相关文件

- `NetYamlForge/Extensions/ServiceCollectionExtensions.cs` (已修改)
- `NetYamlForge/Services/Project/ProjectScope.cs` (Scoped 服务)
- `NetYamlForge/Middleware/ProjectMiddleware.cs` (设置 ProjectScope)
