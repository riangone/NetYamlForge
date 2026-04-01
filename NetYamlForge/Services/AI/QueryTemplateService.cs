using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models.AI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 查询模板服务
/// 加载和管理预定义的查询模板
/// </summary>
public class QueryTemplateService
{
    private readonly string _templatesDir;
    private readonly ILogger<QueryTemplateService> _logger;
    private readonly Dictionary<string, QueryTemplateDefinition> _templates = new();

    public QueryTemplateService(
        string templatesDir,
        ILogger<QueryTemplateService> logger)
    {
        _templatesDir = templatesDir;
        _logger = logger;
    }

    /// <summary>
    /// 加载所有查询模板
    /// </summary>
    public void LoadTemplates()
    {
        if (!Directory.Exists(_templatesDir))
        {
            _logger.LogWarning("查询模板目录不存在：{Dir}", _templatesDir);
            return;
        }

        var yamlFiles = Directory.GetFiles(_templatesDir, "*.yml", SearchOption.TopDirectoryOnly);
        
        foreach (var file in yamlFiles)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var template = ParseTemplate(yaml);
                if (template != null)
                {
                    _templates[template.Name] = template;
                    _logger.LogInformation("加载查询模板：{Name} ({File})", template.Name, Path.GetFileName(file));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载查询模板失败：{File}", file);
            }
        }

        _logger.LogInformation("共加载 {Count} 个查询模板", _templates.Count);
    }

    /// <summary>
    /// 获取查询模板
    /// </summary>
    public QueryTemplateDefinition? GetTemplate(string name)
    {
        return _templates.TryGetValue(name, out var template) ? template : null;
    }

    /// <summary>
    /// 获取所有模板名称
    /// </summary>
    public IEnumerable<string> GetTemplateNames()
    {
        return _templates.Keys.OrderBy(k => k);
    }

    /// <summary>
    /// 解析 YAML 模板
    /// </summary>
    private QueryTemplateDefinition? ParseTemplate(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<QueryTemplateDefinition>(yaml);
    }

    /// <summary>
    /// 将模板与参数合并生成查询参数
    /// </summary>
    public ParsedQueryParams MergeTemplateWithParams(string templateName, Dictionary<string, object?>? parameters = null)
    {
        var template = GetTemplate(templateName);
        if (template == null)
        {
            throw new ArgumentException($"查询模板不存在：{templateName}");
        }

        var queryParams = new ParsedQueryParams
        {
            Entity = template.Entity,
            Action = template.Action ?? "list",
            GroupBy = template.GroupBy ?? new List<string>(),
            OrderBy = template.OrderBy != null ? new OrderClause
            {
                Field = template.OrderBy.Field,
                Dir = template.OrderBy.Dir ?? "asc"
            } : null,
            Top = template.Top,
            Select = template.Select ?? new List<string>(),
            Filters = new List<FilterClause>(),
            Aggregations = new List<AggregationClause>()
        };

        // 处理过滤条件（合并模板和参数）
        if (template.Filters != null)
        {
            foreach (var filter in template.Filters)
            {
                var value = filter.Value;
                
                // 如果值是参数占位符（如 {status}），则替换为参数值
                if (parameters != null && value is string strValue && strValue.StartsWith("{") && strValue.EndsWith("}"))
                {
                    var paramName = strValue[1..^1];
                    if (parameters.TryGetValue(paramName, out var paramValue))
                    {
                        value = paramValue;
                    }
                }

                queryParams.Filters.Add(new FilterClause
                {
                    Field = filter.Field,
                    Op = filter.Op,
                    Value = value,
                    Value2 = filter.Value2,
                    Logic = filter.Logic ?? "AND"
                });
            }
        }

        // 处理聚合
        if (template.Aggregations != null)
        {
            foreach (var agg in template.Aggregations)
            {
                queryParams.Aggregations.Add(new AggregationClause
                {
                    Function = agg.Function,
                    Field = agg.Field,
                    Alias = agg.Alias
                });
            }
        }

        return queryParams;
    }
}

/// <summary>
/// 查询模板定义
/// </summary>
public class QueryTemplateDefinition
{
    /// <summary>
    /// 模板名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 模板描述
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 目标实体
    /// </summary>
    [JsonPropertyName("entity")]
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>
    /// 过滤条件
    /// </summary>
    [JsonPropertyName("filters")]
    public List<TemplateFilterClause>? Filters { get; set; }

    /// <summary>
    /// 分组字段
    /// </summary>
    [JsonPropertyName("groupBy")]
    public List<string>? GroupBy { get; set; }

    /// <summary>
    /// 聚合函数
    /// </summary>
    [JsonPropertyName("aggregations")]
    public List<TemplateAggregationClause>? Aggregations { get; set; }

    /// <summary>
    /// 排序条件
    /// </summary>
    [JsonPropertyName("orderBy")]
    public TemplateOrderClause? OrderBy { get; set; }

    /// <summary>
    /// 限制返回数量
    /// </summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }

    /// <summary>
    /// 选择的字段
    /// </summary>
    [JsonPropertyName("select")]
    public List<string>? Select { get; set; }

    /// <summary>
    /// 参数定义（用于验证）
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<TemplateParameter>? Parameters { get; set; }
}

/// <summary>
/// 模板过滤条件
/// </summary>
public class TemplateFilterClause
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("op")]
    public string Op { get; set; } = "eq";

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("value2")]
    public object? Value2 { get; set; }

    [JsonPropertyName("logic")]
    public string? Logic { get; set; } = "AND";
}

/// <summary>
/// 模板聚合条件
/// </summary>
public class TemplateAggregationClause
{
    [JsonPropertyName("function")]
    public string Function { get; set; } = "count";

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

/// <summary>
/// 模板排序条件
/// </summary>
public class TemplateOrderClause
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("dir")]
    public string? Dir { get; set; } = "asc";
}

/// <summary>
/// 模板参数定义
/// </summary>
public class TemplateParameter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public object? Default { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
