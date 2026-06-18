// ファイル概要：バッチジョブの YAML 定義を読み込むサービスです。

using System.IO;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// バッチジョブ定義の読み込みサービス
/// </summary>
public interface IBatchJobLoader
{
    /// <summary>
    /// プロジェクトからバッチジョブ定義を読み込む
    /// </summary>
    Task<Dictionary<string, BatchJobDefinition>> LoadJobsAsync(string projectPath);

    /// <summary>
    /// ジョブの有効/無効状態を永続化する（override ファイルに書き込む）
    /// </summary>
    Task SetJobEnabledAsync(string projectPath, string jobId, bool enabled);
}

/// <summary>
/// バッチジョブ定義の読み込みサービス実装
/// </summary>
public class BatchJobLoader : IBatchJobLoader
{
    private readonly ILogger<BatchJobLoader> _logger;
    private readonly ISerializer _yamlSerializer;

    public BatchJobLoader(ILogger<BatchJobLoader> logger)
    {
        _logger = logger;
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    private static readonly string OverrideFileName = ".job-states.json";

    public async Task<Dictionary<string, BatchJobDefinition>> LoadJobsAsync(string projectPath)
    {
        var jobsDir = Path.Combine(projectPath, "jobs");
        var result = new Dictionary<string, BatchJobDefinition>();

        if (!Directory.Exists(jobsDir))
        {
            _logger.LogDebug("ジョブディレクトリが存在しません：{Path}", jobsDir);
            return result;
        }

        var yamlFiles = Directory.GetFiles(jobsDir, "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(jobsDir, "*.yaml", SearchOption.AllDirectories));

        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(file);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var jobContainer = deserializer.Deserialize<BatchJobContainer>(yaml);

                if (jobContainer.Jobs != null)
                {
                    foreach (var kvp in jobContainer.Jobs)
                    {
                        var job = kvp.Value;
                        job.Id = string.IsNullOrEmpty(job.Id) ? kvp.Key : job.Id;

                        if (!string.IsNullOrEmpty(job.Settings.SqlFile))
                            job.Settings.SqlFile = Path.Combine(projectPath, job.Settings.SqlFile);
                        if (!string.IsNullOrEmpty(job.Settings.OutputFile))
                            job.Settings.OutputFile = Path.Combine(projectPath, job.Settings.OutputFile);

                        result[job.Id] = job;
                        _logger.LogDebug("ジョブを読み込みました：{JobId}", job.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ジョブファイルの読み込みに失敗しました：{File}", file);
            }
        }

        // Override ファイルで enabled 状態を上書き
        var overrides = await LoadOverridesAsync(projectPath);
        foreach (var kvp in overrides)
        {
            if (result.TryGetValue(kvp.Key, out var job))
                job.Enabled = kvp.Value;
        }

        return result;
    }

    public async Task SetJobEnabledAsync(string projectPath, string jobId, bool enabled)
    {
        var overrides = await LoadOverridesAsync(projectPath);
        overrides[jobId] = enabled;
        await SaveOverridesAsync(projectPath, overrides);
    }

    private async Task<Dictionary<string, bool>> LoadOverridesAsync(string projectPath)
    {
        var overridePath = Path.Combine(projectPath, "jobs", OverrideFileName);
        if (!File.Exists(overridePath))
            return new Dictionary<string, bool>();
        try
        {
            var json = await File.ReadAllTextAsync(overridePath);
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, bool>();
        }
    }

    private async Task SaveOverridesAsync(string projectPath, Dictionary<string, bool> overrides)
    {
        var overridePath = Path.Combine(projectPath, "jobs", OverrideFileName);
        var json = JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(overridePath, json);
    }
}

/// <summary>
/// YAML ファイルのルートコンテナ
/// </summary>
public class BatchJobContainer
{
    /// <summary>
    /// ジョブ定義のディクショナリ
    /// </summary>
    public Dictionary<string, BatchJobDefinition>? Jobs { get; set; }
}
