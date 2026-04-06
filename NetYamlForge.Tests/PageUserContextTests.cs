using NetYamlForge.Models;
using Xunit;

namespace NetYamlForge.Tests;

public class PageUserContextTests
{
    [Fact]
    public void HasRole_ShouldBeCaseInsensitive()
    {
        var ctx = new PageUserContext("user1", "User One", "1", new[] { "Admin", "User" }, true, true);
        
        Assert.True(ctx.HasRole("admin"));
        Assert.True(ctx.HasRole("ADMIN"));
        Assert.True(ctx.HasRole("Admin"));
        Assert.False(ctx.HasRole("Manager"));
    }

    [Fact]
    public void HasAnyRole_ShouldReturnTrue_IfAnyRoleMatches()
    {
        var ctx = new PageUserContext("user1", "User One", "1", new[] { "Admin", "User" }, true, true);
        
        Assert.True(ctx.HasAnyRole(new[] { "Manager", "Admin" }));
        Assert.True(ctx.HasAnyRole(new[] { "user", "guest" }));
        Assert.False(ctx.HasAnyRole(new[] { "Manager", "Guest" }));
    }

    [Fact]
    public void Anonymous_ShouldHaveDefaultValues()
    {
        var ctx = PageUserContext.Anonymous;
        
        Assert.Empty(ctx.UserName);
        Assert.Empty(ctx.Roles);
        Assert.False(ctx.IsAdmin);
        Assert.False(ctx.IsAuthenticated);
    }
}
