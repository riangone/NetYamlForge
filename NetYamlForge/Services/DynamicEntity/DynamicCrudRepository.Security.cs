using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public partial class DynamicCrudRepository
{
    private async Task<IReadOnlyList<string>> GetCurrentUserRolesAsync()
    {
        return await _rls.GetCurrentUserRolesAsync();
    }

    private async Task ApplyRowLevelSecurityAsync(EntityDefinition meta, List<string> where, DynamicParameters param)
    {
        await _rls.ApplyRowLevelSecurityAsync(meta, where, param);
    }

    private async Task EnsurePermissionAsync(EntityDefinition meta, string action)
    {
        await _rls.EnsurePermissionAsync(meta, action);
    }

    private async Task VerifyFieldWritePermissionsAsync(EntityDefinition meta, IDictionary<string, object?> values)
    {
        await _rls.VerifyFieldWritePermissionsAsync(meta, values);
    }

    private async Task<dynamic> ApplyFieldSecurityAsync(EntityDefinition meta, dynamic row)
    {
        return await _rls.ApplyFieldSecurityAsync(meta, row);
    }
}
