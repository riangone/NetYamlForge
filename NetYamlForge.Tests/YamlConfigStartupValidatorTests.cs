// ファイル概要: YamlConfigStartupValidator のユニットテストです。
// 既知の列型・フィルター型セットと、起動時警告ロジックを検証します。

using NetYamlForge.Services.Validation;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// YamlConfigStartupValidator の既知型セット検証テスト。
/// </summary>
public class YamlConfigStartupValidatorTests
{
    // ── 列型 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("decimal")]
    [InlineData("boolean")]
    [InlineData("bool")]
    [InlineData("datetime")]
    [InlineData("date")]
    [InlineData("email")]
    [InlineData("textarea")]
    [InlineData("money")]
    [InlineData("color")]
    [InlineData("rating")]
    public void KnownColumnTypes_ContainsAllCommonTypes(string type)
    {
        Assert.Contains(type, YamlConfigStartupValidator.KnownColumnTypes);
    }

    [Theory]
    [InlineData("unknown_type")]
    [InlineData("xml")]
    [InlineData("blob")]
    [InlineData("")]
    public void KnownColumnTypes_DoesNotContainUnknownTypes(string type)
    {
        Assert.DoesNotContain(type, YamlConfigStartupValidator.KnownColumnTypes);
    }

    // ── フィルター型 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("string")]
    [InlineData("like")]
    [InlineData("dropdown")]
    [InlineData("boolean")]
    [InlineData("enum")]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("date-range")]
    [InlineData("range")]
    [InlineData("checkbox")]
    [InlineData("multi-select")]
    [InlineData("entity-picker")]
    [InlineData("entity-multi-picker")]
    [InlineData("toggle-group")]
    [InlineData("bool-toggle")]
    [InlineData("int")]     // 列型流用パターン
    [InlineData("decimal")] // 列型流用パターン
    [InlineData("email")]   // 列型流用パターン
    [InlineData("eq")]      // テンプレート用
    public void KnownFilterTypes_ContainsAllCommonTypes(string type)
    {
        Assert.Contains(type, YamlConfigStartupValidator.KnownFilterTypes);
    }

    [Theory]
    [InlineData("totally_unknown")]
    [InlineData("xml_filter")]
    public void KnownFilterTypes_DoesNotContainUnknownTypes(string type)
    {
        Assert.DoesNotContain(type, YamlConfigStartupValidator.KnownFilterTypes);
    }

    // ── 大文字小文字非区別 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("STRING")]
    [InlineData("INT")]
    [InlineData("DateTime")]
    public void KnownColumnTypes_IsCaseInsensitive(string type)
    {
        Assert.Contains(type, YamlConfigStartupValidator.KnownColumnTypes);
    }

    [Theory]
    [InlineData("DROPDOWN")]
    [InlineData("LIKE")]
    [InlineData("Bool-Toggle")]
    public void KnownFilterTypes_IsCaseInsensitive(string type)
    {
        Assert.Contains(type, YamlConfigStartupValidator.KnownFilterTypes);
    }
}
