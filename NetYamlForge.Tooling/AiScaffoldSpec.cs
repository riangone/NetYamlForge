// ファイル概要: --ai-scaffold コマンドの入力となる「構造化 Spec」のモデルとロード/静的検証ロジック。
//
// 背景: 「26個のAI生成子プロジェクトのうち完璧に動くものが無い」という問題への対策として、
// 自然言語 1 発でコード全体を生成させるのではなく、AI（または人間）の出力範囲を
// 「entities/hooks/batchJobs の宣言」だけに限定した構造化 Spec に強制する。
// 実際の DB スキーマ・エンティティ YAML・hook 雛形は、既存の決定的なスキャフォールダー
// （EntityYamlScaffolder / HookScaffolder / BatchJobScaffolder 等）が生成するため、
// 「AIが発明したコード」がそのまま製品に混入する経路そのものを塞いでいる。
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services.Cli;

public sealed class AiScaffoldSpec
{
    public string Project { get; set; } = "";
    public string? DisplayName { get; set; }
    public string DbType { get; set; } = "sqlite";
    public List<SpecEntity> Entities { get; set; } = new();
    public List<SpecHook> Hooks { get; set; } = new();
    public List<SpecBatchJob> BatchJobs { get; set; } = new();
    public List<string> AcceptanceCriteria { get; set; } = new();

    public static readonly HashSet<string> AllowedColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "text", "real", "numeric", "blob", "boolean", "datetime"
    };

    /// <summary>YAML ファイルから Spec を読み込む。</summary>
    public static AiScaffoldSpec Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"spec ファイルが見つかりません: {path}", path);
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var spec = deserializer.Deserialize<AiScaffoldSpec>(yaml);
        if (spec is null)
        {
            throw new InvalidDataException($"spec ファイルの内容を解析できませんでした（空、または不正な YAML）: {path}");
        }

        return spec;
    }

    /// <summary>
    /// gate #1: DB にもファイルシステムにも一切触れない、純粋な構造検証。
    /// ここで弾いたエラーはすべて「生成前」に発見されるため、生成物側のバグにならない。
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Project))
        {
            errors.Add("project は必須です。");
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(Project, "^[a-z][a-z0-9-]{1,62}$"))
        {
            errors.Add($"project 名 '{Project}' は小文字英数字とハイフンのケバブケースで指定してください（例: golden-template）。");
        }

        if (!string.Equals(DbType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"dbType '{DbType}' は --ai-scaffold では未対応です（現時点では sqlite のみ、スキーマの決定的な自動投入が可能なため）。");
        }

        if (Entities.Count == 0)
        {
            errors.Add("entities は最低 1 件必要です。");
        }

        var seenTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entities)
        {
            if (string.IsNullOrWhiteSpace(e.Table))
            {
                errors.Add("entities[].table は必須です。");
                continue;
            }

            if (!seenTables.Add(e.Table))
            {
                errors.Add($"table '{e.Table}' が重複しています。");
            }

            if (e.Columns.Count == 0)
            {
                errors.Add($"table '{e.Table}' に columns が 1 件もありません。");
                continue;
            }

            if (e.Columns.Count(c => c.PrimaryKey) == 0)
            {
                errors.Add($"table '{e.Table}' に primaryKey: true の列がありません。");
            }

            var seenCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in e.Columns)
            {
                if (string.IsNullOrWhiteSpace(c.Name))
                {
                    errors.Add($"table '{e.Table}' に name の無い列があります。");
                    continue;
                }

                if (!seenCols.Add(c.Name))
                {
                    errors.Add($"table '{e.Table}'.'{c.Name}' 列が重複しています。");
                }

                if (!AllowedColumnTypes.Contains(c.Type))
                {
                    errors.Add($"table '{e.Table}'.'{c.Name}' の type '{c.Type}' は未対応です（許可: {string.Join(", ", AllowedColumnTypes)}）。");
                }

                if (c.ForeignKey != null &&
                    (string.IsNullOrWhiteSpace(c.ForeignKey.Table) || string.IsNullOrWhiteSpace(c.ForeignKey.Column)))
                {
                    errors.Add($"table '{e.Table}'.'{c.Name}' の foreignKey には table/column の両方を指定してください。");
                }
            }
        }

        // FK 参照先テーブルが spec 内に実在するか（全テーブル収集後にチェック）
        foreach (var e in Entities)
        {
            foreach (var c in e.Columns.Where(c => c.ForeignKey != null && !string.IsNullOrWhiteSpace(c.ForeignKey!.Table)))
            {
                if (!seenTables.Contains(c.ForeignKey!.Table))
                {
                    errors.Add($"table '{e.Table}'.'{c.Name}' の foreignKey 参照先 '{c.ForeignKey.Table}' が entities に存在しません。");
                }
            }
        }

        foreach (var h in Hooks)
        {
            if (string.IsNullOrWhiteSpace(h.Name))
            {
                errors.Add("hooks[].name は必須です。");
            }
        }

        foreach (var j in BatchJobs)
        {
            if (string.IsNullOrWhiteSpace(j.Name))
            {
                errors.Add("batchJobs[].name は必須です。");
            }
        }

        return errors;
    }
}

public sealed class SpecEntity
{
    public string Table { get; set; } = "";
    public List<SpecColumn> Columns { get; set; } = new();
}

public sealed class SpecColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool NotNull { get; set; }
    public bool PrimaryKey { get; set; }
    public bool Identity { get; set; }
    public string? Default { get; set; }
    public SpecForeignKey? ForeignKey { get; set; }
}

public sealed class SpecForeignKey
{
    public string Table { get; set; } = "";
    public string Column { get; set; } = "id";
}

public sealed class SpecHook
{
    public string Name { get; set; } = "";
    public string? Entity { get; set; }
}

public sealed class SpecBatchJob
{
    public string Name { get; set; } = "";
}
