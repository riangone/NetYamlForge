using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NetYamlForge.Controllers;

[Route("photo/thumb")]
public class ThumbnailController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ThumbnailController> _logger;

    public ThumbnailController(IWebHostEnvironment env, ILogger<ThumbnailController> logger)
    {
        _env = env;
        _logger = logger;
    }

    // GET /photo/thumb/{**filename}?w=400
    [HttpGet("{**filename}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetThumb(string filename, [FromQuery] int w = 400)
    {
        if (w <= 0 || w > 2000) w = 400;

        // Sanitize: no path traversal
        var safeName = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeName))
            return BadRequest();

        var origPath = Path.Combine(_env.WebRootPath, "uploads", "photo-vault", safeName);
        if (!System.IO.File.Exists(origPath))
            return NotFound();

        // Cache thumbnail next to original
        var thumbDir = Path.Combine(_env.WebRootPath, "uploads", "photo-vault", ".thumbs");
        Directory.CreateDirectory(thumbDir);

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        var thumbName = $"{Path.GetFileNameWithoutExtension(safeName)}_w{w}.jpg";
        var thumbPath = Path.Combine(thumbDir, thumbName);

        if (!System.IO.File.Exists(thumbPath))
        {
            try
            {
                using var img = await Image.LoadAsync(origPath);
                if (img.Width > w)
                    img.Mutate(x => x.Resize(w, 0)); // maintain aspect ratio
                await img.SaveAsJpegAsync(thumbPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate thumbnail for {File}", safeName);
                // Fall back to original
                return PhysicalFile(origPath, GetMimeType(ext));
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
