using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Controllers;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Tenant;
using Xunit;

namespace NetYamlForge.Tests.Controllers;

/// <summary>
/// 多租户认证控制器测试
/// </summary>
public class TenantAccountControllerTests
{
    private readonly Mock<ITenantUserService> _tenantUsersMock;
    private readonly Mock<ILogger<TenantAccountController>> _loggerMock;
    private readonly TenantAccountController _controller;

    public TenantAccountControllerTests()
    {
        _tenantUsersMock = new Mock<ITenantUserService>();
        _loggerMock = new Mock<ILogger<TenantAccountController>>();
        _controller = new TenantAccountController(_tenantUsersMock.Object, _loggerMock.Object);

        // 设置 HTTP Context 与认证服务
        var httpContext = new DefaultHttpContext();
        
        // 设置 mock 认证服务
        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService
            .Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), 
                It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        mockAuthService
            .Setup(x => x.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), 
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(IAuthenticationService)))
            .Returns(mockAuthService.Object);
        httpContext.RequestServices = serviceProvider.Object;
        
        _controller.ControllerContext.HttpContext = httpContext;
    }

    #region Login GET 测试

    [Fact]
    public void Login_Get_ReturnsView()
    {
        // Act
        var result = _controller.Login();
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<LoginViewModel>(viewResult.Model);
    }

    #endregion

    #region Login POST 测试

    [Fact]
    public async Task Login_Post_ValidCredentials_RedirectsToProject()
    {
        // Arrange
        var model = new LoginViewModel
        {
            UserName = "testuser",
            Password = "TestPass123"
        };

        var user = new AppUser
        {
            Id = 1,
            UserName = "testuser",
            DisplayName = "测试用户",
            UserType = "employee",
            DefaultProjectName = "auto-dealer-demo"
        };

        var projects = new List<ProjectInfo>
        {
            new() { Name = "auto-dealer-demo", DisplayName = "汽车销售演示", DefaultRole = "sales_rep" }
        };

        _tenantUsersMock.Setup(x => x.ValidateCredentialsAsync(model.UserName, model.Password))
            .ReturnsAsync(user);

        _tenantUsersMock.Setup(x => x.GetAccessibleProjectsAsync(user.Id))
            .ReturnsAsync(projects);

        // Act
        var result = await _controller.Login(model);

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/auto-dealer-demo/Dashboard", redirectResult.Url);
    }

    [Fact]
    public async Task Login_Post_InvalidCredentials_ShowsError()
    {
        // Arrange
        var model = new LoginViewModel
        {
            UserName = "testuser",
            Password = "WrongPass"
        };

        _tenantUsersMock.Setup(x => x.ValidateCredentialsAsync(model.UserName, model.Password))
            .ReturnsAsync((AppUser?)null);
        
        // Act
        var result = await _controller.Login(model);
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Login_Post_NoProjectsAssigned_ShowsError()
    {
        // Arrange
        var model = new LoginViewModel
        {
            UserName = "testuser",
            Password = "TestPass123"
        };
        
        var user = new AppUser
        {
            Id = 1,
            UserName = "testuser",
            DisplayName = "测试用户",
            UserType = "employee"
        };
        
        _tenantUsersMock.Setup(x => x.ValidateCredentialsAsync(model.UserName, model.Password))
            .ReturnsAsync(user);
        
        _tenantUsersMock.Setup(x => x.GetAccessibleProjectsAsync(user.Id))
            .ReturnsAsync(new List<ProjectInfo>());
        
        // Act
        var result = await _controller.Login(model);
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        _controller.ModelState.AddModelError("", "未分配任何项目");
        Assert.Contains("未分配任何项目", _controller.ModelState[""].Errors[0].ErrorMessage);
    }

    #endregion

    #region Logout 测试

    [Fact]
    public async Task Logout_Post_RedirectsToLogin()
    {
        // Act
        var result = await _controller.Logout();
        
        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(TenantAccountController.Login), redirectResult.ActionName);
    }

    #endregion

    #region SelectProject 测试

    [Fact]
    public async Task SelectProject_Get_ReturnsViewWithProjects()
    {
        // Arrange
        var userId = 1;
        var projects = new List<ProjectInfo>
        {
            new() { Name = "auto-dealer-demo", DisplayName = "汽车销售演示", IsDefault = true },
            new() { Name = "inventory", DisplayName = "库存管理", IsDefault = false }
        };
        
        _tenantUsersMock.Setup(x => x.GetAccessibleProjectsAsync(userId))
            .ReturnsAsync(projects);
        
        // Mock Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext.HttpContext.User = principal;
        
        // Act
        var result = await _controller.SelectProject();
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SelectProjectViewModel>(viewResult.Model);
        Assert.Equal(2, model.Projects.Count);
    }

    #endregion

    #region AccessDenied 测试

    [Fact]
    public void AccessDenied_Get_WithProject_ReturnsView()
    {
        // Arrange
        var projectName = "auto-dealer-demo";
        
        // Act
        var result = _controller.AccessDenied(projectName);
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AccessDeniedViewModel>(viewResult.Model);
        Assert.Equal(projectName, model.ProjectName);
        Assert.Contains(projectName, model.Message);
    }

    [Fact]
    public void AccessDenied_Get_WithoutProject_ReturnsView()
    {
        // Act
        var result = _controller.AccessDenied();
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AccessDeniedViewModel>(viewResult.Model);
        Assert.Null(model.ProjectName);
        Assert.DoesNotContain("项目", model.Message);
    }

    #endregion

    #region Register GET 测试

    [Fact]
    public void Register_Get_ReturnsView()
    {
        // Arrange
        var project = "auto-dealer-demo";
        var token = "invite-token-123";
        
        // Act
        var result = _controller.Register(project, token);
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<RegisterViewModel>(viewResult.Model);
        Assert.Equal(project, _controller.ViewData["ProjectName"]);
        Assert.Equal(token, _controller.ViewData["Token"]);
    }

    #endregion

    #region Register POST 测试

    [Fact]
    public async Task Register_Post_ValidRequest_CreatesUser()
    {
        // Arrange
        var model = new RegisterViewModel
        {
            UserName = "newcustomer",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            DisplayName = "新客户",
            Email = "customer@example.com",
            Phone = "13800138000"
        };
        
        var project = "auto-dealer-demo";
        var userId = 1;
        
        _tenantUsersMock.Setup(x => x.CreateUserWithProjectRoleAsync(It.IsAny<CreateUserRequest>()))
            .ReturnsAsync(userId);
        
        // Act
        var result = await _controller.Register(model, project);
        
        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(TenantAccountController.Login), redirectResult.ActionName);
    }

    [Fact]
    public async Task Register_Post_Exception_ShowsError()
    {
        // Arrange
        var model = new RegisterViewModel
        {
            UserName = "newcustomer",
            Password = "SecurePass123",
            ConfirmPassword = "SecurePass123",
            DisplayName = "新客户",
            Email = "customer@example.com"
        };
        
        var project = "auto-dealer-demo";
        
        _tenantUsersMock.Setup(x => x.CreateUserWithProjectRoleAsync(It.IsAny<CreateUserRequest>()))
            .ThrowsAsync(new Exception("数据库错误"));
        
        // Act
        var result = await _controller.Register(model, project);
        
        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
    }

    #endregion
}
