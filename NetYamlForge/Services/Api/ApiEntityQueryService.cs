using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetYamlForge.Models;
using NetYamlForge.Controllers;

namespace NetYamlForge.Services.Api;

public class ApiEntityQueryService
{
    private readonly IDynamicCrudRepository _repo;
    private readonly IEntityMetadataProvider _meta;

    public ApiEntityQueryService(IDynamicCrudRepository repo, IEntityMetadataProvider meta)
    {
        _repo = repo;
        _meta = meta;
    }

    public async Task<ApiListResponse> GetListAsync(
        string entity,
        string? search,
        string? sort,
        string? dir,
        int page,
        int pageSize,
        Dictionary<string, string?>? filters,
        EntityDefinition meta)
    {
        var filterDict = filters ?? new Dictionary<string, string?>();

        var items = await _repo.GetAllAsync(
            entity:   entity,
            search:   search,
            sort:     sort,
            dir:      dir ?? "asc",
            filters:  filterDict,
            page:     page,
            pageSize: pageSize);

        var total = await _repo.CountAsync(entity, search, filterDict);

        var data = items.Select(item => ApiDtoMapper.ToApiDto((IDictionary<string, object?>)item, meta)).ToList();

        return new ApiListResponse
        {
            Data       = data,
            Page       = page,
            PageSize   = pageSize,
            Total      = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public ApiEntityMeta GetMeta(string entity, EntityDefinition meta)
    {
        var columns = meta.Columns.ToDictionary(
            kv => kv.Key,
            kv => new ApiColumnMeta
            {
                Type     = kv.Value.Type,
                Label    = kv.Value.Label ?? string.Empty,
                Required = kv.Value.Required,
                Editable = kv.Value.Editable,
                Identity = kv.Value.Identity,
                Options  = kv.Value.Options ?? new List<string>()
            });

        var forms = meta.Forms.ToDictionary(
            kv => kv.Key,
            kv => new ApiFormMeta
            {
                Type     = kv.Value.Type,
                Label    = kv.Value.Label ?? string.Empty,
                Required = kv.Value.Required,
                Editable = kv.Value.Editable,
                Options  = kv.Value.Options ?? new List<string>()
            });

        return new ApiEntityMeta
        {
            Entity          = entity,
            Table           = meta.Table,
            DisplayName     = meta.DisplayName,
            PrimaryKeyColumns = meta.GetPrimaryKeyColumns().ToList(),
            Columns         = columns,
            Forms           = forms
        };
    }

    public async Task<ApiDto?> GetByIdAsync(string entity, string id, EntityDefinition meta)
    {
        var item = await _repo.GetByIdAsync(entity, id);
        if (item == null) return null;

        return ApiDtoMapper.ToApiDto(item, meta);
    }
}
