using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetYamlForge.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace NetYamlForge.Controllers;

public partial class PageController
{
    // POST /{project}/Page/{pageName}/section/{sectionId}/file-upload
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/file-upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FileUpload(string project, string pageName, string sectionId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound();

        var files = Request.Form.Files;
        if (files.Count == 0)
            return BadRequest(new { error = "ファイルが選択されていません" });

        var uploadDir = !string.IsNullOrWhiteSpace(section.UploadDir)
            ? section.UploadDir
            : "uploads/photo-vault";

        var results = new List<object>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var savedPath = await _fileUploadService.UploadAsync(file, uploadDir);
                var ext = Path.GetExtension(file.FileName).TrimStart('.');
                var newUuid = Guid.NewGuid().ToString();

                var exifData = ExtractExif(Path.Combine(_env.WebRootPath, savedPath.TrimStart('/')));
                var templateVars = new Dictionary<string, string?>
                {
                    ["filename"]    = file.FileName,
                    ["upload_path"] = savedPath,
                    ["file_size"]   = file.Length.ToString(),
                    ["ext_upper"]   = ext.ToUpperInvariant(),
                    ["now"]         = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["current_user"] = User.Identity?.Name,
                    ["uuid"]        = newUuid,
                    ["photo_id"]    = newUuid,
                    ["exif.width"]  = exifData.width?.ToString(),
                    ["exif.height"] = exifData.height?.ToString(),
                    ["exif.taken_at"] = exifData.taken_at,
                    ["exif.make"]   = exifData.make,
                    ["exif.model"]  = exifData.model,
                    ["exif.focal_length"] = exifData.focal_length,
                    ["exif.aperture"] = exifData.aperture,
                    ["exif.shutter_speed"] = exifData.shutter_speed,
                    ["exif.iso"]    = exifData.iso?.ToString(),
                    ["exif.gps_lat"] = exifData.gps_lat?.ToString(),
                    ["exif.gps_lon"] = exifData.gps_lon?.ToString(),
                };

                if (section.ExtraFields != null)
                {
                    foreach (var ef in section.ExtraFields)
                    {
                        var formVal = Request.Form.TryGetValue(ef.Id, out var val) ? val.ToString() : null;
                        if (ef.Type == "bool" && Request.Form.TryGetValue(ef.Id, out var boolValues))
                        {
                            formVal = boolValues.Any(v => v == "true") ? "true" : "false";
                        }
                        templateVars[ef.Id] = !string.IsNullOrEmpty(formVal) ? formVal : (ef.Default ?? "");
                    }
                }
                foreach (var key in Request.Form.Keys)
                {
                    if (!templateVars.ContainsKey(key))
                    {
                        templateVars[key] = Request.Form[key].ToString();
                    }
                }

                long? newId = null;

                var oc = section.OnUploadComplete;
                if (oc != null && !string.IsNullOrWhiteSpace(oc.InsertEntity))
                {
                    // Build INSERT for primary entity
                    var insertFields = oc.Fields
                        .Select(kv => new KeyValuePair<string, object?>(
                            kv.Key,
                            (object?)ResolveTemplate(kv.Value, templateVars)))
                        .ToList();

                    var cols  = string.Join(", ", insertFields.Select(f => $"\"{f.Key}\""));
                    var parms = string.Join(", ", insertFields.Select(f => $"@{f.Key}"));
                    var insertSql = $"INSERT INTO \"{oc.InsertEntity}\" ({cols}) VALUES ({parms}); SELECT {_dialect.LastInsertIdExpression};";

                    var param = new DynamicParameters();
                    foreach (var f in insertFields)
                        param.Add(f.Key, f.Value is "" ? null : f.Value);

                    newId = await _db.ExecuteScalarAsync<long?>(insertSql, param);

                    // Build INSERT for secondary entity (e.g., processing_queue)
                    if (!string.IsNullOrWhiteSpace(oc.ThenInsertEntity) && oc.ThenFields.Count > 0)
                    {
                        templateVars[$"{oc.InsertEntity}.photo_id"] = newUuid;
                        templateVars["photo_id"] = newUuid;

                        var thenFields = oc.ThenFields
                            .Select(kv => new KeyValuePair<string, object?>(
                                kv.Key,
                                (object?)ResolveTemplate(kv.Value, templateVars)))
                            .ToList();

                        var tCols  = string.Join(", ", thenFields.Select(f => $"\"{f.Key}\""));
                        var tParms = string.Join(", ", thenFields.Select(f => $"@{f.Key}"));
                        var thenSql = $"INSERT INTO \"{oc.ThenInsertEntity}\" ({tCols}) VALUES ({tParms})";

                        var tParam = new DynamicParameters();
                        foreach (var f in thenFields)
                            tParam.Add(f.Key, f.Value is "" ? null : f.Value);

                        await _db.ExecuteAsync(thenSql, tParam);
                    }
                }

                results.Add(new { file = file.FileName, path = savedPath, id = newId });
                _logger.LogInformation("FileUpload: saved {File} to {Path}, photo_id={Id}", file.FileName, savedPath, newId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileUpload: failed for {File}", file.FileName);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return Json(new
        {
            success = errors.Count == 0,
            uploaded = results.Count,
            errors,
            files = results
        });
    }
}
