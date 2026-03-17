using System.Data;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityListQueryServiceTests
{
    [Fact]
    public async Task LoadAsync_SkipsCount_WhenCountDisabled()
    {
        var repo = new FakeRepo();
        var sut = new DynamicEntityListQueryService(repo, new DynamicEntityForeignKeyDataService(repo));
        var meta = CreateMeta();

        var result = await sut.LoadAsync(
            "customer",
            meta,
            search: "abc",
            sort: null,
            dir: null,
            page: 1,
            pageSize: null,
            count: "0",
            clear: null,
            cursor: null,
            query: new QueryCollection(),
            foreignKeysForForm: false);

        Assert.Equal(-1, result.Total);
        Assert.False(result.IncludeCount);
        Assert.Equal(0, repo.CountCallCount);
        Assert.Equal("abc", repo.LastSearch);
        Assert.True(repo.LastFetchOneExtra);
    }

    [Fact]
    public async Task LoadAsync_ClearsSearch_WhenClearRequested()
    {
        var repo = new FakeRepo();
        var sut = new DynamicEntityListQueryService(repo, new DynamicEntityForeignKeyDataService(repo));
        var meta = CreateMeta();

        var result = await sut.LoadAsync(
            "customer",
            meta,
            search: "abc",
            sort: null,
            dir: null,
            page: 1,
            pageSize: null,
            count: "true",
            clear: "1",
            cursor: null,
            query: new QueryCollection(),
            foreignKeysForForm: false);

        Assert.Null(result.EffectiveSearch);
        Assert.Null(repo.LastSearch);
    }

    [Fact]
    public async Task LoadAsync_LoadsFormOrFilterForeignKeys_ByFlag()
    {
        var repoForFilter = new FakeRepo();
        var sutForFilter = new DynamicEntityListQueryService(repoForFilter, new DynamicEntityForeignKeyDataService(repoForFilter));
        var meta = CreateMeta();

        var filterResult = await sutForFilter.LoadAsync(
            "customer",
            meta,
            search: null,
            sort: null,
            dir: null,
            page: 1,
            pageSize: null,
            count: "true",
            clear: null,
            cursor: null,
            query: new QueryCollection(),
            foreignKeysForForm: false);

        Assert.Contains("CategoryFk", filterResult.ForeignKeyData.Keys);
        Assert.DoesNotContain("RegionFk", filterResult.ForeignKeyData.Keys);
        Assert.Contains("category", repoForFilter.ForeignKeyEntitiesRequested);
        Assert.DoesNotContain("region", repoForFilter.ForeignKeyEntitiesRequested);

        var repoForForm = new FakeRepo();
        var sutForForm = new DynamicEntityListQueryService(repoForForm, new DynamicEntityForeignKeyDataService(repoForForm));
        var formResult = await sutForForm.LoadAsync(
            "customer",
            meta,
            search: null,
            sort: null,
            dir: null,
            page: 1,
            pageSize: null,
            count: "true",
            clear: null,
            cursor: null,
            query: new QueryCollection(),
            foreignKeysForForm: true);

        Assert.Contains("RegionFk", formResult.ForeignKeyData.Keys);
        Assert.DoesNotContain("CategoryFk", formResult.ForeignKeyData.Keys);
        Assert.Contains("region", repoForForm.ForeignKeyEntitiesRequested);
        Assert.DoesNotContain("category", repoForForm.ForeignKeyEntitiesRequested);
    }

    private static EntityDefinition CreateMeta()
    {
        return new EntityDefinition
        {
            Table = "Customer",
            Key = "Id",
            DisplayName = "Customer",
            Paging = new PagingDefinition { PageSize = 7, Mode = "numbered" },
            Filters = new Dictionary<string, FilterDefinition>
            {
                ["CategoryFk"] = new()
                {
                    Type = "dropdown",
                    ForeignKey = new ForeignKeyDefinition { Entity = "category", DisplayColumn = "Name" }
                }
            },
            Forms = new Dictionary<string, FormDefinition>
            {
                ["RegionFk"] = new()
                {
                    Type = "dropdown",
                    ForeignKey = new ForeignKeyDefinition { Entity = "region", DisplayColumn = "Name" }
                }
            }
        };
    }

    private sealed class FakeRepo : IDynamicCrudRepository
    {
        public int CountCallCount { get; private set; }
        public string? LastSearch { get; private set; }
        public bool LastFetchOneExtra { get; private set; }
        public List<string> ForeignKeyEntitiesRequested { get; } = new();

        public Task<IEnumerable<dynamic>> GetAllAsync(string entity, string? search, string? sort, string? dir, Dictionary<string, string?>? filters = null, int page = 1, int? pageSize = null, string? cursor = null, bool keyset = false, bool fetchOneExtra = false)
        {
            LastSearch = search;
            LastFetchOneExtra = fetchOneExtra;
            return Task.FromResult<IEnumerable<dynamic>>(new List<dynamic>());
        }

        public Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
        {
            CountCallCount++;
            return Task.FromResult(0);
        }

        public Task<IEnumerable<dynamic>> GetAllForEntityAsync(string entity, ForeignKeyDefinition? foreignKey = null, string? search = null, int page = 1, int? pageSize = null, bool fetchOneExtra = false)
        {
            ForeignKeyEntitiesRequested.Add(entity);
            return Task.FromResult<IEnumerable<dynamic>>(new List<dynamic>());
        }

        public Task<dynamic?> GetByIdAsync(string entity, object id) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues) => throw new NotImplementedException();
        public Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null) => throw new NotImplementedException();
    }
}
