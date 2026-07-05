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

    static PdfFontLoader()
    {
        LoadFonts();
    }

    /// <summary>
    /// フォントを読み込み、UniversalFontResolver を登録します。
    /// </summary>
    public static void LoadFonts()
    {
        try
        {
            byte[]? regularData = null;
            byte[]? boldData = null;

            // 1. プロジェクトの wwwroot/fonts ディレクトリを優先的にチェック
            var wwwrootFontsDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts");
            var wwwrootRegularPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Regular.ttf");
            var wwwrootBoldPath = Path.Combine(wwwrootFontsDir, "NotoSansJP-Bold.ttf");

            if (File.Exists(wwwrootRegularPath) && File.Exists(wwwrootBoldPath))
            {
                regularData = File.ReadAllBytes(wwwrootRegularPath);
                boldData = File.ReadAllBytes(wwwrootBoldPath);
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

                // フォントが見つからない場合は Google Fonts からダウンロード
                if (regularData == null)
                {
                    try
                    {
                        var fontService = new GoogleFontService(AppContext.BaseDirectory);
                        var regularPath = fontService.GetRegularFontPathAsync().GetAwaiter().GetResult();
                        regularData = File.ReadAllBytes(regularPath);
                    }
                    catch
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
                    try
                    {
                        var fontService = new GoogleFontService(AppContext.BaseDirectory);
                        var boldPath = fontService.GetBoldFontPathAsync().GetAwaiter().GetResult();
                        boldData = File.Exists(boldPath) ? File.ReadAllBytes(boldPath) : regularData;
                    }
                    catch
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
                GlobalFontSettings.FontResolver = new UniversalFontResolver(regularData, boldData);
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
