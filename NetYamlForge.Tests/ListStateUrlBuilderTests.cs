// ファイル概要: ListStateUrlBuilder のユニットテストです。
// ソート URL・ページング URL・状態 URL の生成ロジックを検証します。

using NetYamlForge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// ListStateUrlBuilder のユニットテスト。
/// フィルター・ソート・ページング状態パラメータの URL 生成を検証します。
/// </summary>
public class ListStateUrlBuilderTests
{
    // ── BuildSortUrl ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildSortUrl_NewColumn_ReturnsAscending()
    {
        // 別の列をクリック → 昇順で返す
        var url = ListStateUrlBuilder.BuildSortUrl(
            baseUrl: "/p1/DynamicEntity/ListPartial",
            entity: "invoice",
            sortColumn: "Total",
            currentSort: "CustomerName",
            currentDir: "asc",
            search: null,
            pageSize: 20);

        Assert.Contains("sort=Total", url);
        Assert.Contains("dir=asc", url);
    }

    [Fact]
    public void BuildSortUrl_SameColumnAsc_ReturnsDesc()
    {
        // 同じ列を再クリック（現在 asc）→ desc になる
        var url = ListStateUrlBuilder.BuildSortUrl(
            baseUrl: "/p1/DynamicEntity/ListPartial",
            entity: "invoice",
            sortColumn: "Total",
            currentSort: "Total",
            currentDir: "asc",
            search: null,
            pageSize: 20);

        Assert.Contains("sort=Total", url);
        Assert.Contains("dir=desc", url);
    }

    [Fact]
    public void BuildSortUrl_SameColumnDesc_ReturnsAsc()
    {
        // 同じ列を再クリック（現在 desc）→ asc になる
        var url = ListStateUrlBuilder.BuildSortUrl(
            baseUrl: "/p1/DynamicEntity/ListPartial",
            entity: "invoice",
            sortColumn: "Total",
            currentSort: "Total",
            currentDir: "desc",
            search: null,
            pageSize: 20);

        Assert.Contains("dir=asc", url);
    }

    [Fact]
    public void BuildSortUrl_PreservesSearchAndFilters()
    {
        // search と追加フィルターが URL に保持される
        var filters = new Dictionary<string, string?> { ["status"] = "active" };
        var url = ListStateUrlBuilder.BuildSortUrl(
            baseUrl: "/base",
            entity: "order",
            sortColumn: "CreatedAt",
            currentSort: null,
            currentDir: null,
            search: "test",
            pageSize: 10,
            filters: filters);

        Assert.Contains("search=test", url);
        Assert.Contains("status=active", url);
        Assert.Contains("sort=CreatedAt", url);
    }

    // ── BuildPageUrl ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPageUrl_WithPage_IncludesPageParam()
    {
        var url = ListStateUrlBuilder.BuildPageUrl(
            baseUrl: "/base",
            entity: "invoice",
            sort: "Id",
            dir: "asc",
            search: null,
            pageSize: 20,
            page: 3);

        Assert.Contains("page=3", url);
        Assert.Contains("sort=Id", url);
        Assert.Contains("dir=asc", url);
    }

    [Fact]
    public void BuildPageUrl_WithCursor_IncludesCursorParam()
    {
        var url = ListStateUrlBuilder.BuildPageUrl(
            baseUrl: "/base",
            entity: "invoice",
            sort: "Id",
            dir: "asc",
            search: null,
            pageSize: 20,
            cursor: "100");

        Assert.Contains("cursor=100", url);
        Assert.DoesNotContain("page=", url);
    }

    [Fact]
    public void BuildPageUrl_PreservesFilters()
    {
        var filters = new Dictionary<string, string?> { ["status"] = "shipped" };
        var url = ListStateUrlBuilder.BuildPageUrl(
            baseUrl: "/base",
            entity: "order",
            sort: null,
            dir: null,
            search: "abc",
            pageSize: 50,
            filters: filters);

        Assert.Contains("status=shipped", url);
        Assert.Contains("search=abc", url);
        Assert.Contains("pageSize=50", url);
    }

    // ── BuildIndexStateUrl ────────────────────────────────────────────────────

    [Fact]
    public void BuildIndexStateUrl_ExcludesPrivateParams()
    {
        // returnUrl / clear / returnEntity は state URL に含まれない
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["entity"]       = "invoice",
            ["sort"]         = "Total",
            ["dir"]          = "asc",
            ["returnUrl"]    = "/somewhere",
            ["clear"]        = "1",
            ["returnEntity"] = "customer"
        });

        var url = ListStateUrlBuilder.BuildIndexStateUrl("/base", query, "invoice");

        Assert.Contains("sort=Total", url);
        Assert.DoesNotContain("returnUrl", url);
        Assert.DoesNotContain("clear=", url);
        Assert.DoesNotContain("returnEntity", url);
    }

    [Fact]
    public void BuildIndexStateUrl_AlwaysAddsEntityAndCount()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>());
        var url = ListStateUrlBuilder.BuildIndexStateUrl("/base", query, "customer");

        Assert.Contains("entity=customer", url);
        Assert.Contains("count=true", url);
    }

    // ── ShouldResetPage ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("sort")]
    [InlineData("dir")]
    [InlineData("search")]
    [InlineData("pageSize")]
    public void ShouldResetPage_PageResetTriggers_ReturnsTrue(string param)
    {
        Assert.True(ListStateUrlBuilder.ShouldResetPage(param));
    }

    [Theory]
    [InlineData("page")]
    [InlineData("cursor")]
    [InlineData("status")]
    [InlineData("entity")]
    public void ShouldResetPage_NonTriggers_ReturnsFalse(string param)
    {
        Assert.False(ListStateUrlBuilder.ShouldResetPage(param));
    }
}
