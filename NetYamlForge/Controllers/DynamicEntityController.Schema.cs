// ファイル概要: 動的エンティティの一覧・作成・編集・削除・部分更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.DynamicEntity;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;

namespace NetYamlForge.Controllers;

public partial class DynamicEntityController : BaseProjectController
{
    // エンティティ定義（フィールド・フォーム・フィルタ等）をページで表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult Definition(string entity = "customer")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var meta = _meta.Get(entity);
        return View("Definition", new EntityDefinitionViewModel(entity, meta));
    }

    // 全エンティティ定義の概要一覧を表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult AllDefinitions()
    {
        var all = _meta.GetAll();
        return View("AllDefinitions", new AllDefinitionsViewModel(all));
    }

    // 現在プロジェクトの有効エンティティ設定を診断表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    public IActionResult ConfigDiagnostics(string entity = "customer", bool onlyChanged = true)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var all = _meta.GetAll();
        var projectName = _projectScope.Current?.Name ?? "";
        var diagnostics = _configDiagnosticsService.Build(entity, all, onlyChanged);

        return View("ConfigDiagnostics", new ConfigDiagnosticsViewModel(
            projectName,
            diagnostics.SelectedEntity,
            diagnostics.Entities,
            diagnostics.BaseJson,
            diagnostics.EffectiveJson,
            diagnostics.DiffLines,
            onlyChanged,
            diagnostics.ChangedCount));
    }

    // YAML 定義と物理テーブルの差分、および適用/回滚用 SQL を表示します（Admin のみ）
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> SchemaMigration(string entity = "customer")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        if (!_meta.TryGet(entity, out var meta))
        {
            return NotFound($"Entity '{entity}' は このプロジェクトに存在しません。");
        }

        var project = _projectScope.Current;
        var physicalColumns = await _schemaMigrationService.GetPhysicalColumnsAsync(_db, meta.Table);
        var plan = _schemaMigrationService.BuildPlan(entity, meta, physicalColumns, project.DatabaseType);
        var (upSql, downSql, backupTableName) = _schemaMigrationService.GenerateSql(plan, meta, project.DatabaseType);
        var history = await _schemaMigrationService.GetHistoryAsync(_db, project.Name);

        return View("SchemaMigration", new SchemaMigrationViewModel(
            project.Name,
            entity,
            _meta.GetAll().Keys.OrderBy(x => x).ToList(),
            meta,
            physicalColumns,
            plan,
            upSql,
            downSql,
            backupTableName,
            history));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("~/{project}/DynamicEntity/SchemaMigration/Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SchemaMigrationApply(string entity = "customer")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        if (!_meta.TryGet(entity, out var meta))
        {
            return NotFound($"Entity '{entity}' は このプロジェクトに存在しません。");
        }

        var project = _projectScope.Current;
        var physicalColumns = await _schemaMigrationService.GetPhysicalColumnsAsync(_db, meta.Table);
        var plan = _schemaMigrationService.BuildPlan(entity, meta, physicalColumns, project.DatabaseType);
        var result = await _schemaMigrationService.ApplyAsync(_db, project.Name, plan, meta, project.DatabaseType, dryRun: false);
        TempData["SchemaMigrationMessage"] = result.Applied
            ? $"Migration applied: {result.MigrationId}"
            : "No schema changes to apply.";
        return RedirectToAction(nameof(SchemaMigration), new { entity });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("~/{project}/DynamicEntity/SchemaMigration/Rollback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SchemaMigrationRollback(string migrationId, string entity = "customer")
    {
        migrationId = NormalizeSingleValue(migrationId) ?? string.Empty;
        entity = NormalizeSingleValue(entity) ?? "customer";
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            return BadRequest("migrationId is required.");
        }

        await _schemaMigrationService.RollbackAsync(_db, migrationId);
        TempData["SchemaMigrationMessage"] = $"Migration rolled back: {migrationId}";
        return RedirectToAction(nameof(SchemaMigration), new { entity });
    }
}
