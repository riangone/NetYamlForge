// ファイル概要: カスタムページ機能のコントローラー。
// pages/*.yaml で定義したページを /{project}/Page/{pageName} でレンダリングします。
// セクションの行レベル更新・削除も担当します。

using System.Data;
using System.Security.Claims;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Dialect;
using NetYamlForge.Services.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace NetYamlForge.Controllers;

[Route("{project}/Page")]
public partial class PageController : BaseProjectController
{
    /// <summary>閲覧・更新ともに Admin 専用 of ページ名セット。</summary>
    private static readonly HashSet<string> AdminOnlyPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApprovalInbox",
        "AssignmentRules",
        "DuplicateRules",
        "AutomationRules",
        "DataImportExport",
        "ObjectManager",
        "RoleAccessMatrix",
        "UserRoleProfile",
        "AuditTrail",
        "AuditMetrics",
        "IntegrationHub",
        "WebhookDeliveryMonitor"
    };
    private static readonly HashSet<string> AdminOnlyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsAdmin",
        "IsActive",
        "PreferredLanguage",
        "PasswordHash",
        "Role"
    };

    private readonly ProjectScope _projectScope;
    private readonly IAuditLogService _audit;
    private readonly IPagePermissionService _pagePermission;
    private readonly PageRowMutationService _rowMutationService;
    private readonly PageDataQueryService _pageDataQueryService;
    private readonly PageViewPreferenceService _pageViewPreferenceService;
    private readonly SectionRowFormViewModelFactory _formViewModelFactory;
    private readonly IFileUploadService _fileUploadService;
    private readonly IWebHostEnvironment _env;
    private readonly IDbConnection _db;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<PageController> _logger;
    private readonly NetYamlForge.Services.BatchJob.IBatchJobScheduler _scheduler;
    private readonly IPageActionDispatcher _pageActionDispatcher;

    public PageController(
        ProjectScope projectScope,
        IAuditLogService audit,
        IPagePermissionService pagePermission,
        PageRowMutationService rowMutationService,
        PageDataQueryService pageDataQueryService,
        PageViewPreferenceService pageViewPreferenceService,
        SectionRowFormViewModelFactory formViewModelFactory,
        IFileUploadService fileUploadService,
        IWebHostEnvironment env,
        IDbConnection db,
        ISqlDialect dialect,
        ILogger<PageController> logger,
        NetYamlForge.Services.BatchJob.IBatchJobScheduler scheduler,
        IPageActionDispatcher pageActionDispatcher)
    {
        _projectScope = projectScope;
        _audit = audit;
        _pagePermission = pagePermission;
        _rowMutationService = rowMutationService;
        _pageDataQueryService = pageDataQueryService;
        _pageViewPreferenceService = pageViewPreferenceService;
        _formViewModelFactory = formViewModelFactory;
        _fileUploadService = fileUploadService;
        _env = env;
        _db = db;
        _dialect = dialect;
        _logger = logger;
        _scheduler = scheduler;
        _pageActionDispatcher = pageActionDispatcher;
    }

    private async Task TryWritePageAuditAsync(string action, string? entity, string detail)
    {
        try
        {
            await _audit.WriteAsync(action, entity, detail, User.Identity?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit write failed for action={Action}, entity={Entity}", action, entity);
        }
    }

    private static (int? width, int? height, string? taken_at, string? make, string? model,
        string? focal_length, string? aperture, string? shutter_speed, int? iso,
        double? gps_lat, double? gps_lon) ExtractExif(string filePath)
    {
        try
        {
            using var image = Image.Load(filePath);
            var exif = image.Metadata?.ExifProfile;
            if (exif == null) return (null, null, null, null, null, null, null, null, null, null, null);

            string? ExifStr(ExifTag<string> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<string> sv)
                    return sv.Value;
                return null;
            }

            Rational? ExifRat(ExifTag<Rational> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<Rational> rv)
                    return rv.Value;
                return null;
            }

            ushort[]? ExifUshortArr(ExifTag<ushort[]> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<ushort[]> iv)
                    return iv.Value;
                return null;
            }

            var width  = image.Width;
            var height = image.Height;

            var takenAt = ExifStr(ExifTag.DateTimeOriginal)
                       ?? ExifStr(ExifTag.DateTimeDigitized)
                       ?? ExifStr(ExifTag.DateTime);

            var make  = ExifStr(ExifTag.Make);
            var model = ExifStr(ExifTag.Model);

            var focal = ExifRat(ExifTag.FocalLength);
            var focalStr = focal.HasValue ? $"{focal.Value.Numerator / (double)focal.Value.Denominator:F1}" : null;

            var apert = ExifRat(ExifTag.FNumber);
            var apertStr = apert.HasValue ? $"f/{apert.Value.Numerator / (double)apert.Value.Denominator:F1}" : null;

            var shutter = ExifRat(ExifTag.ExposureTime);
            var shutterStr = shutter.HasValue
                ? (shutter.Value.Numerator >= shutter.Value.Denominator
                    ? $"{shutter.Value.Numerator / (double)shutter.Value.Denominator:F1}s"
                    : $"{shutter.Value.Numerator}/{shutter.Value.Denominator}s")
                : null;

            var isoVal = (int?)ExifUshortArr(ExifTag.ISOSpeedRatings)?.FirstOrDefault();

            double? gpsLat = null, gpsLon = null;
            if (exif.TryGetValue(ExifTag.GPSLatitude, out var latV) && latV is IExifValue<Rational[]> latArr)
            {
                var vals = latArr.Value!;
                if (vals.Length == 3)
                {
                    gpsLat = (double)vals[0].Numerator / vals[0].Denominator
                           + (double)vals[1].Numerator / vals[1].Denominator / 60
                           + (double)vals[2].Numerator / vals[2].Denominator / 3600;
                }
                if (exif.TryGetValue(ExifTag.GPSLatitudeRef, out var lr) && lr is IExifValue<string> latRef && latRef.Value == "S")
                    gpsLat = -gpsLat;
            }
            if (exif.TryGetValue(ExifTag.GPSLongitude, out var lonV) && lonV is IExifValue<Rational[]> lonArr)
            {
                var vals = lonArr.Value!;
                if (vals.Length == 3)
                {
                    gpsLon = (double)vals[0].Numerator / vals[0].Denominator
                           + (double)vals[1].Numerator / vals[1].Denominator / 60
                           + (double)vals[2].Numerator / vals[2].Denominator / 3600;
                }
                if (exif.TryGetValue(ExifTag.GPSLongitudeRef, out var lr) && lr is IExifValue<string> lonRef && lonRef.Value == "W")
                    gpsLon = -gpsLon;
            }

            return (width, height, takenAt, make, model, focalStr, apertStr, shutterStr, isoVal, gpsLat, gpsLon);
        }
        catch
        {
            return (null, null, null, null, null, null, null, null, null, null, null);
        }
    }

    private static readonly Regex TemplateVarRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    private static string? ResolveTemplate(string template, Dictionary<string, string?> vars)
    {
        return TemplateVarRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return vars.TryGetValue(key, out var v) ? v ?? "" : m.Value;
        });
    }

    private async Task<List<Dictionary<string, string>>> LoadSavedViewsAsync(string pageName)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return new List<Dictionary<string, string>>();

        try
        {
            var rows = await _pageViewPreferenceService.LoadSavedViewsAsync(
                _projectScope.Current.Name,
                pageName,
                userName);

            var result = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var url = QueryHelpers.AddQueryString(
                    $"/{_projectScope.Current.Name}/Page/{pageName}",
                    row.Filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                        .ToDictionary(kv => kv.Key, kv => (string?)kv.Value));

                result.Add(new Dictionary<string, string>
                {
                    ["ViewName"] = row.ViewName,
                    ["Url"] = url,
                    ["IsDefault"] = row.IsDefault ? "1" : "0"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadSavedViews skipped for page={Page}", pageName);
            return new List<Dictionary<string, string>>();
        }
    }

    private Dictionary<string, string> FilterFormFields(HashSet<string> allowed)
        => Request.Form
            .Where(kv => allowed.Contains(kv.Key) &&
                         !string.Equals(kv.Key, "__RequestVerificationToken", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    private async Task<SectionRenderModel> BuildSectionRenderModelAsync(
        ProjectInfo proj,
        SectionDefinition section,
        string pageName,
        IDictionary<string, string> allFilters,
        IEnumerable<string>? allSectionIds = null,
        PageUserContext? userContext = null)
    {
        var (rows, total) = await _pageDataQueryService.LoadSectionDataAsync(section, allFilters, allSectionIds, userContext);
        return new SectionRenderModel
        {
            Sec = section,
            Rows = rows.ToList(),
            Total = total,
            Project = proj.Name,
            PageName = pageName,
            AllQueryParams = allFilters
        };
    }

    private async Task<IActionResult> ReturnSectionOrRedirectAsync(
        ProjectInfo proj, SectionDefinition section, string pageName, string sectionId)
    {
        if (IsHtmxRequest())
        {
            Response.Headers["HX-Retarget"] = $"#section-{sectionId}";
            Response.Headers["HX-Reswap"] = "innerHTML";
            var allSectionIds = proj.PageMetadata.TryGet(pageName, out var pageDef)
                ? pageDef.Sections.Select(s => s.Id)
                : null;
            return PartialView("Components/_SectionTable",
                await BuildSectionRenderModelAsync(
                    proj,
                    section,
                    pageName,
                    GetFiltersFromHtmxCurrentUrl(),
                    allSectionIds,
                    BuildUserContext()));
        }
        return Redirect($"/{proj.Name}/Page/{pageName}");
    }

    private static bool IsAdminOnlyMutation(string pageName, string? targetTable)
    {
        if (AdminOnlyPages.Contains(pageName))
            return true;

        return string.Equals(targetTable, "AppUser", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetTable, "AuditLog", StringComparison.OrdinalIgnoreCase);
    }

    private PageUserContext BuildUserContext() => new(
        UserName: User.Identity?.Name ?? "",
        DisplayName: User.FindFirst(ClaimTypes.GivenName)?.Value ?? "",
        UserId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
        Roles: User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList(),
        IsAdmin: UserIsAdmin(),
        IsAuthenticated: User.Identity?.IsAuthenticated == true,
        OwningProject: User.FindFirst("owning_project")?.Value
    );

    private static bool CanViewSection(SectionDefinition section, PageUserContext userContext)
    {
        if (section.VisibleToRoles == null || section.VisibleToRoles.Count == 0)
            return true;
        if (userContext.IsAdmin)
            return true;
        return userContext.HasAnyRole(section.VisibleToRoles);
    }

    private static string ToAsciiJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        var sb = new System.Text.StringBuilder();
        foreach (var c in json)
        {
            if (c > 127)
            {
                sb.AppendFormat("\\u{0:x4}", (int)c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
