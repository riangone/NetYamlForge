using NetYamlForge.Services;
using NetYamlForge.Models;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityListResponseServiceTests
{
    [Fact]
    public async Task LoadFirstPageAfterMutationAsync_ReturnsItemsAndCount_WhenIncludeCountTrue()
    {
        var repo = new FakeRepo
        {
            Items = new List<dynamic> { new { Id = 1 }, new { Id = 2 } },
            CountValue = 42
        };
        var sut = new DynamicEntityListResponseService(repo);

        var result = await sut.LoadFirstPageAfterMutationAsync("customer", includeCount: true);

        Assert.Equal(2, result.Items.Count());
        Assert.Equal(42, result.Total);
        Assert.Equal(1, repo.CountCallCount);
    }

    [Fact]
    public async Task LoadFirstPageAfterMutationAsync_SkipsCount_WhenIncludeCountFalse()
    {
        var repo = new FakeRepo
        {
            Items = new List<dynamic> { new { Id = 1 } },
            CountValue = 99
        };
        var sut = new DynamicEntityListResponseService(repo);

        var result = await sut.LoadFirstPageAfterMutationAsync("customer", includeCount: false);

        Assert.Single(result.Items);
        Assert.Equal(-1, result.Total);
        Assert.Equal(0, repo.CountCallCount);
    }

    private sealed class FakeRepo : IDynamicCrudRepository
    {
        public List<dynamic> Items { get; set; } = new();
        public int CountValue { get; set; }
        public int CountCallCount { get; private set; }

        public Task<IEnumerable<dynamic>> GetAllAsync(string entity, string? search, string? sort, string? dir, Dictionary<string, string?>? filters = null, int page = 1, int? pageSize = null, string? cursor = null, bool keyset = false, bool fetchOneExtra = false)
            => Task.FromResult<IEnumerable<dynamic>>(Items);

        public Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
        {
            CountCallCount++;
            return Task.FromResult(CountValue);
        }

        public Task<dynamic?> GetByIdAsync(string entity, object id) => throw new NotImplementedException();
        public Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues) => throw new NotImplementedException();
        public Task<int> InsertAsync(string entity, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, object id, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, System.Data.IDbTransaction? tx = null) => throw new NotImplementedException();
        public Task<IEnumerable<dynamic>> GetAllForEntityAsync(string entity, ForeignKeyDefinition? foreignKey = null, string? search = null, int page = 1, int? pageSize = null, bool fetchOneExtra = false) => throw new NotImplementedException();
    }
}

