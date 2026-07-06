// ファイル概要: 起動時にすべてのプロジェクトの YAML 設定を検証し、
// 未知の列型・フィルター型・未登録フック参照を構造化ログとして警告します。
// IHostedService として登録し、アプリ起動直後に一度だけ実行されます。

using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services.Validation;

/// <summary>
/// 起動時 YAML 設定バリデーター。
/// 全プロジェクトのエンティティ定義を走査し、未知の型・未登録フック・未実装アクションハンドラーを警告します。
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
    private readonly IProjectHookRegistry _projectHookRegistry;
    private readonly IProjectActionRegistry _projectActionRegistry;
    private readonly IEntityHooksService _entityHooks;
    private readonly ILogger<YamlConfigStartupValidator> _logger;

    public YamlConfigStartupValidator(
        ProjectManager projectManager,
        IEntityHookRegistry hookRegistry,
        IProjectHookRegistry projectHookRegistry,
        IProjectActionRegistry projectActionRegistry,
        IEntityHooksService entityHooks,
        ILogger<YamlConfigStartupValidator> logger)
    {
        _projectManager = projectManager;
        _hookRegistry = hookRegistry;
        _projectHookRegistry = projectHookRegistry;
        _projectActionRegistry = projectActionRegistry;
        _entityHooks = entityHooks;
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

                // フック名チェック（フレームワーク + プロジェクト両レジストリで確認）
                if (def.Hooks != null)
                {
                    var hookSelectors = new System.Func<Models.EntityHooksDefinition, object?>[]
                    {
                        h => h.BeforeCreate, h => h.BeforeUpdate, h => h.BeforeDelete,
                        h => h.AfterCreate,  h => h.AfterUpdate,  h => h.AfterDelete
                    };
                    foreach (var selector in hookSelectors)
                    {
                        var hookList = _entityHooks.GetHookList(def.Hooks, selector);
                        if (hookList == null) continue;
                        foreach (var hookName in hookList)
                        {
                            if (string.IsNullOrWhiteSpace(hookName) || hookName.StartsWith('@'))
                                continue;
                            var baseName = hookName.Split(':', 2)[0];
                            var inFramework = _hookRegistry.Find(baseName) != null;
                            var inProject = _projectHookRegistry.Find(project.Name, baseName) != null;
                            if (!inFramework && !inProject)
                            {
                                _logger.LogWarning(
                                    "yaml_config_warn event=unimplemented_hook project={Project} entity={Entity} hook={Hook} " +
                                    "hint=Hooks/ ディレクトリに実装クラスが見つかりません。dotnet run -- --scaffold-missing-hooks --project={Project2} で自動生成できます",
                                    project.Name, entityName, baseName, project.Name);
                                warnCount++;
                            }
                        }
                    }
                }

                // アクションハンドラー名チェック
                foreach (var (actionKey, action) in def.Actions)
                {
                    if (string.IsNullOrWhiteSpace(action.Handler)) continue;
                    if (_projectActionRegistry.Find(project.Name, action.Handler) == null)
                    {
                        _logger.LogWarning(
                            "yaml_config_warn event=unimplemented_action_handler project={Project} entity={Entity} action={Action} handler={Handler} " +
                            "hint=Hooks/ ディレクトリに ICustomActionHandler 実装が見つかりません。dotnet run -- --scaffold-missing-hooks --project={Project2} で自動生成できます",
                            project.Name, entityName, actionKey, action.Handler, project.Name);
                        warnCount++;
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
