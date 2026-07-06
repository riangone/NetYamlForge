using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace NetYamlForge.Services;

/// <summary>
/// PDFsharp のフォント読み込みロジックを分離したユーティリティクラス。
/// Google Fonts (Noto Sans JP) の TTF ファイルを検索・ダウンロードし、
/// UniversalFontResolver を登録します。
/// </summary>
public static class PdfFontLoader
{
    private const string RegularKey = "NetYamlForge-Regular";
    private const string BoldKey = "NetYamlForge-Bold";
    internal const string FontFamilyName = "Arial";

    private static readonly object _resolverLock = new();
    private static bool _isResolverRegistered = false;
    private static byte[]? _cachedRegularData = null;
    private static byte[]? _cachedBoldData = null;

    static PdfFontLoader()
    {
        LoadFonts();
    }

    /// <summary>
    /// フォントを非同期でロードし、UniversalFontResolver を登録します。
    /// </summary>
    public static async Task LoadFontsAsync()
    {
        if (_isResolverRegistered) return;

        try
        {
            byte[]? regularData = null;
            byte[]? boldData = null;

            // 1. プロジェクトの wwwroot/fonts ディレクトリを優先的にチェック
            var wwwrootFontsDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts");
            var wwwrootRegularPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Regular.ttf");
            var wwwrootBoldPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Bold.ttf");

            // Google Font Service キャッシュディレクトリもチェック
            var fontServiceDir = Path.Combine(AppContext.BaseDirectory, "fonts");
            var cacheRegularPath = Path.Combine(fontServiceDir, "NotoSansJP-Regular.ttf");
            var cacheBoldPath = Path.Combine(fontServiceDir, "NotoSansJP-Bold.ttf");

            if (File.Exists(wwwrootRegularPath) && File.Exists(wwwrootBoldPath))
            {
                regularData = await File.ReadAllBytesAsync(wwwrootRegularPath);
                boldData = await File.ReadAllBytesAsync(wwwrootBoldPath);
            }
            else if (File.Exists(cacheRegularPath) && File.Exists(cacheBoldPath))
            {
                regularData = await File.ReadAllBytesAsync(cacheRegularPath);
                boldData = await File.ReadAllBytesAsync(cacheBoldPath);
            }
            else
            {
                // 2. システムフォントディレクトリから TTF ファイルを検索
                var fontPaths = new[]
                {
                    "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
                    "/usr/share/fonts/truetype/noto/NotoSansJP-Regular.ttf",
                    "/Library/Fonts/Arial Unicode.ttf",
                    "C:\\Windows\\Fonts\\ipaexg.ttf",
                    "C:\\Windows\\Fonts\\YuGothR.ttf",
                };

                foreach (var path in fontPaths)
                {
                    if (File.Exists(path))
                    {
                        regularData = await File.ReadAllBytesAsync(path);
                        break;
                    }
                }

                // フォントが見つからない場合は Google Fonts からダウンロード
                if (regularData == null)
                {
                    try
                    {
                        var fontService = new GoogleFontService(AppContext.BaseDirectory);
                        var regularPath = await fontService.GetRegularFontPathAsync();
                        regularData = await File.ReadAllBytesAsync(regularPath);
                    }
                    catch
                    {
                        var ttcPath = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc";
                        if (File.Exists(ttcPath))
                        {
                            regularData = await File.ReadAllBytesAsync(ttcPath);
                        }
                    }
                }

                // Bold フォント
                var boldPaths = new[]
                {
                    "/usr/share/fonts/truetype/noto/NotoSansJP-Bold.ttf",
                    "C:\\Windows\\Fonts\\ipaexg.ttf",
                    "C:\\Windows\\Fonts\\YuGothB.ttf",
                };

                foreach (var path in boldPaths)
                {
                    if (File.Exists(path))
                    {
                        boldData = await File.ReadAllBytesAsync(path);
                        break;
                    }
                }

                if (boldData == null)
                {
                    try
                    {
                        var fontService = new GoogleFontService(AppContext.BaseDirectory);
                        var boldPath = await fontService.GetBoldFontPathAsync();
                        boldData = File.Exists(boldPath) ? await File.ReadAllBytesAsync(boldPath) : regularData;
                    }
                    catch
                    {
                        var ttcPath = "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc";
                        if (File.Exists(ttcPath))
                        {
                            boldData = await File.ReadAllBytesAsync(ttcPath);
                        }
                        else
                        {
                            boldData = regularData;
                        }
                    }
                }
            }

            if (regularData != null && boldData != null)
            {
                lock (_resolverLock)
                {
                    if (!_isResolverRegistered)
                    {
                        _cachedRegularData = regularData;
                        _cachedBoldData = boldData;
                        GlobalFontSettings.FontResolver = new UniversalFontResolver(regularData, boldData);
                        _isResolverRegistered = true;
                    }
                }
            }
        }
        catch
        {
            // フォントのロードに失敗した場合はフォールバック
        }
    }

    /// <summary>
    /// フォントを読み込み、UniversalFontResolver を登録します。（同期、ネットワークダウンロードは行いません）
    /// </summary>
    public static void LoadFonts()
    {
        if (_isResolverRegistered) return;

        try
        {
            byte[]? regularData = null;
            byte[]? boldData = null;

            // 1. プロジェクトの wwwroot/fonts ディレクトリを優先的にチェック
            var wwwrootFontsDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts");
            var wwwrootRegularPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Regular.ttf");
            var wwwrootBoldPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Bold.ttf");

            // Google Font Service キャッシュディレクトリもチェック
            var fontServiceDir = Path.Combine(AppContext.BaseDirectory, "fonts");
            var cacheRegularPath = Path.Combine(fontServiceDir, "NotoSansJP-Regular.ttf");
            var cacheBoldPath = Path.Combine(fontServiceDir, "NotoSansJP-Bold.ttf");

            if (File.Exists(wwwrootRegularPath) && File.Exists(wwwrootBoldPath))
            {
                regularData = File.ReadAllBytes(wwwrootRegularPath);
                boldData = File.ReadAllBytes(wwwrootBoldPath);
            }
            else if (File.Exists(cacheRegularPath) && File.Exists(cacheBoldPath))
            {
                regularData = File.ReadAllBytes(cacheRegularPath);
                boldData = File.ReadAllBytes(cacheBoldPath);
            }
            else
            {
                // 2. システムフォントディレクトリから TTF ファイルを検索
                var fontPaths = new[]
                {
                    "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
                    "/usr/share/fonts/truetype/noto/NotoSansJP-Regular.ttf",
                    "/Library/Fonts/Arial Unicode.ttf",
                    "C:\\Windows\\Fonts\\ipaexg.ttf",
                    "C:\\Windows\\Fonts\\YuGothR.ttf",
                };

                foreach (var path in fontPaths)
                {
                    if (File.Exists(path))
                    {
                        regularData = File.ReadAllBytes(path);
                        break;
                    }
                }

                // 同期版では GetAwaiter().GetResult() を排除し、すでにキャッシュされたファイルのみを参照します。
                if (regularData == null)
                {
                    var fontService = new GoogleFontService(AppContext.BaseDirectory);
                    if (fontService.AreFontsAvailable())
                    {
                        try
                        {
                            var regularPath = Path.Combine(fontServiceDir, "NotoSansJP-Regular.ttf");
                            regularData = File.ReadAllBytes(regularPath);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    if (regularData == null)
                    {
                        var ttcPath = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc";
                        if (File.Exists(ttcPath))
                        {
                            regularData = File.ReadAllBytes(ttcPath);
                        }
                    }
                }

                // Bold フォント
                var boldPaths = new[]
                {
                    "/usr/share/fonts/truetype/noto/NotoSansJP-Bold.ttf",
                    "C:\\Windows\\Fonts\\ipaexg.ttf",
                    "C:\\Windows\\Fonts\\YuGothB.ttf",
                };

                foreach (var path in boldPaths)
                {
                    if (File.Exists(path))
                    {
                        boldData = File.ReadAllBytes(path);
                        break;
                    }
                }

                if (boldData == null)
                {
                    var fontService = new GoogleFontService(AppContext.BaseDirectory);
                    if (fontService.AreFontsAvailable())
                    {
                        try
                        {
                            var boldPath = Path.Combine(fontServiceDir, "NotoSansJP-Bold.ttf");
                            boldData = File.ReadAllBytes(boldPath);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    if (boldData == null)
                    {
                        var ttcPath = "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc";
                        if (File.Exists(ttcPath))
                        {
                            boldData = File.ReadAllBytes(ttcPath);
                        }
                        else
                        {
                            boldData = regularData;
                        }
                    }
                }
            }

            if (regularData != null && boldData != null)
            {
                lock (_resolverLock)
                {
                    if (!_isResolverRegistered)
                    {
                        _cachedRegularData = regularData;
                        _cachedBoldData = boldData;
                        GlobalFontSettings.FontResolver = new UniversalFontResolver(regularData, boldData);
                        _isResolverRegistered = true;
                    }
                }
            }
        }
        catch
        {
            // フォントのロードに失敗した場合はフォールバック
        }
    }

    /// <summary>
    /// TTC (TrueType Collection) データから最初のフォントを抽出します。
    /// </summary>
    internal static byte[] ExtractFirstFontFromTtc(byte[] ttcData)
    {
        if (ttcData.Length < 12 ||
            ttcData[0] != 0x74 || ttcData[1] != 0x74 ||
            ttcData[2] != 0x63 || ttcData[3] != 0x66)
        {
            throw new InvalidDataException("Invalid TTC file signature");
        }

        int fontCount = ReadBE32(ttcData, 8);
        if (fontCount < 1)
            throw new InvalidDataException("TTC contains no fonts");

        int fontOffset = ReadBE32(ttcData, 12);
        if (fontOffset <= 0 || fontOffset + 12 >= ttcData.Length)
            throw new InvalidDataException("Invalid font offset in TTC");

        int numTables = (ttcData[fontOffset + 4] << 8) | ttcData[fontOffset + 5];

        int dirEnd = fontOffset + 12 + numTables * 16;
        if (dirEnd > ttcData.Length)
            throw new InvalidDataException("Invalid TTC table directory length");

        int maxEnd = dirEnd;
        for (int i = 0; i < numTables; i++)
        {
            int entryPos = fontOffset + 12 + i * 16;
            int tblOffset = ReadBE32(ttcData, entryPos + 8);
            int tblLength = ReadBE32(ttcData, entryPos + 12);
            long tblEnd = (long)fontOffset + tblOffset + ((tblLength + 3) & ~3);
            if (tblEnd > ttcData.Length)
                throw new InvalidDataException("Invalid table offset in TTC");
            if (tblEnd > maxEnd) maxEnd = (int)tblEnd;
        }

        int fontLength = maxEnd - fontOffset;
        var fontData = new byte[fontLength];
        Array.Copy(ttcData, fontOffset, fontData, 0, fontLength);
        return fontData;
    }

    private static int ReadBE32(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16)
         | (data[offset + 2] << 8) | data[offset + 3];

    /// <summary>
    /// すべてのフォントリクエストを事前ロードしたフォントデータで処理するリゾルバー。
    /// </summary>
    internal sealed class UniversalFontResolver : IFontResolver
    {
        private readonly byte[] _regularData;
        private readonly byte[] _boldData;

        public UniversalFontResolver(byte[] regularData, byte[] boldData)
        {
            _regularData = regularData;
            _boldData = boldData;
        }

        public string DefaultFontName => FontFamilyName;

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new(isBold ? BoldKey : RegularKey, XStyleSimulations.None);

        public byte[]? GetFont(string faceName) => faceName switch
        {
            BoldKey => _boldData,
            RegularKey => _regularData,
            _ => _regularData,
        };
    }
}
