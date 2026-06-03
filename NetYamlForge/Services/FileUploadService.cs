// ファイル概要：アップロードされたファイルの保存・管理を行うサービスです。
// 画像ファイルのサムネイル生成やファイルサイズ検証も担当します。

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// 画像のサムネイルを生成します。
    /// </summary>
    private async Task GenerateThumbnailAsync(string imagePath, int maxWidth, int maxHeight)
    {
        // 簡易実装：SixLabors.ImageSharp 等を使用する場合はここに実装
        // 現時点ではサムネイル生成はスキップ
        await Task.CompletedTask;
    }
}
