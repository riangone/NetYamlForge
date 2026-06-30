// ファイル概要：アップロードされたファイルの保存・管理を行うサービスです。
// 画像ファイルのサムネイル生成やファイルサイズ検証も担当します。

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace NetYamlForge.Services;

/// <summary>
/// ファイルアップロード処理サービス。
/// アップロードされたファイルを指定ディレクトリに保存し、パスを返します。
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// ファイルをアップロードし、保存パスを返します。
    /// </summary>
    /// <param name="file">アップロード対象ファイル</param>
    /// <param name="uploadPath">保存先ディレクトリ（相対パス）</param>
    /// <param name="allowedExtensions">許可する拡張子（null の場合は全て許可）</param>
    /// <param name="maxSizeBytes">最大ファイルサイズ（バイト）</param>
    /// <returns>保存されたファイルの相対パス</returns>
    /// <exception cref="InvalidOperationException">ファイルサイズ超過や許可されていない拡張子の場合</exception>
    Task<string> UploadAsync(IFormFile file, string uploadPath, HashSet<string>? allowedExtensions = null, long maxSizeBytes = 10 * 1024 * 1024);

    /// <summary>
    /// 画像ファイルをアップロードし、サムネイルも生成して保存パスを返します。
    /// </summary>
    /// <param name="file">アップロード対象画像ファイル</param>
    /// <param name="uploadPath">保存先ディレクトリ（相対パス）</param>
    /// <param name="thumbnailSize">サムネイルサイズ（幅、高さ）</param>
    /// <param name="maxSizeBytes">最大ファイルサイズ（バイト）</param>
    /// <returns>保存された画像ファイルの相対パス</returns>
    Task<string> UploadImageAsync(IFormFile file, string uploadPath, (int width, int height)? thumbnailSize = null, long maxSizeBytes = 5 * 1024 * 1024);

    /// <summary>
    /// ファイルを削除します。
    /// </summary>
    /// <param name="filePath">削除対象ファイルの相対パス</param>
    void Delete(string filePath);
}

public class FileUploadService : IFileUploadService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileUploadService> _logger;

    public FileUploadService(IWebHostEnvironment environment, ILogger<FileUploadService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<string> UploadAsync(IFormFile file, string uploadPath, HashSet<string>? allowedExtensions = null, long maxSizeBytes = 10 * 1024 * 1024)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("ファイルが選択されていません", nameof(file));
        }

        // ファイルサイズ検証
        if (file.Length > maxSizeBytes)
        {
            throw new InvalidOperationException($"ファイルサイズが大きすぎます（最大 {maxSizeBytes / 1024 / 1024}MB）");
        }

        // 拡張子検証
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (allowedExtensions != null && !allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"許可されていないファイル形式です：{extension}");
        }

        // Magic Bytes 二进制签名校验与像素体积校验防范压缩炸弹
        var readStream = file.OpenReadStream();
        try
        {
            if (!ValidateFileSignature(readStream, extension))
            {
                throw new InvalidOperationException($"ファイルのシグネチャ検証に失敗しました。ファイル内容と拡張子（{extension}）が一致しません。");
            }

            var isImageExt = extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp";
            if (isImageExt)
            {
                ValidatePixelVolume(readStream);
            }
        }
        finally
        {
            if (readStream.CanSeek)
            {
                readStream.Position = 0;
            }
        }

        // 保存先ディレクトリの確保
        var wwwRoot = _environment.WebRootPath;
        var uploadDir = PathSafetyGuard.NormalizeAndValidatePath(uploadPath.TrimStart('/'), wwwRoot, "UploadPath");
        Directory.CreateDirectory(uploadDir);

        // 重複ファイル名回避
        var fileName = file.FileName;
        var safeInitialPath = PathSafetyGuard.NormalizeAndValidatePath(fileName, uploadDir, "FileNameInit");
        fileName = Path.GetFileName(safeInitialPath);

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var counter = 0;
        while (File.Exists(Path.Combine(uploadDir, fileName)))
        {
            counter++;
            fileName = $"{nameWithoutExt}_{counter}{ext}";
        }

        var filePath = PathSafetyGuard.NormalizeAndValidatePath(fileName, uploadDir, "FilePathFinal");
        var relativePath = "/" + Path.GetRelativePath(wwwRoot, filePath).Replace('\\', '/');

        // ファイル保存
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("ファイルアップロード：{FileName} -> {Path}", file.FileName, relativePath);

        return relativePath;
    }

    public async Task<string> UploadImageAsync(IFormFile file, string uploadPath, (int width, int height)? thumbnailSize = null, long maxSizeBytes = 5 * 1024 * 1024)
    {
        // 画像ファイル検証
        var contentType = file.ContentType.ToLowerInvariant();
        if (!contentType.StartsWith("image/"))
        {
            throw new InvalidOperationException("画像ファイルを選択してください");
        }

        // 基本アップロード処理
        var imagePath = await UploadAsync(file, uploadPath, 
            new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }, 
            maxSizeBytes);

        // サムネイル生成（オプション）
        if (thumbnailSize.HasValue)
        {
            await GenerateThumbnailAsync(imagePath, thumbnailSize.Value.width, thumbnailSize.Value.height);
        }

        return imagePath;
    }

    public void Delete(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            var wwwRoot = _environment.WebRootPath;
            var fullPath = PathSafetyGuard.NormalizeAndValidatePath(filePath.TrimStart('/'), wwwRoot, "DeleteFilePath");

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("ファイル削除：{Path}", filePath);
            }

            // サムネイルもあれば削除
            var dir = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            var thumbPath = Path.Combine(dir ?? "", $"{fileName}_thumb{ext}");
            if (File.Exists(thumbPath))
            {
                File.Delete(thumbPath);
            }
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "ファイル削除中にエラー：{Path}", filePath);
        }
    }

    private static readonly Dictionary<string, byte[][]> FileSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } } },
        { ".png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { ".gif", new[] { new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 } } },
        { ".bmp", new[] { new byte[] { 0x42, 0x4D } } },
    };

    private static bool ValidateFileSignature(Stream fileStream, string extension)
    {
        if (!FileSignatures.TryGetValue(extension, out var signatures) && !string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return true; 
        }

        var buffer = new byte[16];
        var originalPosition = fileStream.Position;
        fileStream.Position = 0;
        var bytesRead = fileStream.Read(buffer, 0, buffer.Length);
        fileStream.Position = originalPosition;

        if (bytesRead < 2)
        {
            return false;
        }

        if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            if (bytesRead < 12) return false;
            return buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46
                && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50;
        }

        if (signatures != null)
        {
            foreach (var sig in signatures)
            {
                if (bytesRead >= sig.Length && buffer.Take(sig.Length).SequenceEqual(sig))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ValidatePixelVolume(Stream stream, long maxPixels = 50_000_000)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;
        try
        {
            var info = Image.Identify(stream);
            if (info != null)
            {
                long totalPixels = (long)info.Width * info.Height;
                if (totalPixels > maxPixels)
                {
                    throw new InvalidOperationException($"画像の総ピクセル数が制限を超えています（{info.Width}x{info.Height} = {totalPixels} px、最大 {maxPixels} px）。圧縮爆弾の可能性があります。");
                }
            }
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            // Ignore unrecognized formats and pass
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// 画像のサムネイルを生成します。
    /// </summary>
    private async Task GenerateThumbnailAsync(string imagePath, int maxWidth, int maxHeight)
    {
        try
        {
            var wwwRoot = _environment.WebRootPath;
            var fullPath = PathSafetyGuard.NormalizeAndValidatePath(imagePath.TrimStart('/'), wwwRoot, "ThumbnailSourcePath");
            
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("サムネイル生成対象の元画像が存在しません：{Path}", fullPath);
                return;
            }

            var dir = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            var thumbPath = Path.Combine(dir ?? "", $"{fileName}_thumb{ext}");

            using (var image = await Image.LoadAsync(fullPath))
            {
                var width = image.Width;
                var height = image.Height;

                if (width > maxWidth || height > maxHeight)
                {
                    var ratioX = (double)maxWidth / width;
                    var ratioY = (double)maxHeight / height;
                    var ratio = Math.Min(ratioX, ratioY);

                    var newWidth = (int)(width * ratio);
                    var newHeight = (int)(height * ratio);

                    image.Mutate(x => x.Resize(newWidth, newHeight));
                }

                await image.SaveAsync(thumbPath);
                _logger.LogInformation("サムネイル生成成功：{Path}", thumbPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サムネイル生成中にエラーが発生しました：{Path}", imagePath);
        }
    }
}
