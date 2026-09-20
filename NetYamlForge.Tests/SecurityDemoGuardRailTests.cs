// ファイル概要: security-demo プロジェクトの README.md が主張する 3 つのガードレールが
// 実際に危険な入力を拒否することを検証する回帰テストです。
//
// 「AI が生成しがちな危険な YAML/SQL/パス操作パターンをフレームワークが拒否する」という
// この框架 (NetYamlForge) の核心的な価値提案を、実行可能なコードで証明します。
//
// 参照している「悪い例」のテキストは
// NetYamlForge/projects/security-demo/docs/unsafe-examples/ に人間が読める形で
// 保存されている（.yml.txt / .cs.txt 拡張子にして、アプリやビルドに一切取り込まれない
// ようにしてある）。このテストはそれと同じ内容を文字列として直接検証する。

using System.IO;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class SecurityDemoGuardRailTests
{
    // ─────────────────────────────────────────────────────────────────
    // ガードレール 1: エンティティメタデータ検証
    // (DynamicCrudMetadataValidator / SqlSafetyGuard)
    // ─────────────────────────────────────────────────────────────────

    private static EntityDefinition ParseAndNormalize(string yaml, string entityName)
    {
        var parser = new EntityMetadataParser();
        var root = parser.Parse(yaml, "unsafe-note-entity.yml.txt");
        var def = root.Entities[entityName];
        parser.Normalize(def);
        return def;
    }

    [Fact]
    public void UnsafeJoinType_OutsideAllowlist_IsRejectedAtMetadataValidation()
    {
        // AI が "full outer join" のつもりで書きがちな join type。
        // 許可されているのは left / inner / right のみ。
        const string yaml = """
            imports: []
            entities:
              note:
                table: Note
                key: Id
                displayName: Note
                api: enabled
                joins:
                  - type: full
                    table: Attachment
                    alias: a
                    on: Note.Id = a.NoteId
                columns:
                  Id:
                    type: int
                    identity: true
                    label: ID
            """;

        var def = ParseAndNormalize(yaml, "note");

        var ex = Assert.Throws<InvalidOperationException>(
            () => DynamicCrudMetadataValidator.ValidateMetadata(def, "note"));

        Assert.Contains("joinType", ex.Message);
    }

    [Fact]
    public void RawSqlSmuggledIntoFilterExpression_IsRejectedAtMetadataValidation()
    {
        // AI が「管理者向けに任意の SQL 条件でフィルタさせたい」という要求を、
        // YAML の filter.expression にそのまま書いてしまった例。
        // ';' '--' '/*' '*/' などの危険トークンや許可文字集合外の文字を含む式は
        // SqlSafetyGuard.EnsureExpression が拒否する。
        const string yaml = """
            imports: []
            entities:
              note:
                table: Note
                key: Id
                displayName: Note
                api: enabled
                columns:
                  Id:
                    type: int
                    identity: true
                    label: ID
                filters:
                  AdminRawFilter:
                    type: like
                    label: "Raw filter (unsafe)"
                    expression: "1=1; DROP TABLE Note; --"
            """;

        var def = ParseAndNormalize(yaml, "note");

        Assert.Throws<InvalidOperationException>(
            () => DynamicCrudMetadataValidator.ValidateMetadata(def, "note"));
    }

    [Fact]
    public void SafeNoteEntity_PassesMetadataValidation()
    {
        // 対照実験: security-demo が実際に出荷している entities/note.yml と同じ形の
        // 安全な定義は、当然ながら検証を通過する。
        const string yaml = """
            imports: []
            entities:
              note:
                table: Note
                key: Id
                displayName: Note
                api: enabled
                columns:
                  Id:
                    type: int
                    identity: true
                    label: ID
                  Title:
                    type: string
                    label: Title
                filters:
                  Status:
                    type: like
                    label: Status
            """;

        var def = ParseAndNormalize(yaml, "note");

        var ex = Record.Exception(() => DynamicCrudMetadataValidator.ValidateMetadata(def, "note"));
        Assert.Null(ex);
    }

    // ─────────────────────────────────────────────────────────────────
    // ガードレール 2: パストラバーサル防止 (PathSafetyGuard)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("../../other-tenant/database/other-tenant.db")]
    public void PathTraversalPayload_IsRejectedByPathSafetyGuard(string maliciousRelativePath)
    {
        // docs/unsafe-examples/path-traversal-hook.cs.txt が示す「悪い実装」
        // （素朴な Path.Combine(baseDir, userInput)）なら、この入力でベース
        // ディレクトリ外のファイルに到達できてしまう。
        // 実際に本プロジェクトが使う PathSafetyGuard はこれを拒否する。
        var baseDir = Path.Combine(Path.GetTempPath(), "nyf-security-demo-guard-test", "tenant-base");
        Directory.CreateDirectory(baseDir);

        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath(maliciousRelativePath, baseDir, "security-demo.attachment.download"));
    }

    [Fact]
    public void AbsolutePathOutsideBaseDirectory_IsRejectedByPathSafetyGuard()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "nyf-security-demo-guard-test", "tenant-base-2");
        Directory.CreateDirectory(baseDir);

        // Path.Combine(baseDir, "/etc/passwd") ignores baseDir entirely on
        // most platforms and returns "/etc/passwd" outright — exactly the
        // failure mode the unsafe example calls out.
        Assert.Throws<UnauthorizedAccessException>(() =>
            PathSafetyGuard.NormalizeAndValidatePath("/etc/passwd", baseDir, "security-demo.attachment.download"));
    }

    [Fact]
    public void PathWithinBaseDirectory_IsAcceptedByPathSafetyGuard()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "nyf-security-demo-guard-test", "tenant-base-3");
        Directory.CreateDirectory(baseDir);

        var resolved = PathSafetyGuard.NormalizeAndValidatePath(
            "notes/1/guardrails-overview.pdf", baseDir, "security-demo.attachment.download");

        Assert.StartsWith(Path.GetFullPath(baseDir), resolved);
    }

    // ─────────────────────────────────────────────────────────────────
    // ガードレール 3: SecurityDemoController (インタラクティブ靶場) テスト
    // ─────────────────────────────────────────────────────────────────

    private static NetYamlForge.Projects.SecurityDemo.Controllers.SecurityDemoController CreateSecurityDemoController()
    {
        var mockAuth = new Moq.Mock<NetYamlForge.Services.Auth.IUserAuthService>();
        var mockEnv = new Moq.Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var mockMetadata = new Moq.Mock<IEntityMetadataProvider>();
        var noteDef = new EntityDefinition { Api = "enabled" };
        var attachmentDef = new EntityDefinition { Api = "disabled" };
        mockMetadata.Setup(m => m.GetAll()).Returns(new Dictionary<string, EntityDefinition>
        {
            ["note"] = noteDef,
            ["attachment"] = attachmentDef
        });

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityDemoController>();
        var projectScope = new ProjectScope();

        return new NetYamlForge.Projects.SecurityDemo.Controllers.SecurityDemoController(
            mockAuth.Object, mockEnv.Object, projectScope, mockMetadata.Object, logger);
    }

    [Fact]
    public void SecurityDemoController_ValidateYaml_WithUnsafeJoin_ReturnsBlocked()
    {
        var controller = CreateSecurityDemoController();
        var request = new NetYamlForge.Projects.SecurityDemo.Controllers.YamlValidateRequest
        {
            YamlContent = """
                imports: []
                entities:
                  note:
                    table: Note
                    key: Id
                    displayName: Note
                    api: enabled
                    joins:
                      - type: full
                        table: Attachment
                        alias: a
                        on: Note.Id = a.NoteId
                    columns:
                      Id:
                        type: int
                        identity: true
                        label: ID
                """
        };

        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.ValidateYaml(request));
        var result = Assert.IsType<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityCheckResult>(actionResult.Value);

        Assert.True(result.Blocked);
        Assert.False(result.Success);
        Assert.Equal("UnsafeJoinType", result.ViolationType);
    }

    [Fact]
    public void SecurityDemoController_ValidateYaml_WithSafeYaml_ReturnsAllowed()
    {
        var controller = CreateSecurityDemoController();
        var request = new NetYamlForge.Projects.SecurityDemo.Controllers.YamlValidateRequest
        {
            YamlContent = """
                imports: []
                entities:
                  note:
                    table: Note
                    key: Id
                    displayName: Note
                    api: enabled
                    columns:
                      Id:
                        type: int
                        identity: true
                        label: ID
                      Title:
                        type: string
                        label: Title
                """
        };

        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.ValidateYaml(request));
        var result = Assert.IsType<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityCheckResult>(actionResult.Value);

        Assert.False(result.Blocked);
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("/etc/shadow")]
    public void SecurityDemoController_ValidatePath_WithTraversal_ReturnsBlocked(string payload)
    {
        var controller = CreateSecurityDemoController();
        var request = new NetYamlForge.Projects.SecurityDemo.Controllers.PathValidateRequest
        {
            TargetPath = payload
        };

        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.ValidatePath(request));
        var result = Assert.IsType<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityCheckResult>(actionResult.Value);

        Assert.True(result.Blocked);
        Assert.False(result.Success);
        Assert.Equal("PathTraversalEscape", result.ViolationType);
    }

    [Fact]
    public void SecurityDemoController_CheckApiAccess_DisabledAttachment_ReturnsBlocked()
    {
        var controller = CreateSecurityDemoController();
        var request = new NetYamlForge.Projects.SecurityDemo.Controllers.ApiCheckRequest
        {
            EntityName = "attachment"
        };

        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.CheckApiAccess(request));
        var result = Assert.IsType<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityCheckResult>(actionResult.Value);

        Assert.True(result.Blocked);
        Assert.Equal("ApiDisabledByDefault", result.ViolationType);
    }

    [Fact]
    public void SecurityDemoController_CheckApiAccess_EnabledNote_ReturnsAllowed()
    {
        var controller = CreateSecurityDemoController();
        var request = new NetYamlForge.Projects.SecurityDemo.Controllers.ApiCheckRequest
        {
            EntityName = "note"
        };

        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.CheckApiAccess(request));
        var result = Assert.IsType<NetYamlForge.Projects.SecurityDemo.Controllers.SecurityCheckResult>(actionResult.Value);

        Assert.False(result.Blocked);
        Assert.True(result.Success);
    }

    [Fact]
    public void SecurityDemoController_GetPresets_ReturnsValidPayloads()
    {
        var controller = CreateSecurityDemoController();
        var actionResult = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(controller.GetPresets());
        Assert.NotNull(actionResult.Value);
    }
}

