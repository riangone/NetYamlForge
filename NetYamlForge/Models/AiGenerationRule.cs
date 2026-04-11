using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Models;

/// <summary>
/// AI 生成规则配置（从 YAML 读取）
/// 允许在 project.yaml 或 entities/*.yml 中声明 AI 生成规则
/// </summary>
public class AiGenerationRule
{
    /// <summary>
    /// 规则名称
    /// </summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 触发条件（例如：on-create, on-update, manual）
    /// </summary>
    [YamlMember(Alias = "trigger")]
    public string Trigger { get; set; } = "manual";

    /// <summary>
    /// AI Pipeline 配置
    /// </summary>
    [YamlMember(Alias = "pipeline")]
    public AiPipelineConfigRule Pipeline { get; set; } = new();

    /// <summary>
    /// 生成目标（文件路径或目录）
    /// </summary>
    [YamlMember(Alias = "target")]
    public AiGenerationTarget Target { get; set; } = new();

    /// <summary>
    /// 上下文配置（提供给 AI 的额外信息）
    /// </summary>
    [YamlMember(Alias = "context")]
    public AiGenerationContext Context { get; set; } = new();

    /// <summary>
    /// 后处理规则（生成后执行的操作）
    /// </summary>
    [YamlMember(Alias = "postProcess")]
    public List<AiPostProcessRule> PostProcess { get; set; } = new();
}

/// <summary>
/// AI Pipeline 配置规则
/// </summary>
public class AiPipelineConfigRule
{
    /// <summary>
    /// Pipeline 模式（full 或 single）
    /// </summary>
    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "full";

    /// <summary>
    /// 提示词模板（支持变量替换）
    /// </summary>
    [YamlMember(Alias = "promptTemplate")]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 指定的 AI Agent（可选）
    /// </summary>
    [YamlMember(Alias = "agent")]
    public string? Agent { get; set; }

    /// <summary>
    /// 超时秒数
    /// </summary>
    [YamlMember(Alias = "timeout")]
    public int? Timeout { get; set; }
}

/// <summary>
/// 生成目标配置
/// </summary>
public class AiGenerationTarget
{
    /// <summary>
    /// 目标目录（相对于项目根目录）
    /// </summary>
    [YamlMember(Alias = "directory")]
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// 文件命名模式（例如：{entity}.cs, {Entity}Service.cs）
    /// </summary>
    [YamlMember(Alias = "filePattern")]
    public string FilePattern { get; set; } = string.Empty;

    /// <summary>
    /// 是否覆盖已存在的文件
    /// </summary>
    [YamlMember(Alias = "overwrite")]
    public bool Overwrite { get; set; } = false;

    /// <summary>
    /// 文件类型过滤器（例如：*.cs, *.yaml）
    /// </summary>
    [YamlMember(Alias = "fileTypes")]
    public List<string> FileTypes { get; set; } = new();
}

/// <summary>
/// AI 生成上下文配置
/// </summary>
public class AiGenerationContext
{
    /// <summary>
    /// 包含的实体列表
    /// </summary>
    [YamlMember(Alias = "entities")]
    public List<string> Entities { get; set; } = new();

    /// <summary>
    /// 包含的页面列表
    /// </summary>
    [YamlMember(Alias = "pages")]
    public List<string> Pages { get; set; } = new();

    /// <summary>
    /// 额外说明
    /// </summary>
    [YamlMember(Alias = "additionalInfo")]
    public string AdditionalInfo { get; set; } = string.Empty;

    /// <summary>
    /// 是否包含项目结构信息
    /// </summary>
    [YamlMember(Alias = "includeProjectStructure")]
    public bool IncludeProjectStructure { get; set; } = true;

    /// <summary>
    /// 是否包含现有代码上下文
    /// </summary>
    [YamlMember(Alias = "includeExistingCode")]
    public bool IncludeExistingCode { get; set; } = false;
}

/// <summary>
/// 后处理规则
/// </summary>
public class AiPostProcessRule
{
    /// <summary>
    /// 操作类型（copy, move, transform, validate）
    /// </summary>
    [YamlMember(Alias = "action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 源路径
    /// </summary>
    [YamlMember(Alias = "source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 目标路径
    /// </summary>
    [YamlMember(Alias = "destination")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// 是否必需（失败时是否阻止后续流程）
    /// </summary>
    [YamlMember(Alias = "required")]
    public bool Required { get; set; } = true;
}

/// <summary>
/// AI 生成规则加载器
/// </summary>
public static class AiGenerationRuleLoader
{
    /// <summary>
    /// 从项目 YAML 加载 AI 生成规则
    /// </summary>
    public static List<AiGenerationRule> LoadFromProject(string projectYamlPath)
    {
        if (!File.Exists(projectYamlPath))
            return new List<AiGenerationRule>();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yamlContent = File.ReadAllText(projectYamlPath);
        var yamlDoc = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        if (yamlDoc.TryGetValue("aiGeneration", out var aiGenObj) && aiGenObj is Dictionary<object, object> aiGenDict)
        {
            if (aiGenDict.TryGetValue("rules", out var rulesObj) && rulesObj is List<object> rulesList)
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                return rulesList.Select(r =>
                {
                    var yaml = serializer.Serialize(r);
                    return deserializer.Deserialize<AiGenerationRule>(yaml);
                }).ToList();
            }
        }

        return new List<AiGenerationRule>();
    }

    /// <summary>
    /// 从实体 YAML 加载 AI 生成规则
    /// </summary>
    public static List<AiGenerationRule> LoadFromEntity(string entityYamlPath)
    {
        if (!File.Exists(entityYamlPath))
            return new List<AiGenerationRule>();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yamlContent = File.ReadAllText(entityYamlPath);
        var yamlDoc = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        if (yamlDoc.TryGetValue("aiGeneration", out var aiGenObj) && aiGenObj is Dictionary<object, object> aiGenDict)
        {
            if (aiGenDict.TryGetValue("rules", out var rulesObj) && rulesObj is List<object> rulesList)
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                return rulesList.Select(r =>
                {
                    var yaml = serializer.Serialize(r);
                    return deserializer.Deserialize<AiGenerationRule>(yaml);
                }).ToList();
            }
        }

        return new List<AiGenerationRule>();
    }
}
