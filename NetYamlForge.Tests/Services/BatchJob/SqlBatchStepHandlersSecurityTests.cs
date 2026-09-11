// ファイル概要: SqlToCsvHandler / SqlCommandHandler が PathSafetyGuard を実際に経由して
// パス・トラバーサル攻撃を拒否することを、ハンドラーの ExecuteAsync 実行フローを通して検証します。
// (docs/FRAMEWORK-SECURITY-REFACTOR-PLAN.md 漏洞1 の実装レベル検証)

using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests.Services.BatchJob;

public class SqlBatchStepHandlersSecurityTests
{
    private static Mock<IWebHostEnvironment> CreateEnv(string contentRoot)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(contentRoot);
        return env;
    }

    // projectName を渡さない（= システムレベル Job）場合、_projectManager.TryGet は一切呼ばれない
    // (projectName が null/空の時点で短絡評価される) ため、ProjectManager は null のままで安全に検証できる。
    private static ProjectManager NullProjectManager => null!;

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../outside.sql")]
    public async Task SqlCommandHandler_SqlFilePathTraversal_ThrowsUnauthorizedAccess(string maliciousSqlFile)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_SqlCmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new SqlCommandHandler(NullProjectManager, CreateEnv(contentRoot).Object,
                NullLogger<SqlCommandHandler>.Instance);

            var job = new BatchJobDefinition
            {
                Id = "malicious-job",
                Settings = new JobSettings { SqlFile = maliciousSqlFile }
            };

            // パス検証は DB アクセスより前に行われるため、db/tx は使用されない
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.ExecuteAsync(job, projectName: null,
                    db: null!, tx: null!, result: new BatchJobResult(), ct: CancellationToken.None));
        }
        finally
        {
            Directory.Delete(contentRoot, true);
        }
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../../../outside.csv")]
    public async Task SqlToCsvHandler_OutputFilePathTraversal_ThrowsUnauthorizedAccess(string maliciousOutputFile)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_SqlCsv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new SqlToCsvHandler(NullProjectManager, CreateEnv(contentRoot).Object,
                NullLogger<SqlToCsvHandler>.Instance);

            var job = new BatchJobDefinition
            {
                Id = "malicious-job",
                // SqlQuery を直接指定することで GetSqlAsync が SqlFile 経路を通らず、
                // OutputFile のパス検証（db.ExecuteReader 呼び出しより前）だけが問われるようにする
                Settings = new JobSettings { SqlQuery = "SELECT 1", OutputFile = maliciousOutputFile }
            };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.ExecuteAsync(job, projectName: null,
                    db: null!, tx: null!, result: new BatchJobResult(), ct: CancellationToken.None));
        }
        finally
        {
            Directory.Delete(contentRoot, true);
        }
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    public async Task SqlToCsvHandler_SqlFilePathTraversal_ThrowsUnauthorizedAccess(string maliciousSqlFile)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_SqlCsv2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new SqlToCsvHandler(NullProjectManager, CreateEnv(contentRoot).Object,
                NullLogger<SqlToCsvHandler>.Instance);

            var job = new BatchJobDefinition
            {
                Id = "malicious-job",
                Settings = new JobSettings { SqlFile = maliciousSqlFile }
            };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.ExecuteAsync(job, projectName: null,
                    db: null!, tx: null!, result: new BatchJobResult(), ct: CancellationToken.None));
        }
        finally
        {
            Directory.Delete(contentRoot, true);
        }
    }

    [Fact]
    public async Task SqlToCsvHandler_ValidOutputFileUnderSystemJobBaseDir_Succeeds()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_SqlCsvOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new SqlToCsvHandler(NullProjectManager, CreateEnv(contentRoot).Object,
                NullLogger<SqlToCsvHandler>.Instance);

            var job = new BatchJobDefinition
            {
                Id = "legit-job",
                Settings = new JobSettings { SqlQuery = "SELECT 1 AS Value", OutputFile = "reports/out.csv" }
            };

            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var result = new BatchJobResult();
            // 実際に SQLite 接続で最後まで実行できることを確認する（＝パス検証を正しく通過したこと自体の確認）。
            await handler.ExecuteAsync(job, projectName: null,
                db: connection, tx: null!, result: result, ct: CancellationToken.None);

            Assert.Equal(1, result.RowsAffected);
            Assert.True(File.Exists(result.OutputFile));
            Assert.StartsWith(contentRoot, result.OutputFile, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(contentRoot, true);
        }
    }
}
