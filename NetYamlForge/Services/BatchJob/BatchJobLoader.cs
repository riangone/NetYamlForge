// ファイル概要：バッチジョブの YAML 定義を読み込むサービスです。

using System.IO;
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
                        job.Id = string.IsNullOrEmpty(job.Id) ? Path.GetFileNameWithoutExtension(file) : job.Id;
                        
                        // 相対パスを絶対パスに変換
                        if (!string.IsNullOrEmpty(job.Settings.SqlFile))
                        {
                            job.Settings.SqlFile = Path.Combine(projectPath, job.Settings.SqlFile);
                        }
                        if (!string.IsNullOrEmpty(job.Settings.OutputFile))
                        {
                            job.Settings.OutputFile = Path.Combine(projectPath, job.Settings.OutputFile);
                        }

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

        return result;
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
