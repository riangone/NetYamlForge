using System;
using System.Collections.Generic;
using System.IO;
using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IEntityMetadataParser
{
    EntityConfigRoot Parse(string yaml, string filePath);
    void Normalize(EntityDefinition def);
}

public class EntityMetadataParser : IEntityMetadataParser
{
    private readonly IDeserializer _deserializer;

    public EntityMetadataParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public EntityConfigRoot Parse(string yaml, string filePath)
    {
        try
        {
            return _deserializer.Deserialize<EntityConfigRoot>(yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize YAML in {filePath}: {ex.Message}", ex);
        }
    }

    public void Normalize(EntityDefinition def)
    {
        if (def == null) return;

        def.Joins ??= new List<JoinDefinition>();
        def.Forms ??= new Dictionary<string, FormDefinition>();
        def.Columns ??= new Dictionary<string, ColumnDefinition>();
        def.Filters ??= new Dictionary<string, FilterDefinition>();
        def.Links ??= new Dictionary<string, EntityLinkDefinition>();
        def.Keys ??= new List<string>();
        def.Paging ??= new PagingDefinition();
        def.Layout ??= new EntityLayoutDefinition();
        def.Layout.Forms ??= new FormLayoutDefinition();
        def.Layout.Filters ??= new FilterLayoutDefinition();
        def.Layout.Forms.Order ??= new List<string>();
        def.Layout.Filters.Order ??= new List<string>();
    }
}
