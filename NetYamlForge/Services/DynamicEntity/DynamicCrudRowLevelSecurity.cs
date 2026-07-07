using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Http;
using NetYamlForge.Models;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Services;

/// <summary>
/// DynamicCrudRepository の行レベルセキュリティ（RLS）ロジックを分離したユーティリティクラス。
/// ポリシーベースのアクセス制御とカスタムRLSコンテキスト評価を担当します。
/// </summary>
public class DynamicCrudRowLevelSecurity
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IProjectBusinessLogicRegistry? _bizLogicRegistry;
    private readonly IUserAuthService? _userAuthService;
    private readonly ProjectScope? _projectScope;
    private readonly TenantContext? _tenantContext;
    private readonly IDbConnection _db;
    private readonly ILogger<DynamicCrudRowLevelSecurity> _logger;

    public DynamicCrudRowLevelSecurity(
        IHttpContextAccessor? httpContextAccessor,
        IProjectBusinessLogicRegistry? bizLogicRegistry,
        IUserAuthService? userAuthService,
        ProjectScope? projectScope,
        TenantContext? tenantContext,
        IDbConnection db,
        ILogger<DynamicCrudRowLevelSecurity> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _bizLogicRegistry = bizLogicRegistry;
        _userAuthService = userAuthService;
        _projectScope = projectScope;
        _tenantContext = tenantContext;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 行レベルセキュリティポリシーをWHERE句に適用します。
    /// </summary>
    public async Task ApplyRowLevelSecurityAsync(EntityDefinition meta, List<string> where, DynamicParameters param)
    {
        if (meta.Security?.RowLevelSecurity?.Enabled != true)
        {
            return;
        }

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null) return;
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            where.Add("1 = 0");
            return;
        }

        var userName = user.Identity.Name;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        int userId = 0;
        if (userIdClaim != null)
        {
            int.TryParse(userIdClaim.Value, out userId);
        }

        var roles = await GetCurrentUserRolesAsync();
        var policies = meta.Security.RowLevelSecurity.Policies ?? new List<RowLevelSecurityPolicy>();
        var policyClauses = new List<string>();

        var dynamicParams = new Dictionary<string, object?>();
        if (_projectScope != null && _projectScope.IsSet && _bizLogicRegistry != null && !string.IsNullOrEmpty(userName))
        {
            var projectName = _projectScope.Current.Name;
            var evaluator = _bizLogicRegistry.GetRlsContextEvaluator(projectName);
            if (evaluator != null)
            {
                try
                {
                    dynamicParams = await evaluator.EvaluateRlsContextAsync(meta.Table, userName, userId, _db, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to evaluate dynamic RLS context via Roslyn hook for entity {Entity}", meta.Table);
                }
            }
        }

        param.Add("Rls_CurrentUser", userName);
        param.Add("Rls_CurrentUserId", userId);

        if (dynamicParams != null)
        {
            foreach (var kv in dynamicParams)
            {
                param.Add($"Rls_{kv.Key}", kv.Value);
            }
        }

        foreach (var policy in policies)
        {
            if (roles.Any(r => string.Equals(r, policy.Role, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(policy.FilterClause))
                {
                    var rewritten = RewriteRlsClause(policy.FilterClause, dynamicParams!);
                    policyClauses.Add($"({rewritten})");
                }
            }
        }

        if (policyClauses.Count > 0)
        {
            where.Add("(" + string.Join(" OR ", policyClauses) + ")");
        }
        else if (policies.Count > 0)
        {
            where.Add("1 = 0");
        }
    }

    /// <summary>
    /// テナントコンテキストをWHERE句に適用します。
    /// </summary>
    public void ApplyTenantContext(EntityDefinition meta, List<string> where, DynamicParameters param)
    {
        if (_tenantContext != null &&
            _tenantContext.Strategy.Equals("logical", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            where.Add($"{meta.Table}.tenant_id = @TenantId");
            param.Add("TenantId", _tenantContext.TenantId);
        }
    }

    /// <summary>
    /// 現在のユーザーのロール一覧を取得します。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCurrentUserRolesAsync()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null) return Array.Empty<string>();
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true) return Array.Empty<string>();
        var userName = user.Identity.Name;
        if (string.IsNullOrEmpty(userName) || _userAuthService == null) return Array.Empty<string>();
        
        string? projectName = _projectScope?.IsSet == true ? _projectScope.Current.Name : null;
        return await _userAuthService.GetUserRolesAsync(userName, projectName);
    }

    /// <summary>
    /// 権限チェックを実行します。
    /// </summary>
    public async Task EnsurePermissionAsync(EntityDefinition meta, string action)
    {
        if (meta.Security?.Permissions == null) return;
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null) return;
        var user = httpContext.User;
        var roles = await GetCurrentUserRolesAsync();

        List<string>? allowedRoles = action.ToLowerInvariant() switch
        {
            "read" => meta.Security.Permissions.Read,
            "write" => meta.Security.Permissions.Write,
            "delete" => meta.Security.Permissions.Delete,
            _ => null
        };

        if (allowedRoles == null || allowedRoles.Count == 0) return;

        var hasAccess = roles.Any(r => allowedRoles.Any(ar => string.Equals(r, ar, StringComparison.OrdinalIgnoreCase))) ||
                        (user?.Identity?.IsAuthenticated == true && user.IsInRole("Admin"));

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException($"User does not have '{action}' permission on entity {meta.Table}.");
        }
    }

    /// <summary>
    /// フィールドレベルの書き込み権限を検証します。
    /// </summary>
    public async Task VerifyFieldWritePermissionsAsync(EntityDefinition meta, IDictionary<string, object?> values)
    {
        if (values == null || values.Count == 0) return;

        var roles = await GetCurrentUserRolesAsync();
        var httpContext = _httpContextAccessor?.HttpContext;
        var user = httpContext?.User;
        var isAdmin = user?.Identity?.IsAuthenticated == true && user.IsInRole("Admin");

        foreach (var kv in values)
        {
            var fieldName = kv.Key;
            FieldSecurityDefinition? fieldSec = null;

            if (meta.Columns.TryGetValue(fieldName, out var colDef))
            {
                fieldSec = colDef.Security;
            }
            else if (meta.Forms.TryGetValue(fieldName, out var formDef))
            {
                fieldSec = formDef.Security;
            }

            if (fieldSec?.WriteRoles != null && fieldSec.WriteRoles.Count > 0)
            {
                var hasWriteRole = isAdmin || roles.Any(r => fieldSec.WriteRoles.Any(ar => string.Equals(r, ar, StringComparison.OrdinalIgnoreCase)));
                if (!hasWriteRole)
                {
                    throw new UnauthorizedAccessException($"User does not have write permission for field '{fieldName}' on entity '{meta.Table}'.");
                }
            }
        }
    }

    /// <summary>
    /// フィールドレベルのセキュリティを適用します。
    /// </summary>
    public async Task<dynamic> ApplyFieldSecurityAsync(EntityDefinition meta, dynamic row)
    {
        if (row == null) return row;

        var dict = row as IDictionary<string, object>;
        if (dict == null) return row;

        var expando = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
        foreach (var kv in dict)
        {
            expando[kv.Key] = kv.Value;
        }

        var roles = await GetCurrentUserRolesAsync();

        foreach (var col in meta.Columns)
        {
            var fieldName = col.Key;
            var colDef = col.Value;
            if (colDef.Security == null) continue;

            if (colDef.Security.ReadRoles != null && colDef.Security.ReadRoles.Count > 0)
            {
                var hasReadRole = roles.Any(r => colDef.Security.ReadRoles.Any(ar => string.Equals(r, ar, StringComparison.OrdinalIgnoreCase)));
                if (!hasReadRole)
                {
                    expando.Remove(fieldName);
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(colDef.Security.ReadMask) && expando.TryGetValue(fieldName, out var val) && val != null)
            {
                var valStr = val.ToString() ?? "";
                if (colDef.Security.ReadMask.Equals("email", StringComparison.OrdinalIgnoreCase))
                {
                    expando[fieldName] = MaskEmail(valStr);
                }
                else
                {
                    expando[fieldName] = MaskGeneric(valStr);
                }
            }
        }

        return expando;
    }

    private static string RewriteRlsClause(string clause, Dictionary<string, object?> dynamicParams)
    {
        var rewritten = clause;
        rewritten = Regex.Replace(rewritten, @"@CurrentUser\b", "@Rls_CurrentUser", RegexOptions.IgnoreCase);
        rewritten = Regex.Replace(rewritten, @"@CurrentUserId\b", "@Rls_CurrentUserId", RegexOptions.IgnoreCase);

        if (dynamicParams != null)
        {
            foreach (var key in dynamicParams.Keys)
            {
                rewritten = Regex.Replace(rewritten, $@"@{key}\b", $"@Rls_{key}", RegexOptions.IgnoreCase);
            }
        }
        return rewritten;
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@")) return email;
        var parts = email.Split('@', 2);
        var local = parts[0];
        var domain = parts[1];
        if (local.Length <= 2) return "*@" + domain;
        return local[0] + new string('*', local.Length - 2) + local[^1] + "@" + domain;
    }

    private static string MaskGeneric(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length <= 4) return new string('*', text.Length);
        return text[..2] + new string('*', text.Length - 4) + text[^2..];
    }
}
