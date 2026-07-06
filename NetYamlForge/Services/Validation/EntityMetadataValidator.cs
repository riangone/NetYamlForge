using System;
using System.Collections.Generic;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public interface IEntityMetadataValidator
{
    void Validate(EntityConfigRoot root, string filePath);
}

public class EntityMetadataValidator : IEntityMetadataValidator
{
    public void Validate(EntityConfigRoot root, string filePath)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (root.Entities == null || root.Entities.Count == 0)
        {
            return;
        }

        var errors = new List<string>();
        foreach (var entry in root.Entities)
        {
            var entityName = entry.Key;
            var def = entry.Value;

            if (string.IsNullOrWhiteSpace(entityName))
            {
                errors.Add("entities のキーが空です。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(def.Table))
            {
                errors.Add($"entities.{entityName}.table は必須です。");
            }

            var hasPrimaryKey = !string.IsNullOrWhiteSpace(def.Key) || (def.Keys?.Count ?? 0) > 0;
            if (!hasPrimaryKey)
            {
                errors.Add($"entities.{entityName}.key または entities.{entityName}.keys は必須です。");
            }

            if (string.IsNullOrWhiteSpace(def.DisplayName) && string.IsNullOrWhiteSpace(def.DisplayNameKey))
            {
                errors.Add($"entities.{entityName}.displayName または entities.{entityName}.displayNameKey は必須です。");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"{filePath}: " + string.Join(" ", errors));
        }
    }
}
