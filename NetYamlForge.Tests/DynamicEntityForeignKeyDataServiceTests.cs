using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityForeignKeyDataServiceTests
{
    [Fact]
    public async Task LoadForFormAsync_LoadsOnlyForeignKeyFields()
    {
        var repo = new FakeRepo();
        var sut = new DynamicEntityForeignKeyDataService(repo);
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "OrderId",
            DisplayName = "Orders",
            Forms = new Dictionary<string, FormDefinition>
            {
                ["CustomerId"] = new() { ForeignKey = new ForeignKeyDefinition { Entity = "Customers", DisplayColumn = "Name" } },
                ["Memo"] = new()
            }
        };

        var result = await sut.LoadForFormAsync(meta);

        Assert.Single(result);
        Assert.True(result.ContainsKey("CustomerId"));
        Assert.Equal(1, repo.GetAllForEntityCallCount);
    }

    [Fact]
    public async Task LoadForFiltersAsync_LoadsOnlyForeignKeyFilters()
    {
        var repo = new FakeRepo();
        var sut = new DynamicEntityForeignKeyDataService(repo);
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "OrderId",
            DisplayName = "Orders",
            Filters = new Dictionary<string, FilterDefinition>
            {
                ["CustomerId"] = new() { ForeignKey = new ForeignKeyDefinition { Entity = "Customers", DisplayColumn = "Name" } },
                ["Status"] = new()
            }
        };

        var result = await sut.LoadForFiltersAsync(meta);

        Assert.Single(result);
        Assert.True(result.ContainsKey("CustomerId"));
        Assert.Equal(1, repo.GetAllForEntityCallCount);
    }

    private sealed class FakeRepo : IDynamicCrudRepository
    {
        public int GetAllForEntityCallCount { get; private set; }
        public Task<IEnumerable<dynamic>> GetAllForEntityAsync(string entity, ForeignKeyDefinition? foreignKey = null, string? search = null, int page = 1, int? pageSize = null, bool fetchOneExtra = false)
        {
            GetAllForEntityCallCount++;
            return Task.FromResult<IEnumerable<dynamic>>(new List<dynamic> { new { Id = 1, Name = "A" } });
        }

        public Task<IEnumerable<dynamic>> GetAllAsync(string entity, string? search, string? sort, string? dir, Dictionary<string, string?>? filters = null, int page = 1, int? pageSize = null, string? cursor = null, bool keyset = false, bool fetchOneExtra = false) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, object id) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues) => throw new NotImplementedException();
        public Task<int> InsertAsync(string entity, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, object id, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null) => throw new NotImplementedException();
    }
}

