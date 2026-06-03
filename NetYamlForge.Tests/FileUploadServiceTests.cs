using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class FileUploadServiceTests : IDisposable
{
    private readonly string _webRootPath;
    private readonly Mock<IWebHostEnvironment> _mockEnvironment;
    private readonly Mock<ILogger<FileUploadService>> _mockLogger;
    private readonly FileUploadService _service;

    public FileUploadServiceTests()
    {
        _webRootPath = Path.Combine(Path.GetTempPath(), "NetYamlForge_WebRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRootPath);

        _mockEnvironment = new Mock<IWebHostEnvironment>();
        _mockEnvironment.Setup(m => m.WebRootPath).Returns(_webRootPath);

        _mockLogger = new Mock<ILogger<FileUploadService>>();
        _service = new FileUploadService(_mockEnvironment.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, true);
            }
        }
        catch
        {
            // Ignore clean up errors
        }
    }

    private Mock<IFormFile> CreateMockFile(string fileName, string content, string contentType = "text/plain")
    {
        var fileMock = new Mock<IFormFile>();
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(bytes.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback<Stream, System.Threading.CancellationToken>((s, c) =>
            {
                stream.Position = 0;
                stream.CopyTo(s);
            })
            .Returns(Task.CompletedTask);

        return fileMock;
    }

    [Fact]
    public async Task UploadAsync_ValidFile_SavesSuccessfully()
    {
        // Arrange
        var fileMock = CreateMockFile("test.txt", "Hello World");
        var uploadPath = "uploads/txt";

        // Act
        var relativePath = await _service.UploadAsync(fileMock.Object, uploadPath);

        // Assert
        Assert.NotNull(relativePath);
        Assert.StartsWith("/uploads/txt/test", relativePath);

        var fullPath = Path.Combine(_webRootPath, relativePath.TrimStart('/'));
        Assert.True(File.Exists(fullPath));
        Assert.Equal("Hello World", await File.ReadAllTextAsync(fullPath));
    }

    [Fact]
    public async Task UploadAsync_DirectoryTraversalUploadPath_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var fileMock = CreateMockFile("test.txt", "Hello World");
        var badUploadPath = "../outside_root";

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UploadAsync(fileMock.Object, badUploadPath));
    }

    [Fact]
    public async Task UploadAsync_DirectoryTraversalFileName_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var fileMock = CreateMockFile("../hacked_file.txt", "Hello World");
        var uploadPath = "uploads";

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UploadAsync(fileMock.Object, uploadPath));
    }

    [Fact]
    public async Task Delete_ValidFile_DeletesSuccessfully()
    {
        // Arrange
        var fileMock = CreateMockFile("delete_me.txt", "Delete payload");
        var relativePath = await _service.UploadAsync(fileMock.Object, "temp");
        var fullPath = Path.Combine(_webRootPath, relativePath.TrimStart('/'));

        Assert.True(File.Exists(fullPath));

        // Act
        _service.Delete(relativePath);

        // Assert
        Assert.False(File.Exists(fullPath));
    }

    [Fact]
    public void Delete_TraversalPath_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var badPath = "../outside_root/hack.txt";

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() =>
            _service.Delete(badPath));
    }
}
