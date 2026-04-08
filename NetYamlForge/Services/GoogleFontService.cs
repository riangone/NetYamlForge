// ファイル概要: Google Fonts から日本語対応フォントをダウンロード・キャッシュするサービス。
// PDFsharp のフォント問題 (ttc ファイル非対応) を解決するための代替フォントを提供します。

using System.IO.Compression;
using System.Net.Http;

namespace NetYamlForge.Services;

/// <summary>
/// Google Fonts から日本語対応フォント (Noto Sans JP) をダウンロードし、
/// ローカルにキャッシュするサービス。
/// </summary>
public class GoogleFontService
{
    // Noto Sans JP の Google Fonts URL
    // TTF ファイルを直接ダウンロード（ttc を使用しない）
    // User-Agent ヘッダーが必要（ないと ttf が返らない可能性がある）
    private const string NotoSansJpRegularUrl =
        "https://fonts.gstatic.com/s/notosansjp/v56/-F6jfjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf";

    private const string NotoSansJpBoldUrl =
        "https://fonts.gstatic.com/s/notosansjp/v56/-F62fjtqLzI2JPCgQBnw7HFyzSD-AsregP8VFBEj75s.ttf";

    private const string FontDirectoryName = "fonts";
    private const string RegularFontFileName = "NotoSansJP-Regular.ttf";
    private const string BoldFontFileName = "NotoSansJP-Bold.ttf";

    private readonly string _fontDirectory;
    private readonly string _regularFontPath;
    private readonly string _boldFontPath;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3600)
    };

    public GoogleFontService(string baseDirectory)
    {
        _fontDirectory = Path.Combine(baseDirectory, FontDirectoryName);
        _regularFontPath = Path.Combine(_fontDirectory, RegularFontFileName);
        _boldFontPath = Path.Combine(_fontDirectory, BoldFontFileName);
    }

    /// <summary>
    /// 正体フォントのパスを取得します。必要に応じてダウンロードします。
    /// </summary>
    public async Task<string> GetRegularFontPathAsync()
    {
        if (!File.Exists(_regularFontPath))
            await DownloadFontAsync(_regularFontPath, NotoSansJpRegularUrl);
        
        return _regularFontPath;
    }

    /// <summary>
    /// 太字フォントのパスを取得します。必要に応じてダウンロードします。
    /// </summary>
    public async Task<string> GetBoldFontPathAsync()
    {
        if (!File.Exists(_boldFontPath))
            await DownloadFontAsync(_boldFontPath, NotoSansJpBoldUrl);
        
        return _boldFontPath;
    }

    /// <summary>
    /// 両方のフォントが利用可能かチェックします。
    /// </summary>
    public bool AreFontsAvailable()
    {
        return File.Exists(_regularFontPath) && File.Exists(_boldFontPath);
    }

    /// <summary>
    /// フォントディレクトリをクリアします（テスト用）。
    /// </summary>
    public void ClearCache()
    {
        if (Directory.Exists(_fontDirectory))
        {
            foreach (var file in Directory.GetFiles(_fontDirectory, "*.ttf"))
                File.Delete(file);
        }
    }

    /// <summary>
    /// フォントファイルをダウンロードします。
    /// </summary>
    private async Task DownloadFontAsync(string fontPath, string url)
    {
        Directory.CreateDirectory(_fontDirectory);

        try
        {
            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(fontPath);
            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Google Fonts からのフォントダウンロードに失敗しました：{url}", ex);
        }
    }
}
