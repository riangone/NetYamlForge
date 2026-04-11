using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// skills/ ディレクトリの Markdown ファイルからスキルとシステムプロンプトを読み込む。
/// ファイル形式: YAML frontmatter（---）+ プロンプト本文。
/// _system-prompt.md はシステムプロンプト専用（frontmatter なし）。
/// </summary>
public class SkillLoader
{
    private readonly string _skillsDir;
    private readonly ILogger<SkillLoader> _logger;

    // キャッシュ（起動後は不変）
    private List<AiSkill>? _skills;
    private string? _systemPrompt;

    public SkillLoader(IWebHostEnvironment env, ILogger<SkillLoader> logger)
    {
        _skillsDir = Path.Combine(env.ContentRootPath, "skills");
        _logger = logger;
    }

    /// <summary>スキル一覧を取得します（order 昇順）</summary>
    public IReadOnlyList<AiSkill> GetSkills()
    {
        if (_skills != null) return _skills;
        _skills = LoadSkills();
        return _skills;
    }

    /// <summary>システムプロンプトを取得します</summary>
    public string? GetSystemPrompt()
    {
        if (_systemPrompt != null) return _systemPrompt;
        _systemPrompt = LoadSystemPrompt();
        return _systemPrompt;
    }

    private List<AiSkill> LoadSkills()
    {
        var result = new List<AiSkill>();
        if (!Directory.Exists(_skillsDir)) return result;

        foreach (var file in Directory.GetFiles(_skillsDir, "*.md"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith("_")) continue; // _system-prompt.md など除外

            try
            {
                var skill = ParseSkillFile(file);
                if (skill != null) result.Add(skill);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "スキルファイルの解析に失敗: {File}", file);
            }
        }

        return result.OrderBy(s => s.Order).ThenBy(s => s.Name).ToList();
    }

    private string? LoadSystemPrompt()
    {
        var path = Path.Combine(_skillsDir, "_system-prompt.md");
        if (!File.Exists(path)) return null;

        var content = File.ReadAllText(path).Trim();
        // frontmatter がある場合は除去
        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("---", 3);
            if (end >= 0) content = content[(end + 3)..].Trim();
        }
        return content;
    }

    private static AiSkill? ParseSkillFile(string filePath)
    {
        var raw = File.ReadAllText(filePath);
        string frontmatter = string.Empty;
        string body = raw.Trim();

        // frontmatter 抽出
        if (raw.TrimStart().StartsWith("---"))
        {
            var start = raw.IndexOf("---");
            var end = raw.IndexOf("---", start + 3);
            if (end >= 0)
            {
                frontmatter = raw[(start + 3)..end].Trim();
                body = raw[(end + 3)..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(frontmatter)) return null;

        var skill = new AiSkill
        {
            Id = Path.GetFileNameWithoutExtension(filePath),
            Prompt = body
        };

        // 簡易 YAML パース（key: value 形式のみ対応）
        foreach (var line in frontmatter.Split('\n'))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim().ToLowerInvariant();
            var value = line[(colonIndex + 1)..].Trim().Trim('"');

            switch (key)
            {
                case "name":             skill.Name = value; break;
                case "icon":             skill.Icon = value; break;
                case "description":      skill.Description = value; break;
                case "needsinput":       skill.NeedsInput = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "inputplaceholder": skill.InputPlaceholder = value; break;
                case "order":            if (int.TryParse(value, out var o)) skill.Order = o; break;
            }
        }

        return string.IsNullOrWhiteSpace(skill.Name) ? null : skill;
    }
}
