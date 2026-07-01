#pragma warning disable DCS001

using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;

namespace NetYamlForge.Services.Tenant;

public class TenantQuotaValidator : ITenantQuotaValidator
{
    private readonly ProjectScope _projectScope;
    private readonly IEntityMetadataProvider _metadataProvider;
    private readonly IDbConnection _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<TenantQuotaValidator> _logger;

    public TenantQuotaValidator(
        ProjectScope projectScope,
        IEntityMetadataProvider metadataProvider,
        IDbConnection db,
        IWebHostEnvironment environment,
        ILogger<TenantQuotaValidator> logger)
    {
        _projectScope = projectScope;
        _metadataProvider = metadataProvider;
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    public Task CheckEntityCreationQuotaAsync(string tenantId)
    {
        if (_projectScope?.IsSet != true) return Task.CompletedTask;
        var quotas = _projectScope.Current.Multitenancy?.Quotas;
        if (quotas == null) return Task.CompletedTask;

        var currentCount = _metadataProvider.GetAll().Count;
        if (currentCount >= quotas.MaxEntitiesCount)
        {
            _logger.LogWarning("Tenant {TenantId} exceeded max entity definition quota ({Current}/{Max})", tenantId, currentCount, quotas.MaxEntitiesCount);
            throw new InvalidOperationException($"Tenant quota exceeded: Maximum dynamic entities count limit of {quotas.MaxEntitiesCount} reached.");
        }

        return Task.CompletedTask;
    }

    public async Task CheckDatabaseRowsQuotaAsync(string tenantId, string tableName)
    {
        if (_projectScope?.IsSet != true) return;
        var quotas = _projectScope.Current.Multitenancy?.Quotas;
        if (quotas == null) return;

        var sql = $"SELECT COUNT(*) FROM \"{tableName}\"";
        try
        {
            var count = await _db.ExecuteScalarAsync<int>(sql);
            if (count >= quotas.MaxDbRowsPerEntity)
            {
                _logger.LogWarning("Tenant {TenantId} exceeded max rows quota for table {Table} ({Current}/{Max})", tenantId, tableName, count, quotas.MaxDbRowsPerEntity);
                throw new InvalidOperationException($"Tenant quota exceeded: Maximum database rows limit of {quotas.MaxDbRowsPerEntity} reached for table '{tableName}'.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to query row count for quota check on table {Table}", tableName);
        }
    }

    public Task CheckStorageQuotaAsync(string tenantId, long incomingFileSizeBytes)
    {
        if (_projectScope?.IsSet != true) return Task.CompletedTask;
        var quotas = _projectScope.Current.Multitenancy?.Quotas;
        if (quotas == null) return Task.CompletedTask;

        var wwwRoot = _environment.WebRootPath;
        var uploadDir = Path.Combine(wwwRoot, "uploads", tenantId);
        long currentSizeBytes = 0;
        if (Directory.Exists(uploadDir))
        {
            var di = new DirectoryInfo(uploadDir);
            currentSizeBytes = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }

        if (currentSizeBytes + incomingFileSizeBytes > quotas.MaxStorageBytes)
        {
            _logger.LogWarning("Tenant {TenantId} exceeded max storage quota ({Current}/{Max}) with incoming file size {Incoming}",
                tenantId, currentSizeBytes, quotas.MaxStorageBytes, incomingFileSizeBytes);
            throw new InvalidOperationException($"Tenant quota exceeded: Maximum storage space limit of {quotas.MaxStorageBytes / 1024 / 1024}MB reached.");
        }

        return Task.CompletedTask;
    }
}
