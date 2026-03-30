using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.Providers;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 自然语言查询解析服务
/// 将用户的自然语言转换为结构化的查询参数
/// </summary>
public class QueryParserService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IEntityMetadataProvider _entityMetadata;
    private readonly ILogger<QueryParserService> _logger;
    private readonly CliConfig _config;

    public QueryParserService(
        ILlmProvider llmProvider,
        IEntityMetadataProvider entityMetadata,
        ILogger<QueryParserService> logger,
        IOptions<CliConfig> config)
    {
        _llmProvider = llmProvider;
        _entityMetadata = entityMetadata;
        _logger = logger;
        _config = config.Value;
    }

    /// <summary>
    /// 解析自然语言查询
    /// </summary>
    public async Task<ParsedQueryParams> ParseAsync(string naturalLanguage, string? project = null, string? specifiedEntity = null)
    {
        _logger.LogInformation("解析自然语言查询：{Query}", naturalLanguage);

        // 构建 Prompt
        var prompt = BuildPrompt(naturalLanguage, specifiedEntity);

        // 调用 LLM 解析
        var response = await _llmProvider.CompleteAsync(prompt, CancellationToken.None);

        // 解析 JSON 响应
        var parsedQuery = ParseResponse(response, specifiedEntity);

        // 验证和修正字段名
        ValidateAndFixFields(parsedQuery, project);

        _logger.LogInformation("解析完成：实体={Entity}, 过滤器={FiltersCount}, 排序={OrderBy}", 
            parsedQuery.Entity, parsedQuery.Filters.Count, parsedQuery.OrderBy?.Field);

        return parsedQuery;
    }

    /// <summary>
    /// 构建 Prompt
    /// </summary>
    private string BuildPrompt(string naturalLanguage, string? specifiedEntity)
    {
        var entityInfo = specifiedEntity != null 
            ? GetEntitySchemaInfo(specifiedEntity) 
            : "AI 将根据查询内容自动推断实体";

        return $@"你是一个专业的 SQL 查询生成助手。请将用户的自然语言查询转换为结构化的 JSON 查询参数。

## 可用实体信息
{entityInfo}

## 输出格式
必须严格遵循以下 JSON Schema：
```json
{{
  ""entity"": ""实体名称（小写复数形式）"",
  ""action"": ""list | count | aggregate"",
  ""filters"": [
    {{
      ""field"": ""字段名"",
      ""op"": ""eq | ne | gt | lt | gte | lte | like | in | between | is_null"",
      ""value"": 值，
      ""value2"": 值 2（仅 between 需要）,
      ""logic"": ""AND | OR""
    }}
  ],
  ""orderBy"": {{
    ""field"": ""字段名"",
    ""dir"": ""asc | desc""
  }},
  ""top"": 返回数量限制（整数）,
  ""select"": [""字段 1"", ""字段 2""],
  ""groupBy"": [""分组字段""],
  ""aggregations"": [
    {{
      ""function"": ""sum | avg | count | min | max"",
      ""field"": ""字段名"",
      ""alias"": ""别名""
    }}
  ]
}}
```

## 日期范围快捷语法
支持以下相对日期表达：
- today, yesterday
- this_week, last_week
- this_month, last_month
- this_quarter, last_quarter
- this_year, last_year

当用户使用这些表达时，在 filter 的 value 中使用相同的字符串（如 ""last_month""），系统会自动转换。

## 示例

用户输入：""显示上个月销售额前 10 的产品""
输出：
```json
{{
  ""entity"": ""products"",
  ""action"": ""list"",
  ""filters"": [
    {{ ""field"": ""sales_date"", ""op"": ""between"", ""value"": ""last_month"" }}
  ],
  ""orderBy"": {{ ""field"": ""sales_amount"", ""dir"": ""desc"" }},
  ""top"": 10,
  ""select"": [""product_name"", ""category"", ""sales_amount""]
}}
```

用户输入：""找出库存低于 5 的商品""
输出：
```json
{{
  ""entity"": ""products"",
  ""action"": ""list"",
  ""filters"": [
    {{ ""field"": ""stock_quantity"", ""op"": ""lt"", ""value"": 5 }}
  ],
  ""select"": [""product_name"", ""stock_quantity""]
}}
```

用户输入：""统计每个类别的产品数量和平均价格""
输出：
```json
{{
  ""entity"": ""products"",
  ""action"": ""aggregate"",
  ""groupBy"": [""category""],
  ""aggregations"": [
    {{ ""function"": ""count"", ""field"": ""id"", ""alias"": ""product_count"" }},
    {{ ""function"": ""avg"", ""field"": ""price"", ""alias"": ""avg_price"" }}
  ]
}}
```

## 当前查询
用户输入：{naturalLanguage}

请只输出 JSON 内容，不要包含 ```json 标记或其他解释。";
    }

    /// <summary>
    /// 获取实体 Schema 信息
    /// </summary>
    private string GetEntitySchemaInfo(string entityName)
    {
        try
        {
            var meta = _entityMetadata.Get(entityName);
            var sb = new StringBuilder();
            sb.AppendLine($"实体：{meta.DisplayName} ({entityName})");
            sb.AppendLine("可用字段:");
            
            foreach (var col in meta.Columns)
            {
                if (!col.Value.Hidden)
                {
                    sb.AppendLine($"  - {col.Key} ({col.Value.Type}, {(col.Value.Required ? "必填" : "可选")})");
                }
            }

            return sb.ToString();
        }
        catch
        {
            return $"实体 '{entityName}' 未找到，AI 将尝试推断";
        }
    }

    /// <summary>
    /// 解析 LLM 响应
    /// </summary>
    private ParsedQueryParams ParseResponse(string response, string? specifiedEntity)
    {
        // 清理响应（移除 markdown 标记）
        var json = response.Trim();
        if (json.StartsWith("```json"))
        {
            json = json["```json".Length..];
        }
        if (json.StartsWith("```"))
        {
            json = json["```".Length..];
        }
        if (json.EndsWith("```"))
        {
            json = json[..^3];
        }
        json = json.Trim();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsed = JsonSerializer.Deserialize<ParsedQueryParams>(json, options) 
                ?? new ParsedQueryParams();

            // 如果指定了实体，使用指定的实体
            if (!string.IsNullOrEmpty(specifiedEntity))
            {
                parsed.Entity = specifiedEntity;
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON 解析失败：{Json}", json);
            throw new InvalidOperationException($"无法解析 AI 返回的 JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 验证和修正字段名
    /// </summary>
    private void ValidateAndFixFields(ParsedQueryParams query, string? project)
    {
        if (string.IsNullOrEmpty(query.Entity))
        {
            throw new InvalidOperationException("AI 未能推断出实体名称");
        }

        try
        {
            var meta = _entityMetadata.Get(query.Entity);
            var validFields = meta.Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 验证并修正 select 字段
            if (query.Select.Count > 0)
            {
                query.Select = query.Select
                    .Where(f => validFields.Contains(f))
                    .ToList();
            }
            else
            {
                // 默认选择可见字段
                query.Select = meta.Columns
                    .Where(kv => !kv.Value.Hidden)
                    .Select(kv => kv.Key)
                    .ToList();
            }

            // 验证 orderBy 字段
            if (query.OrderBy != null && !validFields.Contains(query.OrderBy.Field))
            {
                _logger.LogWarning("无效的排序字段：{Field}", query.OrderBy.Field);
                query.OrderBy = null;
            }

            // 验证 groupBy 字段
            if (query.GroupBy.Count > 0)
            {
                query.GroupBy = query.GroupBy
                    .Where(f => validFields.Contains(f))
                    .ToList();
            }

            // 验证 aggregations 字段
            foreach (var agg in query.Aggregations)
            {
                if (!validFields.Contains(agg.Field))
                {
                    _logger.LogWarning("无效的聚合字段：{Field}", agg.Field);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证字段时出错");
            // 继续执行，让后续服务处理错误
        }
    }
}

/// <summary>
/// LLM 选项
/// </summary>
public class LlmOptions
{
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 1000;
}
