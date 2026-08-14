using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace NetYamlForge.Controllers;

public partial class PageController
{
    // POST /{project}/Page/{pageName}/section/{sectionId}/form-submit
#pragma warning disable DCS001 // Entity/table names come from YAML config, validated by section definition
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/form-submit")]
    [Consumes("application/json")]
    public async Task<IActionResult> FormSubmit(string project, string pageName, string sectionId, [FromBody] Dictionary<string, string> formData)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound(new { error = $"Page '{pageName}' not found." });
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null || section.Fields == null)
            return BadRequest(new { error = "Section not found or not a form." });

        var submitAction = section.Actions?.FirstOrDefault(a =>
            string.Equals(a.Type, "submit", StringComparison.OrdinalIgnoreCase));
        if (submitAction == null || string.IsNullOrEmpty(submitAction.InsertEntity))
            return BadRequest(new { error = "No submit action or insert entity defined." });

        var entityName = submitAction.InsertEntity;
        var fieldMapping = submitAction.Fields;
        if (fieldMapping == null || fieldMapping.Count == 0)
            return BadRequest(new { error = "No insert field mapping defined." });

        try
        {
            // Build column → value map from form data
            var insertCols = new List<string>();
            var insertParams = new Dictionary<string, object?>();
            foreach (var kv in fieldMapping)
            {
                var colName = kv.Key;       // DB column name
                var template = kv.Value;    // e.g. "{{source_path}}" or literal like "directory"

                object? val;
                if (template == "{{uuid}}")
                {
                    val = Guid.NewGuid().ToString("N");
                }
                else if (template == "{{now}}")
                {
                    val = DateTime.UtcNow;
                }
                else if (template == "{{current_user}}")
                {
                    val = User.Identity?.Name ?? "";
                }
                else if (template.StartsWith("{{") && template.EndsWith("}}"))
                {
                    var fieldId = template[2..^2].Trim();
                    formData.TryGetValue(fieldId, out var raw);
                    val = raw;
                }
                else
                {
                    val = template; // literal value
                }

                insertCols.Add(colName);
                var paramName = $"@{colName}";
                insertParams[paramName] = val;
            }

            var colList = string.Join(", ", insertCols);
            var paramList = string.Join(", ", insertParams.Keys);
            var sql = $"INSERT INTO {entityName} ({colList}) VALUES ({paramList})";

            await _db.ExecuteAsync(sql, insertParams);

            await TryWritePageAuditAsync("page_form_submit", entityName,
                $"Page={pageName},Section={sectionId},Entity={entityName}");

            // Trigger the import worker immediately instead of waiting for cron
            _ = _scheduler.TriggerJobNowAsync(proj.Name, "photo_import_worker");

            return Ok(new
            {
                message = submitAction.SuccessMessage ?? "提交成功",
                refreshSection = submitAction.RefreshSection
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormSubmit failed for {Page}/{Section}", pageName, sectionId);
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /{project}/Page/{pageName}/section/{sectionId}/form-submit (Form URL encoded)
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/form-submit")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SectionFormSubmit(string project, string pageName, string sectionId,
        [FromForm] string actionId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound();

        var action = section.Actions?.FirstOrDefault(a => a.Id == actionId && a.Type == "submit");
        if (action?.InsertEntity == null)
            return BadRequest("Invalid action");

        var templateVars = new Dictionary<string, string?>
        {
            ["now"]          = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["current_user"] = User.Identity?.Name,
            ["uuid"]         = Guid.NewGuid().ToString(),
        };

        if (section.Fields != null)
        {
            foreach (var field in section.Fields)
            {
                var val = Request.Form.TryGetValue(field.Id, out var fv) ? fv.ToString() : field.Default ?? "";
                if (field.Type == "bool" && Request.Form.TryGetValue(field.Id, out var boolValues))
                {
                    val = boolValues.Any(v => v == "true") ? "true" : "false";
                }
                templateVars[field.Id] = val;
            }
        }

        var insertFields = action.Fields
            .Select(kv => new KeyValuePair<string, object?>(
                kv.Key,
                (object?)ResolveTemplate(kv.Value, templateVars)))
            .ToList();

        var cols  = string.Join(", ", insertFields.Select(f => $"\"{f.Key}\""));
        var parms = string.Join(", ", insertFields.Select(f => $"@{f.Key}"));
        var insertSql = $"INSERT INTO \"{action.InsertEntity}\" ({cols}) VALUES ({parms})";

        var param = new Dapper.DynamicParameters();
        foreach (var f in insertFields)
            param.Add(f.Key, f.Value is "" ? null : f.Value);

        await _db.ExecuteAsync(insertSql, param);

        await TryWritePageAuditAsync("form_submit", action.InsertEntity,
            $"Page={pageName},Section={sectionId},Action={actionId}");

        var successMsg = action.SuccessMessage ?? "操作成功";
        Response.Headers["HX-Trigger"] = ToAsciiJson(
            $"{{\"show-toast\":{{\"message\":\"{successMsg.Replace("\"", "\\\"")}\",\"type\":\"success\"}}}}");

        if (!string.IsNullOrEmpty(action.RefreshSection))
            Response.Headers["HX-Trigger-After-Settle"] =
                $"{{\"nyf-refresh-section\":{{\"sectionId\":\"{action.RefreshSection}\"," +
                $"\"url\":\"/{project}/Page/{pageName}/section/{action.RefreshSection}\"}}}}";

        return Content(
            $"<div class=\"alert alert-success text-sm py-2 px-4 mt-2\">{System.Web.HttpUtility.HtmlEncode(successMsg)}</div>",
            "text/html");
    }
}
