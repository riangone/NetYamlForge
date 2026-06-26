using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class CommandErrorHttpMapperTests
{
    [Fact]
    public void IsConflict_ReturnsTrue_ForConcurrencyCode()
    {
        var sut = new CommandErrorHttpMapper();
        var error = new CommandError(CommandErrorCodes.ConcurrencyConflictOrNotFound, "x");

        var result = sut.IsConflict(error);

        Assert.True(result);
    }

    [Fact]
    public void IsConflict_ReturnsFalse_ForOtherCode()
    {
        var sut = new CommandErrorHttpMapper();
        var error = new CommandError(CommandErrorCodes.HookRejectedBeforeDelete, "x");

        var result = sut.IsConflict(error);

        Assert.False(result);
    }

    [Fact]
    public void TestSerialize()
    {
        var job = new NetYamlForge.Services.BatchJob.BatchJobDefinition { Id = "test-job-id" };
        var json = System.Text.Json.JsonSerializer.Serialize(job);
        Assert.Contains("test-job-id", json);
    }
}
