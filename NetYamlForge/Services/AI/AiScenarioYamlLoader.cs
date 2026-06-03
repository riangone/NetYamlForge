using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services.AI;

public interface IAiScenarioYamlLoader
{
    AiScenarioConfig GetConfig(string projectId);
    void Reload(string projectId);
}

public class AiScenarioYamlLoader : IAiScenarioYamlLoader
{
    private readonly ProjectManager _projectManager;
    private readonly ILogger<AiScenarioYamlLoader> _logger;
    private readonly ConcurrentDictionary<string, AiScenarioConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDeserializer _deserializer;

    public AiScenarioYamlLoader(ProjectManager projectManager, ILogger<AiScenarioYamlLoader> logger)
    {
        _projectManager = projectManager;
        _logger = logger;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance) // 蛇形命名
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public AiScenarioConfig GetConfig(string projectId)
    {
        return _configs.GetOrAdd(projectId, id => LoadConfig(id));
    }

    public void Reload(string projectId)
    {
        _logger.LogInformation("Reloading AI scenario configuration for project {ProjectId}", projectId);
        var newConfig = LoadConfig(projectId);
        _configs[projectId] = newConfig;
    }

    private AiScenarioConfig LoadConfig(string projectId)
    {
        if (_projectManager.TryGet(projectId, out var project) && project != null)
        {
            var yamlPath = Path.Combine(project.ProjectDir, "ai", "scenarios.yaml");
            if (File.Exists(yamlPath))
            {
                try
                {
                    var content = File.ReadAllText(yamlPath);
                    var config = _deserializer.Deserialize<AiScenarioConfig>(content);
                    if (config != null)
                    {
                        // 填充默认的 allowed_entities 和 allowed_actions，如果 YAML 里未配置
                        if (config.AllowedEntities == null || config.AllowedEntities.Count == 0)
                        {
                            config.AllowedEntities = GetDefaultAllowedEntities();
                        }
                        if (config.AllowedActions == null || config.AllowedActions.Count == 0)
                        {
                            config.AllowedActions = GetDefaultAllowedActions();
                        }
                        
                        // 补全 Scenario 的 Name 并做基本的映射修正
                        foreach (var kvp in config.Scenarios)
                        {
                            if (string.IsNullOrEmpty(kvp.Value.Name))
                            {
                                kvp.Value.Name = kvp.Key;
                            }
                        }

                        _logger.LogInformation("Loaded AI scenario config from {Path} for project {ProjectId}", yamlPath, projectId);
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load AI scenario config from {Path} for project {ProjectId}. Falling back to default config.", yamlPath, projectId);
                }
            }
        }

        _logger.LogWarning("No AI scenario config file found for project {ProjectId}. Using hardcoded default configuration.", projectId);
        return GetDefaultConfig();
    }

    private List<string> GetDefaultAllowedEntities()
    {
        return new List<string>
        {
            "vehicles",
            "sales_leads",
            "customers",
            "service_appointments",
            "test_drives"
        };
    }

    private List<string> GetDefaultAllowedActions()
    {
        return new List<string> { "list", "count", "get", "create", "update" };
    }

    private AiScenarioConfig GetDefaultConfig()
    {
        var config = new AiScenarioConfig
        {
            AllowedEntities = GetDefaultAllowedEntities(),
            AllowedActions = GetDefaultAllowedActions()
        };

        // 默认场景 1: test_drive
        config.Scenarios["test_drive"] = new ScenarioConfig
        {
            Name = "test_drive",
            Description = "試乗予約",
            InitialState = "Init",
            RequiredSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種の試乗をご希望ですか？", IsRequired = true },
                new() { Name = "preferred_date", Prompt = "ご希望の日付を教えてください（例：明日、来週月曜日）", IsRequired = true },
                new() { Name = "preferred_time", Prompt = "ご希望の時間帯を教えてください（例：午前 10 時、午後 2 時）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotConfig>
            {
                new() { Name = "current_vehicle", Prompt = "現在お乗りの車はありますか？（任意）", IsRequired = false },
                new() { Name = "license_status", Prompt = "運転免許証はお持ちですか？（任意）", IsRequired = false }
            },
            Tools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Init"] = new() { "query_data" },
                ["CollectVehicle"] = new() { "query_data" },
                ["Confirming"] = new() { "create_appointment_request" }
            }
        };

        // 默认场景 2: estimate
        config.Scenarios["estimate"] = new ScenarioConfig
        {
            Name = "estimate",
            Description = "見積もり依頼",
            InitialState = "Init",
            RequiredSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種の御見積もりをご希望ですか？", IsRequired = true },
                new() { Name = "grade", Prompt = "グレードは決まっていますか？（例：G、X、Z など）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotConfig>
            {
                new() { Name = "budget_amount", Prompt = "ご予算はありますか？（例：300 万円以内）", IsRequired = false },
                new() { Name = "payment_method", Prompt = "お支払い方法はいかがなさいますか？（ローン/現金）", IsRequired = false, AllowedValues = new() { "ローン", "現金", "リース" } },
                new() { Name = "trade_in", Prompt = "下取り車両はございますか？", IsRequired = false, AllowedValues = new() { "はい", "いいえ" } },
                new() { Name = "options", Prompt = "ご希望のオプションはありますか？（例：ナビ、ETC、ドラレコ）", IsRequired = false }
            },
            Tools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Init"] = new() { "query_data" },
                ["CollectVehicle"] = new() { "query_data" },
                ["Confirming"] = new() { "create_appointment_request" }
            }
        };

        // 默认场景 3: appointment_service
        config.Scenarios["appointment_service"] = new ScenarioConfig
        {
            Name = "appointment_service",
            Description = "サービス予約（車検・整備）",
            InitialState = "Init",
            RequiredSlots = new List<SlotConfig>
            {
                new() { Name = "service_type", Prompt = "どのようなご用件でしょうか？（例：車検、点検、オイル交換）", IsRequired = true },
                new() { Name = "vehicle_model", Prompt = "お車の車種を教えてください", IsRequired = true },
                new() { Name = "preferred_date", Prompt = "ご希望の日付を教えてください", IsRequired = true },
                new() { Name = "preferred_time", Prompt = "ご希望の時間帯を教えてください", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotConfig>
            {
                new() { Name = "mileage", Prompt = "現在の走行距離を教えてください（任意）", IsRequired = false },
                new() { Name = "concerns", Prompt = "気になる点はありますか？（例：異音、振動）", IsRequired = false }
            },
            Tools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Init"] = new() { "query_data" },
                ["CollectVehicle"] = new() { "query_data" },
                ["Confirming"] = new() { "create_appointment_request" }
            }
        };

        // 默认场景 4: trade_in
        config.Scenarios["trade_in"] = new ScenarioConfig
        {
            Name = "trade_in",
            Description = "下取り査定",
            InitialState = "Init",
            RequiredSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_brand", Prompt = "お車のメーカーを教えてください（例：トヨタ、ホンダ）", IsRequired = true },
                new() { Name = "vehicle_model", Prompt = "車種を教えてください", IsRequired = true },
                new() { Name = "vehicle_year", Prompt = "初度登録年を教えてください（例：2018 年）", IsRequired = true },
                new() { Name = "mileage", Prompt = "走行距離を教えてください（例：5 万 km）", IsRequired = true },
                new() { Name = "customer_name", Prompt = "お名前を教えてください", IsRequired = true },
                new() { Name = "customer_phone", Prompt = "ご連絡先電話番号を教えてください", IsRequired = true }
            },
            OptionalSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_condition", Prompt = "お車の状態はいかがですか？（事故歴など）", IsRequired = false },
                new() { Name = "preferred_date", Prompt = "査定ご希望の日付はありますか？", IsRequired = false }
            },
            Tools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Init"] = new() { "query_data" },
                ["CollectVehicle"] = new() { "query_data" },
                ["Confirming"] = new() { "create_appointment_request" }
            }
        };

        // 默认场景 5: vehicle_inquiry
        config.Scenarios["vehicle_inquiry"] = new ScenarioConfig
        {
            Name = "vehicle_inquiry",
            Description = "車両お問い合わせ",
            InitialState = "Init",
            RequiredSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_model", Prompt = "どの車種についてお調べしますか？", IsRequired = true }
            },
            OptionalSlots = new List<SlotConfig>
            {
                new() { Name = "vehicle_type", Prompt = "ご希望の車体タイプはありますか？（例：SUV、セダン）", IsRequired = false },
                new() { Name = "budget_amount", Prompt = "ご予算はありますか？", IsRequired = false },
                new() { Name = "vehicle_color", Prompt = "ご希望の色はありますか？", IsRequired = false }
            },
            Tools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Init"] = new() { "query_data" },
                ["CollectVehicle"] = new() { "query_data" },
                ["Confirming"] = new() { "create_appointment_request" }
            }
        };

        return config;
    }
}
