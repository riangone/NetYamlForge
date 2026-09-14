// ファイル概要: DirectoryImportExecutor が source_path（フォーム入力由来のユーザー制御値）に対して
// PathSafetyGuard.ValidateAgainstAllowList を実際に経由し、管理者が許可していないディレクトリへの
// スキャン・読み取りを拒否することを、ExecuteAsync の実行フローを通して検証します。
// (docs/FRAMEWORK-SECURITY-REFACTOR-PLAN.md: DirectoryImportExecutor の source_path が
//  PathSafetyGuard を通っていないという既知の未対応項目の解消)

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests.Services.BatchJob;

public class DirectoryImportExecutorSecurityTests
{
    private static Mock<IWebHostEnvironment> CreateEnv(string contentRoot)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(contentRoot);
        return env;
    }

    private static IConfiguration BuildConfig(params string[] allowedRoots)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < allowedRoots.Length; i++)
        {
            dict[$"{DirectoryImportExecutor.AllowedRootsConfigKey}:{i}"] = allowedRoots[i];
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static SqliteConnection CreateDbWithPendingJob(string sourcePath)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute(@"
            CREATE TABLE import_jobs (
                job_id TEXT PRIMARY KEY,
                job_type TEXT,
                source_path TEXT,
                recursive INTEGER,
                file_filter TEXT,
                ai_provider TEXT,
                album_id TEXT,
                status TEXT,
                error_message TEXT,
                files_found INTEGER,
                files_imported INTEGER,
                files_skipped INTEGER,
                files_failed INTEGER,
                started_at TEXT,
                completed_at TEXT
            );
            CREATE TABLE photos (
                photo_id TEXT PRIMARY KEY,
                file_name TEXT, file_path TEXT, file_size INTEGER, file_format TEXT,
                taken_at TEXT, album_id TEXT, annotation_status TEXT,
                created_at TEXT, updated_at TEXT, deleted_at TEXT
            );
            CREATE TABLE processing_queue (
                photo_id TEXT, file_path TEXT, status TEXT, provider TEXT,
                priority INTEGER, retry_count INTEGER, queued_at TEXT
            );");

        connection.Execute(
            "INSERT INTO import_jobs (job_id, job_type, source_path, recursive, file_filter, status) " +
            "VALUES ('job-1', 'directory', @SourcePath, 1, '*.jpg', 'queued')",
            new { SourcePath = sourcePath });

        return connection;
    }

    private static BatchJobDefinition BuildJob() => new()
    {
        Id = "directory-import-test",
        Settings = new JobSettings { SqlQuery = "SELECT * FROM import_jobs WHERE status = 'queued' LIMIT 1" }
    };

    [Fact]
    public async Task ExecuteAsync_SourcePathOutsideAllowedRoots_RejectsAndMarksJobFailed()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_DirImport_" + Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(Path.GetTempPath(), "NYF_DirImport_AllowedRoot_" + Guid.NewGuid().ToString("N"));
        var attackerDir = Path.Combine(Path.GetTempPath(), "NYF_DirImport_Attacker_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(attackerDir);
        File.WriteAllText(Path.Combine(attackerDir, "secret.txt"), "should never be scanned");

        try
        {
            using var db = CreateDbWithPendingJob(attackerDir);
            var executor = new DirectoryImportExecutor(
                projectManager: null!,
                env: CreateEnv(contentRoot).Object,
                scheduler: Mock.Of<IBatchJobScheduler>(),
                configuration: BuildConfig(allowedRoot), // attackerDir はホワイトリストに含まれない
                logger: NullLogger<DirectoryImportExecutor>.Instance);

            var result = await executor.ExecuteAsync(BuildJob(), projectName: "", db, tx: null!, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Rejected source_path", result.ErrorMessage);

            var status = db.QueryFirst<string>("SELECT status FROM import_jobs WHERE job_id = 'job-1'");
            Assert.Equal("failed", status);

            // 攻撃者ディレクトリの中身が一切 photos テーブルに取り込まれていないことを確認
            var photoCount = db.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM photos");
            Assert.Equal(0, photoCount);
        }
        finally
        {
            Directory.Delete(contentRoot, true);
            Directory.Delete(allowedRoot, true);
            Directory.Delete(attackerDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoAllowedRootsConfigured_RejectsEveryPath_SecureDefault()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_DirImport_" + Guid.NewGuid().ToString("N"));
        var someDir = Path.Combine(Path.GetTempPath(), "NYF_DirImport_Some_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(someDir);

        try
        {
            using var db = CreateDbWithPendingJob(someDir);
            var executor = new DirectoryImportExecutor(
                projectManager: null!,
                env: CreateEnv(contentRoot).Object,
                scheduler: Mock.Of<IBatchJobScheduler>(),
                configuration: BuildConfig(), // 許可ルート未設定
                logger: NullLogger<DirectoryImportExecutor>.Instance);

            var result = await executor.ExecuteAsync(BuildJob(), projectName: "", db, tx: null!, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Rejected source_path", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(contentRoot, true);
            Directory.Delete(someDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SourcePathUnderAllowedRoot_ScansAndImportsSuccessfully()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "NYF_DirImport_" + Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(Path.GetTempPath(), "NYF_DirImport_AllowedOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(allowedRoot);
        File.WriteAllText(Path.Combine(allowedRoot, "photo1.jpg"), "fake-jpg-bytes");

        try
        {
            using var db = CreateDbWithPendingJob(allowedRoot);
            var executor = new DirectoryImportExecutor(
                projectManager: null!,
                env: CreateEnv(contentRoot).Object,
                scheduler: Mock.Of<IBatchJobScheduler>(),
                configuration: BuildConfig(allowedRoot),
                logger: NullLogger<DirectoryImportExecutor>.Instance);

            var job = BuildJob();
            job.Behavior.AutoEnqueueAnnotation = false; // スケジューラへの副作用を避ける

            var result = await executor.ExecuteAsync(job, projectName: "", db, tx: null!, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(1, result.RowsAffected);

            var status = db.QueryFirst<string>("SELECT status FROM import_jobs WHERE job_id = 'job-1'");
            Assert.Equal("done", status);
        }
        finally
        {
            Directory.Delete(contentRoot, true);
            Directory.Delete(allowedRoot, true);
        }
    }
}
