using NetYamlForge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityListHttpResponseServiceTests
{
    [Fact]
    public void SetEntityFormSavedHeaders_SetsRetargetAndTrigger()
    {
        var sut = new DynamicEntityListHttpResponseService();
        var context = new DefaultHttpContext();

        sut.SetEntityFormSavedHeaders(context.Response);

        Assert.Equal("#list-container", context.Response.Headers["HX-Retarget"].ToString());
        Assert.Equal("entity-form-saved", context.Response.Headers["HX-Trigger"].ToString());
    }

    [Fact]
    public void TrySetPushUrl_SetsHeader_WhenBaseUrlExists()
    {
        var sut = new DynamicEntityListHttpResponseService();
        var context = new DefaultHttpContext();
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["search"] = "abc",
            ["entity"] = "customer"
        });

        sut.TrySetPushUrl(context.Request, context.Response, "/DynamicEntity/Index", query, "customer", "/back");

        var header = context.Response.Headers["HX-Push-Url"].ToString();
        Assert.Contains("entity=customer", header);
        Assert.Contains("search=abc", header);
    }

    [Fact]
    public void TrySetPushUrl_UsesReplace_WhenTriggerIsListContainer()
    {
        var sut = new DynamicEntityListHttpResponseService();
        var context = new DefaultHttpContext();
        context.Request.Headers["HX-Trigger"] = "list-container";
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["entity"] = "customer" });

        sut.TrySetPushUrl(context.Request, context.Response, "/Index", query, "customer", null);

        Assert.False(context.Response.Headers.ContainsKey("HX-Push-Url"));
        Assert.True(context.Response.Headers.ContainsKey("HX-Replace-Url"));
    }

    [Fact]
    public void TrySetPushUrl_UsesReplace_WhenUrlIsSame()
    {
        var sut = new DynamicEntityListHttpResponseService();
        var context = new DefaultHttpContext();
        var stateUrl = "/DynamicEntity/Index?entity=customer&search=abc&count=true";
        context.Request.Headers["HX-Current-Url"] = "http://localhost" + stateUrl;
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["entity"] = "customer",
            ["search"] = "abc"
        });

        sut.TrySetPushUrl(context.Request, context.Response, "/DynamicEntity/Index", query, "customer", null);

        Assert.False(context.Response.Headers.ContainsKey("HX-Push-Url"));
        Assert.True(context.Response.Headers.ContainsKey("HX-Replace-Url"));
    }
}

