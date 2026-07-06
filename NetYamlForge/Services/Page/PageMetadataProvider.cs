// ファイル概要: プロジェクトの pages/*.yaml を読み込み、カスタムページ定義を提供します。

using System.Diagnostics.CodeAnalysis;
using NetYamlForge.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public interface IPageMetadataProvider
{
    bool TryGet(string pageName, [NotNullWhen(true)] out PageDefinition? page);
    IReadOnlyDictionary<string, PageDefinition> GetAll();
}

public class PageMetadataProvider : IPageMetadataProvider
{
    private readonly Dictionary<string, PageDefinition> _pages;

    public PageMetadataProvider(string projectDir)
    {
        _pages = new Dictionary<string, PageDefinition>(StringComparer.OrdinalIgnoreCase);
        var pagesDir = Path.Combine(projectDir, "pages");
        if (!Directory.Exists(pagesDir)) return;
        var loadErrors = new List<string>();

        // pages/*.yaml は entities/*.yml と同じ camelCase 規約を使用します。
        // 旧 snake_case キー（source_type, page_size 等）は廃止。camelCase（sourceType, pageSize）を使用してください。
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SectionColumnsConverter())
            .WithTypeConverter(new SectionHooksConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var file in Directory.GetFiles(pagesDir).Where(f =>
            f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var yaml = File.ReadAllText(file);
                YamlSchemaValidator.ValidateUiPageYaml(yaml, file);
                var pageName = Path.GetFileNameWithoutExtension(file);
                var page = deserializer.Deserialize<PageDefinition>(yaml);
                page.Id = pageName;
                _pages[pageName] = page;
            }
            catch (Exception ex)
            {
                loadErrors.Add($"- {Path.GetFileName(file)}: {ex}");
            }
        }

        if (loadErrors.Count > 0)
        {
            var details = string.Join(Environment.NewLine, loadErrors);
            throw new InvalidOperationException(
                $"pages/*.yaml の読み込みに失敗しました。以下を修正してください。{Environment.NewLine}{details}");
        }
    }

    public bool TryGet(string pageName, [NotNullWhen(true)] out PageDefinition? page) =>
        _pages.TryGetValue(pageName, out page);

    public IReadOnlyDictionary<string, PageDefinition> GetAll() => _pages;
}

/// <summary>
/// pages/*.yaml の columns フィールドをシーケンス/マッピング両形式でデシリアライズするコンバーター。
/// - シーケンス形式: columns: [id, name, status]   → {id:{}, name:{}, status:{}}
/// - マッピング形式: columns: {id: {label: ID}, ...} → そのまま
/// </summary>
public class SectionColumnsConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) =>
        type == typeof(Dictionary<string, SectionColumnDef>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var result = new Dictionary<string, SectionColumnDef>(StringComparer.OrdinalIgnoreCase);

        if (parser.TryConsume<SequenceStart>(out _))
        {
            // リスト形式: columns: [id, name, status]
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                var scalar = parser.Consume<Scalar>();
                result[scalar.Value] = new SectionColumnDef();
            }
        }
        else if (parser.TryConsume<MappingStart>(out _))
        {
            // 辞書形式: columns: {id: {label: ID}, name: {label: 名前}}
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                // null スカラー（空マッピング値）のとき MappingStart がないのでチェック
                if (parser.Current is Scalar { Value: "" or "~" or "null" })
                {
                    parser.MoveNext();
                    result[key] = new SectionColumnDef();
                }
                else
                {
                    result[key] = (SectionColumnDef?)rootDeserializer(typeof(SectionColumnDef))
                                  ?? new SectionColumnDef();
                }
            }
        }

        return result;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException("SectionColumnsConverter は読み取り専用です。");
}

/// <summary>
/// pages/*.yaml の hooks フィールドをデシリアライズするコンバーター。
/// camelCase キー（beforeCreate / afterCreate 等）が正規形式。entities YAML と統一。
/// 旧 snake_case (before_create) も後方互換として受け付ける。
/// 各値は単一文字列またはリスト両形式をサポート。
/// presets キーで @presetName 形式の再利用フックリストを定義可能（entities と統一）。
/// </summary>
public class SectionHooksConverter : IYamlTypeConverter
{
    // camelCase / snake_case → プロパティ名の正規化マップ（後方互換）
    private static readonly Dictionary<string, string> _keyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["beforecreate"]  = nameof(SectionHooksDefinition.BeforeCreate),
        ["aftercreate"]   = nameof(SectionHooksDefinition.AfterCreate),
        ["beforeupdate"]  = nameof(SectionHooksDefinition.BeforeUpdate),
        ["afterupdate"]   = nameof(SectionHooksDefinition.AfterUpdate),
        ["beforedelete"]  = nameof(SectionHooksDefinition.BeforeDelete),
        ["afterdelete"]   = nameof(SectionHooksDefinition.AfterDelete),
    };

    public bool Accepts(Type type) => type == typeof(SectionHooksDefinition);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var result = new SectionHooksDefinition();
        if (!parser.TryConsume<MappingStart>(out _)) return result;

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            var normalizedKey = key.Replace("_", "").ToLowerInvariant();

            if (normalizedKey == "presets")
            {
                // presets: { presetName: [hook1, hook2], ... }
                var presets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (parser.TryConsume<MappingStart>(out _))
                {
                    while (!parser.TryConsume<MappingEnd>(out _))
                    {
                        var presetName = parser.Consume<Scalar>().Value;
                        presets[presetName] = ReadHookList(parser);
                    }
                }
                result.Presets = presets;
                continue;
            }

            var hookList = ReadHookList(parser);

            if (_keyMap.TryGetValue(normalizedKey, out var propName))
            {
                PropertyAccessorCache.SetValue(result, propName, hookList);
            }
        }

        return result;
    }

    private static List<string> ReadHookList(IParser parser)
    {
        if (parser.TryConsume<SequenceStart>(out _))
        {
            var list = new List<string>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                list.Add(parser.Consume<Scalar>().Value);
            return list;
        }
        if (parser.Current is Scalar { Value: "" or "~" or "null" })
        {
            parser.MoveNext();
            return new List<string>();
        }
        return new List<string> { parser.Consume<Scalar>().Value };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException("SectionHooksConverter は読み取り専用です。");
}

/// <summary>ページが存在しないプロジェクト向けの Null オブジェクト</summary>
public class NullPageMetadataProvider : IPageMetadataProvider
{
    public static readonly NullPageMetadataProvider Instance = new();
    private NullPageMetadataProvider() { }
    public bool TryGet(string pageName, [NotNullWhen(true)] out PageDefinition? page)
    {
        page = null;
        return false;
    }
    public IReadOnlyDictionary<string, PageDefinition> GetAll() =>
        new Dictionary<string, PageDefinition>();
}
