// AI 独立プロジェクト用 コンテキスト実装

using System.Data;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 独立 AI サービス用 プロジェクト コンテキスト実装
/// 設定ファイルまたは環境変数からプロジェクト情報を取得
/// </summary>
public class StandaloneAIProjectContext : IAIProjectContext
{
    private readonly IConfiguration _configuration;
    private readonly IAIDbConnectionFactory _dbFactory;
    private readonly string? _projectName;

    public StandaloneAIProjectContext(IConfiguration configuration, IAIDbConnectionFactory dbFactory)
    {
        _configuration = configuration;
        _dbFactory = dbFactory;
        _projectName = _configuration["AI:ProjectName"]
            ?? Environment.GetEnvironmentVariable("NYFORGE_PROJECT")
            ?? "default";
    }

    public string ProjectName => _projectName ?? "default";

    public bool IsSet => !string.IsNullOrEmpty(_projectName);

    public string GetDatabasePath()
    {
        return _configuration.GetConnectionString("DefaultConnection")
            ?? _configuration["Database:ConnectionString"]
            ?? "Data Source=ai-chat.db";
    }

    public IDbConnection CreateConnection()
    {
        return _dbFactory.CreateConnection(ProjectName);
    }
}
