// ファイル概要: Admin 向けの設定診断機能を提供するサービスです。
// グローバル設定（config/entities/）とプロジェクトオーバーライド後の
// 有効設定を JSON 形式で比較・差分表示します。
// DynamicEntityController.ConfigDiagnostics アクションから呼ばれます。

using System.Text.Json;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// エンティティ設定の診断情報を構築するサービス。
/// 「グローバル基底設定」vs「プロジェクト適用後の有効設定」の差分を
/// 行単位のテキストリストとして返します（Added/Removed/Changed の色分け表示に使用）。
/// </summary>
public sealed class DynamicEntityConfigDiagnosticsService
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly IBaseEntityMetadataProvider _baseMetadataProvider;
    private readonly DynamicEntityConfigDiffService _configDiffService;
    private readonly ILogger<DynamicEntityConfigDiagnosticsService> _logger;

    public DynamicEntityConfigDiagnosticsService(
        IBaseEntityMetadataProvider baseMetadataProvider,
        DynamicEntityConfigDiffService configDiffService,
        ILogger<DynamicEntityConfigDiagnosticsService> logger)
    {
        _baseMetadataProvider = baseMetadataProvider;
        _configDiffService = configDiffService;
        _logger = logger;
    }

    /// <summary>
    /// 指定エンティティの診断結果を構築します。
    /// </summary>
    /// <param name="requestedEntity">表示するエンティティ名（存在しない場合は先頭エンティティを使用）</param>
    /// <param name="all">プロジェクト適用後の全エンティティ定義マップ</param>
    /// <param name="onlyChanged">true なら変更行のみ表示、false なら全行表示</param>
    public DynamicEntityConfigDiagnosticsResult Build(
        string requestedEntity,
        IReadOnlyDictionary<string, EntityDefinition> all,
        bool onlyChanged)
    {
        var selectedEntity = all.ContainsKey(requestedEntity)
            ? requestedEntity
            : all.Keys.FirstOrDefault() ?? requestedEntity;
        var effectiveMeta = all.TryGetValue(selectedEntity, out var meta) ? meta : null;
        EntityDefinition? baseMeta = null;

        try
        {
            _baseMetadataProvider.TryGet(selectedEntity, out baseMeta);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConfigDiagnostics base metadata load failed for entity={Entity}", selectedEntity);
        }

        var baseJson = ToJson(baseMeta);
        var effectiveJson = ToJson(effectiveMeta);
        var (diffLines, changedCount) = _configDiffService.BuildJsonDiffLines(
            baseMeta,
            effectiveMeta,
            includeUnchanged: !onlyChanged);

        return new DynamicEntityConfigDiagnosticsResult(
            selectedEntity,
            all.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            baseJson,
            effectiveJson,
            diffLines,
            changedCount);
    }

    private static string ToJson(EntityDefinition? meta) =>
        meta == null ? "{}" : JsonSerializer.Serialize(meta, PrettyJson);
}

/// <summary>
/// DynamicEntityConfigDiagnosticsService.Build の戻り値。
/// ConfigDiagnostics ビューで設定の JSON 比較とプロジェクト別エンティティ一覧を表示するために使用します。
/// </summary>
/// <param name="SelectedEntity">実際に選択されたエンティティ名</param>
/// <param name="Entities">全エンティティ名リスト（プルダウン選択用）</param>
/// <param name="BaseJson">グローバル基底設定の JSON テキスト</param>
/// <param name="EffectiveJson">プロジェクトオーバーライド後の有効設定の JSON テキスト</param>
/// <param name="DiffLines">差分行テキストリスト（"+" 追加、"-" 削除、" " 変更なし で始まる）</param>
/// <param name="ChangedCount">変更のあった設定フィールド数</param>
public sealed record DynamicEntityConfigDiagnosticsResult(
    string SelectedEntity,
    IReadOnlyList<string> Entities,
    string BaseJson,
    string EffectiveJson,
    IReadOnlyList<string> DiffLines,
    int ChangedCount);
