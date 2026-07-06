# NetYamlForge 核心框架基本功能改进与进化详细设计书

本设计书针对 NetYamlForge 框架的非 AI 底层基础功能缺陷，提供具体的重构与改进方案。这些改进能够消除现有系统的安全隐患，并将其底座扩展至企业级多数据库环境。

---

## 1. 数据库迁移系统 (Schema Migration) 跨平台方言扩展

### 1.1 现状与痛点
当前 [DynamicEntitySchemaMigrationService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/DynamicEntity/DynamicEntitySchemaMigrationService.cs) 的物理列检查 (`GetPhysicalColumnsAsync`) 和 DDL 语句生成 (`GenerateSql`) 显式限制了只能在 SQLite 和 PostgreSQL 上运行，非二者会抛出 `NotSupportedException`。这使得在 MySQL 或 SQL Server 物理数据源上使用动态 YAML 实体配置时，无法利用自动 Schema 迁移和迁移 Dry-run 功能。

### 1.2 改进设计方案

#### A. 物理列结构检查扩展 (GetPhysicalColumnsAsync)
为 MySQL 和 SQL Server 编写对应的 Metadata 查询，从而打通列信息提取通道。

*   **MySQL 查询设计**：
    ```sql
    SELECT 
        ORDINAL_POSITION AS Cid, 
        COLUMN_NAME AS Name, 
        DATA_TYPE AS Type, 
        (IS_NULLABLE = 'NO') AS NotNullBool,
        CASE WHEN COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS Pk
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = @TableName
    ORDER BY ORDINAL_POSITION;
    ```
*   **SQL Server 查询设计**：
    ```sql
    SELECT 
        c.ORDINAL_POSITION AS Cid,
        c.COLUMN_NAME AS Name,
        c.DATA_TYPE AS Type,
        CASE WHEN c.IS_NULLABLE = 'NO' THEN 1 ELSE 0 END AS NotNullBool,
        CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS Pk
    FROM INFORMATION_SCHEMA.COLUMNS c
    LEFT JOIN (
        SELECT ku.COLUMN_NAME, ku.TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
    ) pk ON pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
    WHERE c.TABLE_NAME = @TableName
    ORDER BY c.ORDINAL_POSITION;
    ```

#### B. DDL 语法方言适配 (GenerateSql)
为 MySQL 和 SQL Server 提供专属的 DDL 转换模板。与 SQLite 不同，这两个企业级数据库不需要昂贵的表重建（Rename to bak -> Create new -> Insert -> Drop bak），直接生成 `ALTER TABLE` 语句即可：

*   **MySQL 语法模板**：
    *   **新增列**：`ALTER TABLE \`TableName\` ADD COLUMN \`ColName\` Type [NOT] NULL`
    *   **删除列**：`ALTER TABLE \`TableName\` DROP COLUMN \`ColName\``
    *   **修改类型**：`ALTER TABLE \`TableName\` MODIFY COLUMN \`ColName\` NewType`
    *   **修改空值约束**：`ALTER TABLE \`TableName\` MODIFY COLUMN \`ColName\` CurrentType [NOT] NULL`
*   **SQL Server 语法模板**：
    *   **新增列**：`ALTER TABLE [TableName] ADD [ColName] Type [NOT] NULL`
    *   **删除列**：`ALTER TABLE [TableName] DROP COLUMN [ColName]`
    *   **修改类型**：`ALTER TABLE [TableName] ALTER COLUMN [ColName] NewType`
    *   **修改空值约束**：`ALTER TABLE [TableName] ALTER COLUMN [ColName] CurrentType [NOT] NULL`

---

## 2. Roslyn 动态编译钩子 (Project Hooks) 安全沙箱隔离

### 2.1 现状与痛点
在 [ProjectHookLoader.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Project/ProjectHookLoader.cs) 中，自定义 C# 钩子是由 Roslyn 编译器动态加载至主进程的 `AssemblyLoadContext` 中，并启用了 `WithAllowUnsafe(true)`。如果管理员或不受信任的项目 YAML 注入了包含恶意代码的 C# 钩子（如 `System.Diagnostics.Process.Start` 删库或 `System.IO` 越权读写），主进程将会被完全控制，极易引发严重的主机安全危机。

### 2.2 改进设计方案

#### A. 编译器软沙箱限制 (Roslyn Compilation Sandbox)
1.  **禁用 Unsafe 代码**：将 `CSharpCompilationOptions` 调整为 `.WithAllowUnsafe(false)`，从底层阻止利用指针对内存进行直接操作的可能性。
2.  **收紧依赖引用 (Metadata References)**：在 `GetMetadataReferences()` 中进行严格过滤，仅允许加载系统最基础的运行时程序集（如 `System.Runtime`，`System.Collections`），拦截包括 `System.Diagnostics.Process` 所在程序集等高危引用。

#### B. 静态源码 AST 扫描器 (SyntaxTree Verification)
在调用 `compilation.Emit` 之前，通过继承 `CSharpSyntaxWalker` 实现静态代码安全审查网关 `HookSecurityValidator`：

```csharp
public sealed class HookSecurityValidator : CSharpSyntaxWalker
{
    private static readonly HashSet<string> BannedNamespaces = new()
    {
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Threading",
        "System.IO" // 除白名单 API 外拦截
    };

    private static readonly HashSet<string> BannedTypes = new()
    {
        "Process", "Assembly", "File", "Directory", "Path", "Registry", "Type", "Marshal"
    };

    public List<string> ValidationErrors { get; } = new();

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var ns = node.Name.ToString();
        if (BannedNamespaces.Any(banned => ns == banned || ns.StartsWith(banned + ".")))
        {
            ValidationErrors.Add($"不允许导入敏感命名空间: '{ns}' (第 {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1} 行)");
        }
        base.VisitUsingDirective(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        var identifier = node.Identifier.ValueText;
        if (BannedTypes.Contains(identifier))
        {
            ValidationErrors.Add($"禁用的敏感 API/类型调用: '{identifier}' (第 {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1} 行)");
        }
        base.VisitIdentifierName(node);
    }
}
```

---

## 3. 文件上传与媒体服务安全改进与サムネイル落地

### 3.1 现状与痛点
在 [FileUploadService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/FileUploadService.cs) 中：
1.  对于图片类型，系统仅验证了 `ContentType` 和文件名后缀。攻击者容易通过将恶意脚本伪装为 `bad_script.png` 上传，绕过检查。
2.  `GenerateThumbnailAsync` 函数为完全空实现（仅返回 `Task.CompletedTask`），导致缩略图机制完全失效，前端加载大图时产生严重性能负担。

### 3.2 改进设计方案

#### A. 魔法字节 (Magic Bytes) 二进制多媒体校验
不应仅仅信任浏览器请求头自带的 MIME-Type，必须直接读取流的前几个字节头部对格式做真实性判断。
实现一个 `FileSignatureValidator`：

```csharp
public static class FileSignatureValidator
{
    private static readonly Dictionary<string, byte[][]> ImageSignatures = new()
    {
        { ".png",  new byte[][] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".jpeg", new byte[][] { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpg",  new byte[][] { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".gif",  new byte[][] { new byte[] { 0x47, 0x49, 0x46, 0x38 } } },
        { ".webp", new byte[][] { new byte[] { 0x52, 0x49, 0x46, 0x46 } } } // 需匹配 RIFF...WEBP 格式
    };

    public static bool VerifyImageSignature(Stream stream, string extension)
    {
        if (!ImageSignatures.TryGetValue(extension.ToLowerInvariant(), out var sigList))
        {
            return false;
        }

        var maxSigLen = sigList.Max(s => s.Length);
        var buffer = new byte[maxSigLen];
        
        long origPosition = stream.Position;
        int readBytes = stream.Read(buffer, 0, maxSigLen);
        stream.Position = origPosition; // 恢复流指针位置

        foreach (var sig in sigList)
        {
            if (readBytes >= sig.Length && buffer.Take(sig.Length).SequenceEqual(sig))
            {
                return true;
            }
        }
        return false;
    }
}
```

#### B. 使用 ImageSharp 进行サムネイル无损生成
在 `FileUploadService` 中真正集成 `SixLabors.ImageSharp` 库来实现缩略图逻辑：

1.  **处理流程**：
    *   读取主图数据流。
    *   校验最大分辨率和像素限制，防范 Image Bomb（解压炸弹）攻击。
    *   使用 `Image.LoadAsync()` 并调用 `.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Max }))`。
    *   将缩略图存盘为 `[filename]_thumb.[ext]`。
