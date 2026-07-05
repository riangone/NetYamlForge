using System.Text.RegularExpressions;

namespace NetYamlForge.Services.Hooks;

/// <summary>フック内でSQL識別子（テーブル名・列名）を検証するための共通正規表現。</summary>
internal static class HookConstants
{
    public static readonly Regex HookIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
}
