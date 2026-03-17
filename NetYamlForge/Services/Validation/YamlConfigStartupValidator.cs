// ファイル概要: 起動時にすべてのプロジェクトの YAML 設定を検証し、
// 未知の列型・フィルター型・未登録フック参照を構造化ログとして警告します。
// IHostedService として登録し、アプリ起動直後に一度だけ実行されます。

using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services.Validation;

/// <summary>
/// 起動時 YAML 設定バリデーター。
/// 全プロジェクトのエンティティ定義を走査し、未知の型・未登録フックを警告します。
/// </summary>
public sealed class YamlConfigStartupValidator : IHostedService
{
    // フォームフィールドで有効な列型
    public static readonly HashSet<string> KnownColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "long", "decimal", "double",
        "boolean", "bool", "datetime", "date",
        "text", "textarea", "email", "radio",
        "color", "range", "rating", "money",
        // ファイルアップロード系
        "file", "image",
        // リッチテキスト系
        "richtext", "markdown",
        // 自動完成・タグ系
        "autocomplete", "tags",
        // 数値・通貨系（拡張）
        "percent",
        // 連絡先系
        "tel", "url", "password",
        // 選択系（拡張）
        "checkbox-group", "switch-group",
        // 日付拡張
        "datetime-range",
        // コード編集
        "code", "json",
        // 特殊入力
        "signature", "map",
        // リスト操作
        "sortable-list"
    };

    // _FilterControl.cshtml で明示的に処理されるフィルター型
    public static readonly HashSet<string> KnownFilterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // 専用フィルターUI
        "like", "dropdown", "boolean", "enum",
        "date", "datetime", "date-range", "range",
        "checkbox", "multi-select", "multi-select-legacy",
        "entity-picker", "entity-multi-picker",
        "toggle-group", "bool-toggle",
        // 列型をそのままフィルター入力型として流用
        "string", "int", "long", "decimal", "double",
        "bool", "text", "textarea", "email",
        "radio", "color", "rating",
        // テンプレート用
        "eq"
    };

    private readonly ProjectManager _projectManager;
    private readonly IEntityHookRegistry _hookRegistry;
    private readonly ILogger<YamlConfigStartupValidator> _logger;

    public YamlConfigStartupValidator(
        ProjectManager projectManager,
        IEntityHookRegistry hookRegistry,
        ILogger<YamlConfigStartupValidator> logger)
    {
        _projectManager = projectManager;
        _hookRegistry = hookRegistry;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var projects = _projectManager.GetAll();
        int warnCount = 0;

        foreach (var project in projects)
        {
            var entities = project.EntityMetadata.GetAll();

            foreach (var (entityName, def) in entities)
            {
                // 列型チェック
                foreach (var (colName, col) in def.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(col.Type) &&
                        !KnownColumnTypes.Contains(col.Type))
                    {
                        _logger.LogWarning(
                            "yaml_config_warn event=unknown_column_type project={Project} entity={Entity} column={Column} type={Type}",
                            project.Name, entityName, colName, col.Type);
                        warnCount++;
                    }
                }

                // フィルター型チェック
                foreach (var (filterName, filter) in def.Filters)
                {
                    if (!string.IsNullOrWhiteSpace(filter.Type) &&
                        !KnownFilterTypes.Contains(filter.Type))
                    {
                        _logger.LogWarning(
                            "yaml_config_warn event=unknown_filter_type project={Project} entity={Entity} filter={Filter} type={Type}",
                            project.Name, entityName, filterName, filter.Type);
                        warnCount++;
                    }
                }

                // フック名チェック（フレームワーク登録フックのみ確認）
                if (def.Hooks != null)
                {
                    var hookSelectors = new System.Func<Models.EntityHooksDefinition, object?>[]
                    {
                        h => h.BeforeCreate, h => h.BeforeUpdate, h => h.BeforeDelete,
                        h => h.AfterCreate,  h => h.AfterUpdate,  h => h.AfterDelete
                    };
                    foreach (var selector in hookSelectors)
                    {
                        var hookList = def.Hooks.GetHookList(selector);
                        if (hookList == null) continue;
                        foreach (var hookName in hookList)
                        {
                            if (string.IsNullOrWhiteSpace(hookName) || hookName.StartsWith('@'))
                                continue;
                            var baseName = hookName.Split(':', 2)[0];
                            if (_hookRegistry.Find(baseName) == null)
                            {
                                // プロジェクト固有フックは実行時ロードのため Debug レベルに留める
                                _logger.LogDebug(
                                    "yaml_config_info event=hook_not_in_framework_registry project={Project} entity={Entity} hook={Hook} hint=プロジェクト固有フックか未登録",
                                    project.Name, entityName, hookName);
                            }
                        }
                    }
                }
            }
        }

        if (warnCount > 0)
            _logger.LogWarning(
                "yaml_config_summary 起動時設定検証: {WarnCount} 件の警告。詳細は上記ログを確認してください。",
                warnCount);
        else
            _logger.LogInformation("yaml_config_summary 起動時設定検証: 問題なし（全プロジェクト）");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
