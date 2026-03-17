// ファイル概要: フォーム入力値を列定義に基づいて型変換・バリデーションします。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Globalization;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public interface IValueConverter
{
    bool TryConvert(string? input, ColumnDefinition column, out object? value, out string? error);
}

public class ValueConverter : IValueConverter
{
    public bool TryConvert(string? input, ColumnDefinition column, out object? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            if (column.Required && !column.Identity)
            {
                error = "Required";
                return false;
            }

            return true;
        }

        switch (column.Type.ToLowerInvariant())
        {
            case "int":
                if (int.TryParse(input, out var i))
                {
                    value = i;
                    return true;
                }

                error = "Invalid integer";
                return false;

            case "long":
                if (long.TryParse(input, out var l))
                {
                    value = l;
                    return true;
                }

                error = "Invalid long";
                return false;

            case "decimal":
                if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }

                error = "Invalid decimal";
                return false;

            case "double":
                if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
                {
                    value = db;
                    return true;
                }

                error = "Invalid double";
                return false;

            case "datetime":
            case "date":
                if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    value = dt;
                    return true;
                }

                error = "Invalid date";
                return false;

            case "bool":
                if (bool.TryParse(input, out var b))
                {
                    value = b;
                    return true;
                }

                if (input == "on")
                {
                    value = true;
                    return true;
                }

                error = "Invalid boolean";
                return false;

            // ファイル・画像アップロード型：ファイルパスまたは URL 文字列として扱う
            case "file":
            case "image":
                value = input;
                return true;

            // リッチテキスト・Markdown 型：文字列として扱う
            case "richtext":
            case "markdown":
                value = input;
                return true;

            // 自動完成・タグ入力型：文字列（CSV または JSON）として扱う
            case "autocomplete":
            case "tags":
                value = input;
                return true;

            // パーセント入力：数値として扱う
            case "percent":
                if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                {
                    value = p;
                    return true;
                }
                error = "Invalid percent value";
                return false;

            // 選択系・日付拡張・コード編集・特殊入力：文字列（CSV または JSON）として扱う
            case "checkbox-group":
            case "switch-group":
            case "datetime-range":
            case "code":
            case "json":
            case "signature":
            case "map":
            case "sortable-list":
                value = input;
                return true;

            default:
                value = input;
                return true;
        }
    }
}
