using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NetYamlForge.Controllers;

[Route("{project}/photo-file")]
public class PhotoFileController : Controller
{
    private readonly IDbConnection _db;
    private readonly ILogger<PhotoFileController> _logger;

    public PhotoFileController(IDbConnection db, ILogger<PhotoFileController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("serve/{photoId}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Serve(string project, string photoId)
    {
        if (string.IsNullOrEmpty(photoId))
            return BadRequest();

        var filePath = await _db.QueryFirstOrDefaultAsync<string>(
            "SELECT file_path FROM photos WHERE photo_id = @PhotoId AND deleted_at IS NULL",
            new { PhotoId = photoId });

        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return NotFound();

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return PhysicalFile(filePath, GetMimeType(ext));
    }

    [HttpGet("thumb/{photoId}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Thumb(string project, string photoId, [FromQuery] int w = 400)
    {
        if (string.IsNullOrEmpty(photoId))
            return BadRequest();
        if (w <= 0 || w > 2000) w = 400;

        var filePath = await _db.QueryFirstOrDefaultAsync<string>(
            "SELECT file_path FROM photos WHERE photo_id = @PhotoId AND deleted_at IS NULL",
            new { PhotoId = photoId });

        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return NotFound();

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var thumbDir = Path.Combine(Path.GetTempPath(), "NetYamlForge", "photo-thumbs");
        Directory.CreateDirectory(thumbDir);
        var thumbName = $"{photoId}_w{w}.jpg";
        var thumbPath = Path.Combine(thumbDir, thumbName);

        if (!System.IO.File.Exists(thumbPath))
        {
            try
            {
                using var img = await Image.LoadAsync(filePath);
                if (img.Width > w)
                    img.Mutate(x => x.Resize(w, 0));
                await img.SaveAsJpegAsync(thumbPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate thumbnail for {PhotoId}", photoId);
                return PhysicalFile(filePath, GetMimeType(ext));
            }
        }

        return PhysicalFile(thumbPath, "image/jpeg");
    }

    private static string GetMimeType(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"  => "image/png",
        ".webp" => "image/webp",
        ".gif"  => "image/gif",
        ".heic" => "image/heic",
        _       => "application/octet-stream"
    };
}
