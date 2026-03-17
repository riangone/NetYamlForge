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

    /// <summary>
    /// ASP.NET Core DI から呼ばれる既存コンストラクタ（後方互換）。
    /// config/ ディレクトリのエンティティを読み込みます。
    /// </summary>
    public EntityMetadataProvider(IWebHostEnvironment env, IConfiguration configuration)
    {
        var deserializer = BuildDeserializer();
        _entities = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        _loadErrors = new List<string>();

        var provider = (configuration["DatabaseProvider"] ?? "sqlite").ToLowerInvariant();
        var defaultDir = Path.Combine(env.ContentRootPath, "config", "entities");
        var generatedDir = Path.Combine(env.ContentRootPath, "config", "entities.generated");

        // generated をベースとして先に読み込みます。
        LoadDirectory(deserializer, generatedDir, skipExisting: false);

        // プロバイダー固有ディレクトリを先に読み込み（例: entities-sqlserver/）
        if (provider != "sqlite")
        {
            var providerDir = Path.Combine(env.ContentRootPath, "config", $"entities-{provider}");
            LoadDirectory(deserializer, providerDir, skipExisting: false);
        }

        // デフォルト entities/ は generated / provider の定義を上書き可能とします
        LoadDirectory(deserializer, defaultDir, skipExisting: false);

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
                var root = deserializer.Deserialize<EntityConfigRoot>(yaml);
                ValidateEntityConfigRoot(root, fallback);
                MergeEntities(root.Entities, skipExisting: false);
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"{fallback}: {ex.Message}");
            }
        }

        ThrowIfLoadErrors();
    }

    /// <summary>
    /// ProjectManager から呼ばれるコンストラクタ。
    /// projectDir 配下の entities[-{provider}]/ ディレクトリを読み込みます。
    /// </summary>
    public EntityMetadataProvider(string projectDir, string databaseProvider)
    {
        var deserializer = BuildDeserializer();
        _entities = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        _loadErrors = new List<string>();

        var provider = (databaseProvider ?? "sqlite").ToLowerInvariant();
        var generatedDir = Path.Combine(projectDir, "entities.generated");

        // generated をベースとして先に読み込みます。
        LoadDirectory(deserializer, generatedDir, skipExisting: false);

        // プロバイダー固有ディレクトリを先に読み込み
        if (provider != "sqlite")
        {
            var providerDir = Path.Combine(projectDir, $"entities-{provider}");
            LoadDirectory(deserializer, providerDir, skipExisting: false);
        }

        // デフォルト entities/ は generated / provider の定義を上書き可能とします
        LoadDirectory(deserializer, Path.Combine(projectDir, "entities"), skipExisting: false);

        // フォールバック: entities.yml 単一ファイル
        if (_entities.Count == 0)
        {
            var fallback = Path.Combine(projectDir, "entities.yml");
            if (File.Exists(fallback))
            {
                try
                {
                    var yaml = File.ReadAllText(fallback);
                    YamlSchemaValidator.ValidateEntityYaml(yaml, fallback);
                    var root = deserializer.Deserialize<EntityConfigRoot>(yaml);
                    ValidateEntityConfigRoot(root, fallback);
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

    private static IDeserializer BuildDeserializer() =>
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private void LoadDirectory(IDeserializer deserializer, string dir, bool skipExisting, int depth = 0)
    {
        if (!Directory.Exists(dir) || depth > 5) return;

        foreach (var file in Directory.GetFiles(dir, "*.yml").OrderBy(x => x))
        {
            EntityConfigRoot? root;
            try
            {
                var yaml = File.ReadAllText(file);
                YamlSchemaValidator.ValidateEntityYaml(yaml, file);
                root = deserializer.Deserialize<EntityConfigRoot>(yaml);
                ValidateEntityConfigRoot(root, file);
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"{file}: {ex.Message}");
                continue;
            }

            // imports を先に処理（再帰しない: depth+1 は importsでは使わない）
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
                        var importRoot = deserializer.Deserialize<EntityConfigRoot>(importYaml);
                        ValidateEntityConfigRoot(importRoot, importFile);
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

    private static void ValidateEntityConfigRoot(EntityConfigRoot root, string filePath)
    {
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
            NormalizeEntityDefinition(entity.Value);
            if (!skipExisting || !_entities.ContainsKey(entity.Key))
            {
                _entities[entity.Key] = entity.Value;
            }
        }
    }

    private static void NormalizeEntityDefinition(EntityDefinition def)
    {
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

    public EntityDefinition Get(string entityName) => _entities[entityName];

    public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _entities;

    public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition) =>
        _entities.TryGetValue(entityName, out definition);
}
