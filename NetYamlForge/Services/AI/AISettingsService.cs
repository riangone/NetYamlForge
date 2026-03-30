using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI プロバイダーのユーザー設定を ai-user-settings.json に永続化するサービス。
/// appsettings.json のデフォルト値を上書きする。
/// </summary>
public class AISettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<AISettingsService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public AISettingsService(IWebHostEnvironment env, ILogger<AISettingsService> logger)
    {
        _settingsPath = Path.Combine(env.ContentRootPath, "ai-user-settings.json");
        _logger = logger;
    }

    /// <summary>デフォルトで使用する CLI ツール名を保存する。</summary>
    public void SetDefaultTool(string toolName)
    {
        var node = LoadNode();
        if (node["AICli"] is not JsonObject aicli)
        {
            aicli = new JsonObject();
            node["AICli"] = aicli;
        }
        aicli["DefaultTool"] = toolName;
        Save(node);
        _logger.LogInformation("AI default tool updated to: {Tool}", toolName);
    }

    /// <summary>AiWindow のプロバイダー優先順序を保存する。</summary>
    public void SetWindowProviderPriority(IReadOnlyList<string> priority)
    {
        var node = LoadNode();
        if (node["AiWindow"] is not JsonObject aiWindow)
        {
            aiWindow = new JsonObject();
            node["AiWindow"] = aiWindow;
        }
        var arr = new JsonArray();
        foreach (var p in priority) arr.Add(p);
        aiWindow["ProviderPriority"] = arr;
        Save(node);
        _logger.LogInformation("AI Window provider priority updated");
    }

    private JsonObject LoadNode()
    {
        if (!File.Exists(_settingsPath)) return new JsonObject();
        try
        {
            var text = File.ReadAllText(_settingsPath);
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read {Path}", _settingsPath);
            return new JsonObject();
        }
    }

    private void Save(JsonObject node)
    {
        File.WriteAllText(_settingsPath, node.ToJsonString(_jsonOptions));
    }
}
