using System;
using System.Collections.Generic;
using NetYamlForge.Models;
using NetYamlForge.Controllers;

namespace NetYamlForge.Services.Api;

internal static class ApiDtoMapper
{
    public static ApiDto ToApiDto(IDictionary<string, object?> item, EntityDefinition meta)
    {
        var pkCol = meta.GetPrimaryKeyColumns()[0];
        var dto   = new ApiDto
        {
            Id   = item.TryGetValue(pkCol, out var idVal) ? idVal?.ToString() : null,
            Data = new Dictionary<string, object?>()
        };

        foreach (var kv in item)
        {
            if (meta.Columns.TryGetValue(kv.Key, out var col))
                dto.Data[kv.Key] = ConvertValue(kv.Value, col.Type);
        }

        return dto;
    }

    public static object? ConvertValue(object? value, string type)
    {
        if (value == null || value == DBNull.Value) return null;
        return type.ToLowerInvariant() switch
        {
            "int" or "integer" or "long"               => Convert.ToInt64(value),
            "double" or "decimal" or "float" or "number" => Convert.ToDecimal(value),
            "bool" or "boolean"                        => Convert.ToBoolean(value),
            _                                          => value.ToString()
        };
    }
}
