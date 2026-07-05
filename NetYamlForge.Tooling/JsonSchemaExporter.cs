using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Cli;

public static class JsonSchemaExporter
{
    public static void Export(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        ExportSchema(typeof(EntityConfigRoot), "entities.schema.json", outputDir);
        ExportSchema(typeof(PageDefinition), "pages.schema.json", outputDir);
        ExportSchema(typeof(DashboardConfig), "dashboard.schema.json", outputDir);
        ExportSchema(typeof(ProjectConfig), "project.schema.json", outputDir);
    }

    private static void ExportSchema(Type rootType, string fileName, string outputDir)
    {
        var defs = new Dictionary<string, object>();
        var rootSchema = TypeToSchema(rootType, defs, isRoot: true);

        var finalSchema = new Dictionary<string, object>
        {
            { "$schema", "http://json-schema.org/draft-07/schema#" },
            { "type", "object" }
        };

        if (rootSchema is Dictionary<string, object> rootDict)
        {
            foreach (var kvp in rootDict)
            {
                finalSchema[kvp.Key] = kvp.Value;
            }
        }

        if (defs.Count > 0)
        {
            finalSchema["definitions"] = defs;
        }

        var json = JsonSerializer.Serialize(finalSchema, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(Path.Combine(outputDir, fileName), json);
    }

    private static object TypeToSchema(Type type, Dictionary<string, object> defs, bool isRoot = false)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return TypeToSchema(underlyingType, defs);
        }

        if (type == typeof(object)) return new Dictionary<string, object>();

        if (type == typeof(string)) return new Dictionary<string, object>
        {
            { "anyOf", new List<object>
                {
                    new Dictionary<string, object> { { "type", "string" } },
                    new Dictionary<string, object> { { "type", "number" } },
                    new Dictionary<string, object> { { "type", "integer" } },
                    new Dictionary<string, object> { { "type", "boolean" } }
                }
            }
        };
        if (type == typeof(bool)) return new Dictionary<string, object> { { "type", "boolean" } };
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return new Dictionary<string, object> { { "type", "integer" } };
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            return new Dictionary<string, object> { { "type", "number" } };

        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) ||
                                   type.GetGenericTypeDefinition() == typeof(IList<>) ||
                                   type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
        {
            var itemType = type.GetGenericArguments()[0];
            return new Dictionary<string, object>
            {
                { "type", "array" },
                { "items", TypeToSchema(itemType, defs) }
            };
        }
        if (type.IsArray)
        {
            var itemType = type.GetElementType()!;
            return new Dictionary<string, object>
            {
                { "type", "array" },
                { "items", TypeToSchema(itemType, defs) }
            };
        }

        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                                   type.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
        {
            var valType = type.GetGenericArguments()[1];
            var valSchema = TypeToSchema(valType, defs);
            return new Dictionary<string, object>
            {
                { "anyOf", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "additionalProperties", valSchema }
                        },
                        new Dictionary<string, object>
                        {
                            { "type", "array" },
                            { "items", new Dictionary<string, object>
                                {
                                    { "anyOf", new List<object>
                                        {
                                            valSchema,
                                            new Dictionary<string, object> { { "type", "string" } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        if (type.IsEnum)
        {
            var names = new List<string>();
            foreach (var val in Enum.GetNames(type))
            {
                names.Add(val.ToCamelCase());
            }
            return new Dictionary<string, object>
            {
                { "type", "string" },
                { "enum", names }
            };
        }

        if (type.IsClass)
        {
            var typeName = type.Name;
            if (!isRoot)
            {
                if (!defs.ContainsKey(typeName))
                {
                    defs[typeName] = new Dictionary<string, object>();
                    var classSchema = BuildClassSchema(type, defs);
                    defs[typeName] = classSchema;
                }
                return new Dictionary<string, object> { { "$ref", $"#/definitions/{typeName}" } };
            }
            else
            {
                return BuildClassSchema(type, defs);
            }
        }

        return new Dictionary<string, object> { { "type", "string" } };
    }

    private static Dictionary<string, object> BuildClassSchema(Type type, Dictionary<string, object> defs)
    {
        var properties = new Dictionary<string, object>();
        var requiredList = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

            var propName = prop.Name.ToCamelCase();
            var propSchema = TypeToSchema(prop.PropertyType, defs);
            properties[propName] = propSchema;

            var isNullable = Nullable.GetUnderlyingType(prop.PropertyType) != null;
            var isValueType = prop.PropertyType.IsValueType;
            var isRequiredAttr = prop.GetCustomAttribute<RequiredAttribute>() != null;

            if (isRequiredAttr)
            {
                requiredList.Add(propName);
            }
        }

        var schema = new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", properties },
            { "additionalProperties", true }
        };

        if (requiredList.Count > 0)
        {
            schema["required"] = requiredList;
        }

        return schema;
    }

    private static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str) || !char.IsUpper(str[0])) return str;
        return char.ToLower(str[0]) + str[1..];
    }
}
