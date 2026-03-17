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
}
