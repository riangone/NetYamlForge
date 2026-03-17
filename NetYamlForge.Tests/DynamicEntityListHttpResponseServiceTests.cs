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

        sut.TrySetPushUrl(context.Response, "/DynamicEntity/Index", query, "customer", "/back");

        var header = context.Response.Headers["HX-Push-Url"].ToString();
        Assert.Contains("entity=customer", header);
        Assert.Contains("search=abc", header);
    }
}

