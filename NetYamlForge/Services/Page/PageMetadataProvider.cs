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

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new SectionColumnsConverter())
            .WithTypeConverter(new SectionHooksConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        foreach (var file in Directory.GetFiles(pagesDir, "*.yaml"))
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
                loadErrors.Add($"- {Path.GetFileName(file)}: {ex.Message}");
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
/// pages/*.yaml の hooks フィールドを camelCase キーでデシリアライズするコンバーター。
/// entities YAML と同様に beforeCreate / afterCreate 等の camelCase キーを使用します。
/// snake_case (before_create) も受け付けます（大文字小文字・アンダースコア無視）。
/// 各値は単一文字列またはリスト両形式をサポートします。
/// </summary>
public class SectionHooksConverter : IYamlTypeConverter
{
    // camelCase / snake_case → プロパティセッター名の正規化マップ
    private static readonly Dictionary<string, string> _keyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["beforecreate"]  = nameof(SectionHooksDefinition.BeforeCreate),
        ["before_create"] = nameof(SectionHooksDefinition.BeforeCreate),
        ["aftercreate"]   = nameof(SectionHooksDefinition.AfterCreate),
        ["after_create"]  = nameof(SectionHooksDefinition.AfterCreate),
        ["beforeupdate"]  = nameof(SectionHooksDefinition.BeforeUpdate),
        ["before_update"] = nameof(SectionHooksDefinition.BeforeUpdate),
        ["afterupdate"]   = nameof(SectionHooksDefinition.AfterUpdate),
        ["after_update"]  = nameof(SectionHooksDefinition.AfterUpdate),
        ["beforedelete"]  = nameof(SectionHooksDefinition.BeforeDelete),
        ["before_delete"] = nameof(SectionHooksDefinition.BeforeDelete),
        ["afterdelete"]   = nameof(SectionHooksDefinition.AfterDelete),
        ["after_delete"]  = nameof(SectionHooksDefinition.AfterDelete),
    };

    public bool Accepts(Type type) => type == typeof(SectionHooksDefinition);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var result = new SectionHooksDefinition();
        if (!parser.TryConsume<MappingStart>(out _)) return result;

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;

            // 値を List<string> としてパース（単一文字列 or シーケンス）
            List<string> hookList;
            if (parser.TryConsume<SequenceStart>(out _))
            {
                hookList = new List<string>();
                while (!parser.TryConsume<SequenceEnd>(out _))
                    hookList.Add(parser.Consume<Scalar>().Value);
            }
            else if (parser.Current is Scalar { Value: "" or "~" or "null" })
            {
                parser.MoveNext();
                hookList = new List<string>();
            }
            else
            {
                hookList = new List<string> { parser.Consume<Scalar>().Value };
            }

            // 正規化キーでプロパティに設定
            var normalizedKey = key.Replace("_", "").ToLowerInvariant();
            if (_keyMap.TryGetValue(normalizedKey, out var propName) ||
                _keyMap.TryGetValue(key, out propName))
            {
                var prop = typeof(SectionHooksDefinition).GetProperty(propName);
                prop?.SetValue(result, hookList);
            }
        }

        return result;
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
