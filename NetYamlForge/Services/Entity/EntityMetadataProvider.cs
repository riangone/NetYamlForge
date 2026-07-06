// ファイル概要: YAML定義からエンティティメタデータを読み込み提供します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Diagnostics.CodeAnalysis;
using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IEntityMetadataProvider
{
    EntityDefinition Get(string entityName);
    IReadOnlyDictionary<string, EntityDefinition> GetAll();
    bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition);
}

public class EntityMetadataProvider : IEntityMetadataProvider
{
    private readonly Dictionary<string, EntityDefinition> _entities;
    private readonly List<string> _loadErrors;
    private readonly IEntityMetadataParser _parser;
    private readonly IEntityMetadataValidator _validator;

    public EntityMetadataProvider(IWebHostEnvironment env, IConfiguration configuration)
        : this(env, configuration, null, null)
    {
    }

    public EntityMetadataProvider(IWebHostEnvironment env, IConfiguration configuration, IEntityMetadataParser? parser, IEntityMetadataValidator? validator)
    {
        _parser = parser ?? new EntityMetadataParser();
        _validator = validator ?? new EntityMetadataValidator();
        _entities = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        _loadErrors = new List<string>();

        var provider = (configuration["DatabaseProvider"] ?? "sqlite").ToLowerInvariant();
        var defaultDir = Path.Combine(env.ContentRootPath, "config", "entities");
        var generatedDir = Path.Combine(env.ContentRootPath, "config", "entities.generated");

        LoadDirectory(generatedDir, skipExisting: false);

        if (provider != "sqlite")
        {
            var providerDir = Path.Combine(env.ContentRootPath, "config", $"entities-{provider}");
            LoadDirectory(providerDir, skipExisting: false);
        }

        LoadDirectory(defaultDir, skipExisting: false);

        if (_entities.Count == 0)
        {
            var fallback = Path.Combine(env.ContentRootPath, "config", "entities.yml");
            if (!File.Exists(fallback))
            {
                throw new FileNotFoundException("No entity yaml found", fallback);
            }

            try
            {
                var yaml = File.ReadAllText(fallback);
                YamlSchemaValidator.ValidateEntityYaml(yaml, fallback);
                var root = _parser.Parse(yaml, fallback);
                _validator.Validate(root, fallback);
                MergeEntities(root.Entities, skipExisting: false);
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"{fallback}: {ex.Message}");
            }
        }

        ThrowIfLoadErrors();
    }

    public EntityMetadataProvider(string projectDir, string databaseProvider)
        : this(projectDir, databaseProvider, null, null)
    {
    }

    public EntityMetadataProvider(string projectDir, string databaseProvider, IEntityMetadataParser? parser, IEntityMetadataValidator? validator)
    {
        _parser = parser ?? new EntityMetadataParser();
        _validator = validator ?? new EntityMetadataValidator();
        _entities = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        _loadErrors = new List<string>();

        var provider = (databaseProvider ?? "sqlite").ToLowerInvariant();
        var generatedDir = Path.Combine(projectDir, "entities.generated");

        LoadDirectory(generatedDir, skipExisting: false);

        if (provider != "sqlite")
        {
            var providerDir = Path.Combine(projectDir, $"entities-{provider}");
            LoadDirectory(providerDir, skipExisting: false);
        }

        LoadDirectory(Path.Combine(projectDir, "entities"), skipExisting: false);

        if (_entities.Count == 0)
        {
            var fallback = Path.Combine(projectDir, "entities.yml");
            if (File.Exists(fallback))
            {
                try
                {
                    var yaml = File.ReadAllText(fallback);
                    YamlSchemaValidator.ValidateEntityYaml(yaml, fallback);
                    var root = _parser.Parse(yaml, fallback);
                    _validator.Validate(root, fallback);
                    MergeEntities(root.Entities, skipExisting: false);
                }
                catch (Exception ex)
                {
                    _loadErrors.Add($"{fallback}: {ex.Message}");
                }
            }
        }

        ThrowIfLoadErrors();
    }

    private void LoadDirectory(string dir, bool skipExisting, int depth = 0)
    {
        if (!Directory.Exists(dir) || depth > 5) return;

        foreach (var file in Directory.GetFiles(dir, "*.yml").OrderBy(x => x))
        {
            EntityConfigRoot? root;
            try
            {
                var yaml = File.ReadAllText(file);
                YamlSchemaValidator.ValidateEntityYaml(yaml, file);
                root = _parser.Parse(yaml, file);
                _validator.Validate(root, file);
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"{file}: {ex.Message}");
                continue;
            }

            foreach (var importPath in root.Imports)
            {
                var importFile = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(file)!, importPath));
                if (File.Exists(importFile))
                {
                    try
                    {
                        var importYaml = File.ReadAllText(importFile);
                        YamlSchemaValidator.ValidateEntityYaml(importYaml, importFile);
                        var importRoot = _parser.Parse(importYaml, importFile);
                        _validator.Validate(importRoot, importFile);
                        MergeEntities(importRoot.Entities, skipExisting);
                    }
                    catch (Exception ex)
                    {
                        _loadErrors.Add($"{importFile}: {ex.Message}");
                    }
                }
            }

            MergeEntities(root.Entities, skipExisting);
        }
    }

    private void ThrowIfLoadErrors()
    {
        if (_loadErrors.Count == 0) return;

        var details = string.Join(Environment.NewLine, _loadErrors.Select(e => $"- {e}"));
        throw new InvalidOperationException(
            $"エンティティ定義 YAML の読み込みに失敗しました。以下を修正してください。{Environment.NewLine}{details}");
    }

    private void MergeEntities(Dictionary<string, EntityDefinition> entities, bool skipExisting)
    {
        foreach (var entity in entities)
        {
            _parser.Normalize(entity.Value);
            if (!skipExisting || !_entities.ContainsKey(entity.Key))
            {
                _entities[entity.Key] = entity.Value;
            }
        }
    }

    public EntityDefinition Get(string entityName) => _entities[entityName];

    public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _entities;

    public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition) =>
        _entities.TryGetValue(entityName, out definition);
}
