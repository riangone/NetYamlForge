// ファイル概要：MCP（Model Context Protocol）クライアント向けに公開するエンティティ CRUD ツール群。
// 実処理は `EntityToolService` に委譲し、本クラスは [McpServerTool] 属性によるツール宣言と
// パラメータ転送のみを担います。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NetYamlForge.Services.Mcp;

/// <summary>
/// `/mcp` エンドポイント経由で公開されるエンティティ操作ツール群。
/// すべてのツールの第1引数は対象テナントプロジェクト名（`project`）であり、
/// 内部で `ProjectScope` への bind を行った上で既存の動的エンティティサービスへ委譲します。
/// </summary>
[McpServerToolType]
public sealed class EntityMcpTools
{
    private readonly EntityToolService _service;

    public EntityMcpTools(EntityToolService service)
    {
        _service = service;
    }

    [McpServerTool(Name = "list_projects")]
    [Description("登録されている全てのテナントプロジェクト（name, displayName）を一覧表示します。")]
    public McpToolResult ListProjects()
        => _service.ListProjects();

    [McpServerTool(Name = "list_entities")]
    [Description("指定プロジェクトで API アクセスが有効（api != disabled）なエンティティの一覧を取得します。")]
    public McpToolResult ListEntities(
        [Description("テナントプロジェクト名（例: blog）")] string project)
        => _service.ListEntities(project);

    [McpServerTool(Name = "get_entity_meta")]
    [Description("指定エンティティのカラム定義・フォーム定義・主キー・利用可能なカスタムアクションを取得します。API が disabled の場合はエラーを返します。")]
    public McpToolResult GetEntityMeta(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名（entities.yml のキー）")] string entity)
        => _service.GetEntityMeta(project, entity);

    [McpServerTool(Name = "list_entity_records", ReadOnly = true)]
    [Description("エンティティのレコード一覧をページネーション・検索・ソート付きで取得します。API が disabled の場合はエラーを返します。")]
    public Task<McpToolResult> ListEntityRecords(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("検索キーワード（任意）")] string? search = null,
        [Description("ソート対象カラム名（任意）")] string? sort = null,
        [Description("ソート方向 'asc' または 'desc'（任意、既定値 'asc'）")] string? dir = null,
        [Description("ページ番号（1始まり、既定値 1）")] int page = 1,
        [Description("1ページあたりの件数（既定値 20）")] int pageSize = 20)
        => _service.ListRecordsAsync(project, entity, search, sort, dir, page, pageSize);

    [McpServerTool(Name = "get_entity_record", ReadOnly = true)]
    [Description("主キーを指定して、エンティティの単一レコードを取得します。API が disabled の場合はエラーを返します。")]
    public Task<McpToolResult> GetEntityRecord(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("レコードの主キー値")] string id)
        => _service.GetRecordAsync(project, entity, id);

    [McpServerTool(Name = "create_entity_record")]
    [Description("新しいエンティティレコードを作成します。`data` はカラム名→値のマップです。API が disabled または readonly の場合はエラーを返します。")]
    public Task<McpToolResult> CreateEntityRecord(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("作成するレコードのカラム名→値のマップ")] Dictionary<string, object?> data)
        => _service.CreateRecordAsync(project, entity, data);

    [McpServerTool(Name = "update_entity_record")]
    [Description("既存のエンティティレコードをフィールド単位で更新します（PATCH 相当）。`data` に指定したフィールドのみ更新されます。API が disabled または readonly の場合はエラーを返します。")]
    public Task<McpToolResult> UpdateEntityRecord(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("更新対象レコードの主キー値")] string id,
        [Description("更新するカラム名→値のマップ（指定したフィールドのみ更新）")] Dictionary<string, object?> data)
        => _service.UpdateRecordAsync(project, entity, id, data);

    [McpServerTool(Name = "delete_entity_record", Destructive = true)]
    [Description("主キーを指定して、エンティティレコードを削除します。API が disabled または readonly の場合はエラーを返します。")]
    public Task<McpToolResult> DeleteEntityRecord(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("削除するレコードの主キー値")] string id)
        => _service.DeleteRecordAsync(project, entity, id);

    [McpServerTool(Name = "invoke_entity_action")]
    [Description("エンティティに定義されたカスタムアクション（entities.yml の actions）を実行します。API が disabled または readonly の場合はエラーを返します。")]
    public Task<McpToolResult> InvokeEntityAction(
        [Description("テナントプロジェクト名")] string project,
        [Description("エンティティ名")] string entity,
        [Description("対象レコードの主キー値")] string id,
        [Description("実行するアクション名（entities.yml の actions キー）")] string actionKey,
        [Description("アクションに渡す入力パラメータ（任意）")] Dictionary<string, object?>? inputs = null)
        => _service.InvokeActionAsync(project, entity, id, actionKey, inputs);
}
