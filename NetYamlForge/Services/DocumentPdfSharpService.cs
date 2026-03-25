// ファイル概要: PDFsharp (MIT ライセンス) を使用して YAML 帳票テンプレートから PDF を生成するサービス。
// iText 実装 (DocumentPdfService) の代替実装。同じ IDocumentPdfService インターフェースを実装。
// 5 つのプリミティブ（line / paragraph / row / labelTable / dataTable）を XGraphics で描画します。

using System.Text;
using System.Text.RegularExpressions;
using NetYamlForge.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

/// <summary>
/// PDFsharp を使用して YAML 帳票テンプレートから PDF を生成するサービス。
/// iText 7 実装 (DocumentPdfService) のクロスプラットフォーム・MIT ライセンス代替版。
/// </summary>
public class DocumentPdfSharpService : IDocumentPdfService
{
    // 核心フレームワークの PDF テンプレートディレクトリ
    private static readonly string GlobalTemplatesDir =
        Path.Combine(AppContext.BaseDirectory, "Schemas", "pdf-templates");

    // フォント候補: (正体パス, 太字パス or null)
    // 優先順位順。CJK対応フォントが望ましいが、なければ Latin フォールバックを使用。
    // .ttc (TrueType Collection) も含む（抽出処理で最初のフォントを抽出）。
    private static readonly (string Regular, string? Bold)[] FontCandidates =
    [
        // CJK 対応フォント — Linux（日本語フォントをインストールした場合）
        ("/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",       null),
        ("/usr/share/fonts/truetype/fonts-japanese-gothic.ttf",      null),
        ("/usr/share/fonts/opentype/ipafont-mincho/ipamp.ttf",       null),
        ("/usr/share/fonts/truetype/vlgothic/VL-Gothic-Regular.ttf", null),
        ("/usr/share/fonts/truetype/noto/NotoSansJP-Regular.ttf",
         "/usr/share/fonts/truetype/noto/NotoSansJP-Bold.ttf"),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
         "/usr/share/fonts/truetype/noto/NotoSansCJK-Bold.ttc"),
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
         "/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc"),
        ("/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
         "/usr/share/fonts/noto-cjk/NotoSansCJK-Bold.ttc"),
        // CJK 対応フォント — macOS
        ("/Library/Fonts/Arial Unicode.ttf",                         null),
        ("C:\\Windows\\Fonts\\msgothic.ttc",                         null),
        ("/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",           null),
        // Windows — CJK（.ttc を含む）
        ("C:\\Windows\\Fonts\\YuGothR.ttc",                          null),  // 游ゴシック
        // Latin フォールバック — Windows（標準搭載）
        ("C:\\Windows\\Fonts\\arial.ttf",   "C:\\Windows\\Fonts\\arialbd.ttf"),
        ("C:\\Windows\\Fonts\\calibri.ttf", "C:\\Windows\\Fonts\\calibrib.ttf"),
        ("C:\\Windows\\Fonts\\verdana.ttf", "C:\\Windows\\Fonts\\verdanab.ttf"),
        ("C:\\Windows\\Fonts\\tahoma.ttf",  "C:\\Windows\\Fonts\\tahomabd.ttf"),
        // Latin フォールバック — Linux（Ubuntu 標準搭載）
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
         "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/freefont/FreeSans.ttf",
         "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf"),
    ];

    // UniversalFontResolver 内部で GetFont() の引数として使うキー。
    // PDFsharp は内部キーを "familyName|bold|italic" 形式で構築するため、
    // パイプ文字を含む名前を face キーに使うと衝突する。ハイフン区切りを使用する。
    private const string RegularKey = "NetYamlForge-Regular";
    private const string BoldKey    = "NetYamlForge-Bold";

    // new XFont() に渡すファミリー名。UniversalFontResolver はどんな名前でも
    // 横取りして自前データを返すため、実在するフォント名である必要はないが、
    // "Arial" にしておくことでリゾルバー未登録時の Windows fallback にもなる。
    internal const string FontFamilyName = "Arial";

    static DocumentPdfSharpService()
    {
        // 使用可能なフォントファイルを先頭から探す
        var found = FontCandidates.FirstOrDefault(c => File.Exists(c.Regular));

        // 候補リストに見つからない場合はシステムフォントを広域検索する
        if (found == default)
            found = FindAnySystemFont();

        if (found == default)
            return;   // フォントなし: リゾルバー未登録のまま。Windows は Arial で動作継続、Linux はクラッシュ

        try
        {
            var regularData = ExtractFontFromPath(found.Regular);
            var boldData    = found.Bold != null && File.Exists(found.Bold)
                              ? ExtractFontFromPath(found.Bold)
                              : regularData;   // bold がなければ regular で代用

            // UniversalFontResolver はすべてのフォント要求（"Arial" 含む）を横取りし、
            // 自前フォントデータを返す。Linux でプラットフォームフォントが存在しなくても
            // XFont("Arial", ...) が動作するようになる。
            GlobalFontSettings.FontResolver = new UniversalFontResolver(regularData, boldData);
        }
        catch
        {
            // リゾルバー登録失敗（XFont 生成後のロック等）は無視。
            // Windows は Arial ネイティブで動作継続。
        }
    }

    /// <summary>
    /// フォントファイルパスからフォントデータをバイト配列として読み込みます。
    /// TTC (TrueType Collection) ファイルの場合は、最初のフォントを抽出して返します。
    /// TTF ファイルの場合は、そのままのデータを返します。
    /// </summary>
    private static byte[] ExtractFontFromPath(string fontPath)
    {
        var extension = Path.GetExtension(fontPath).ToLowerInvariant();
        var fontData = File.ReadAllBytes(fontPath);

        // TTC ファイルの場合は最初のフォントを抽出
        if (extension == ".ttc")
            return ExtractFirstFontFromTtc(fontData);

        // TTF ファイルはそのまま返す
        return fontData;
    }

    /// <summary>
    /// TTC (TrueType Collection) データから最初のフォントを抽出します。
    /// TTC 形式：
    ///   - 0-3: "ttcf" シグネチャ
    ///   - 4-7: バージョン
    ///   - 8-11: フォント数
    ///   - 12-: 各フォントのオフセット（4 バイトずつ）
    ///   - 各オフセット位置に TTF データ
    /// </summary>
    private static byte[] ExtractFirstFontFromTtc(byte[] ttcData)
    {
        // TTC シグネチャチェック ("ttcf")
        if (ttcData.Length < 12 ||
            ttcData[0] != 0x74 || ttcData[1] != 0x74 ||
            ttcData[2] != 0x63 || ttcData[3] != 0x66)
        {
            throw new InvalidDataException("Invalid TTC file signature");
        }

        // フォント数の取得（ビッグエンディアン）
        int fontCount = (ttcData[8] << 24) | (ttcData[9] << 16) | (ttcData[10] << 8) | ttcData[11];
        if (fontCount < 1)
            throw new InvalidDataException("TTC contains no fonts");

        // 最初のフォントのオフセットを取得（オフセットテーブルはバイト 12 から始まる）
        int offsetOffset = 12; // 最初のオフセットの位置
        int fontOffset = (ttcData[offsetOffset] << 24) | (ttcData[offsetOffset + 1] << 16) |
                         (ttcData[offsetOffset + 2] << 8) | ttcData[offsetOffset + 3];

        if (fontOffset <= 0 || fontOffset >= ttcData.Length)
            throw new InvalidDataException("Invalid font offset in TTC");

        // 最初のフォントの長さを計算
        // 2 番目のフォントのオフセットから最初のフォントの長さを計算する
        int fontLength;
        if (fontCount > 1)
        {
            int offsetOffset2 = offsetOffset + 4; // 2 番目のオフセット
            int fontOffset2 = (ttcData[offsetOffset2] << 24) | (ttcData[offsetOffset2 + 1] << 16) |
                              (ttcData[offsetOffset2 + 2] << 8) | ttcData[offsetOffset2 + 3];
            fontLength = fontOffset2 - fontOffset;
        }
        else
        {
            // フォントが 1 つの場合はファイルの終わりまで
            fontLength = ttcData.Length - fontOffset;
        }

        // フォントデータを抽出
        var fontData = new byte[fontLength];
        Array.Copy(ttcData, fontOffset, fontData, 0, fontLength);
        return fontData;
    }

    /// <summary>
    /// FontCandidates に含まれない場合に、システムの一般的なフォントディレクトリから
    /// 任意の TTF ファイルを探して返します。
    /// </summary>
    private static (string Regular, string? Bold) FindAnySystemFont()
    {
        string[] searchDirs =
        [
            // Windows
            @"C:\Windows\Fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Microsoft", "Windows", "Fonts"),
            // Linux
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
            // macOS
            "/Library/Fonts",
            "/System/Library/Fonts",
        ];

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                // .ttc も対象にする（抽出処理で対応）
                var ttf = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories)
                                   .FirstOrDefault();
                if (ttf != null)
                    return (ttf, null);
                
                // TTF が見つからなければ TTC も試す
                var ttc = Directory.GetFiles(dir, "*.ttc", SearchOption.AllDirectories)
                                   .FirstOrDefault();
                if (ttc != null)
                    return (ttc, null);
            }
            catch { /* アクセス拒否等は無視 */ }
        }

        return default;
    }

    // ── テンプレート読み込み ──────────────────────────────────────────────────

    public PdfTemplateConfig? LoadTemplate(string projectDir, string templateName)
    {
        var path = Path.Combine(projectDir, "pdf-templates", templateName + ".yaml");
        return LoadYaml(path);
    }

    public PdfTemplateConfig? LoadGlobalTemplate(string templateName)
    {
        var path = Path.Combine(GlobalTemplatesDir, templateName + ".yaml");
        return LoadYaml(path);
    }

    private static PdfTemplateConfig? LoadYaml(string path)
    {
        if (!File.Exists(path)) return null;
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<PdfTemplateConfig>(File.ReadAllText(path));
    }

    // ── PDF 生成エントリポイント ──────────────────────────────────────────────

    public byte[] Generate(
        PdfTemplateConfig template,
        IDictionary<string, object?> header,
        IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
        string? projectDir = null)
    {
        var doc = new PdfDocument();
        var (pageW, pageH) = ParsePageSize(template.PageSize, template.Orientation);

        var m = template.Margins;
        double mt = m.Length > 0 ? m[0] : 36;
        double mr = m.Length > 1 ? m[1] : 42;
        double mb = m.Length > 2 ? m[2] : 36;
        double ml = m.Length > 3 ? m[3] : 42;

        var theme = BuildTheme(template.Theme);
        var st    = new RenderState(doc, pageW, pageH, mt, mr, mb, ml, theme);

        foreach (var section in template.Sections)
            RenderTopLevel(st, section, header, dataSources);

        st.Gfx.Dispose();

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 状態管理クラス
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class RenderState
    {
        public PdfDocument Doc { get; }
        public XGraphics   Gfx { get; private set; }
        public double Y  { get; set; }
        public double MarginTop    { get; }
        public double MarginRight  { get; }
        public double MarginBottom { get; }
        public double MarginLeft   { get; }
        public double PageWidth    { get; }
        public double PageHeight   { get; }
        public ThemeVars Theme     { get; }

        public double ContentLeft   => MarginLeft;
        public double ContentRight  => PageWidth - MarginRight;
        public double ContentWidth  => ContentRight - ContentLeft;
        public double ContentBottom => PageHeight - MarginBottom;

        public RenderState(PdfDocument doc,
            double pageW, double pageH,
            double mt, double mr, double mb, double ml,
            ThemeVars theme)
        {
            Doc = doc;
            MarginTop = mt; MarginRight = mr; MarginBottom = mb; MarginLeft = ml;
            PageWidth = pageW; PageHeight = pageH; Theme = theme;
            Y = mt;
            Gfx = OpenNewPage(doc, pageW, pageH);
        }

        public void EnsureSpace(double needed)
        {
            if (Y + needed > ContentBottom) NewPage();
        }

        public void NewPage()
        {
            Gfx.Dispose();
            Gfx = OpenNewPage(Doc, PageWidth, PageHeight);
            Y = MarginTop;
        }

        private static XGraphics OpenNewPage(PdfDocument doc, double w, double h)
        {
            var page = doc.AddPage();
            page.Width  = XUnit.FromPoint(w);
            page.Height = XUnit.FromPoint(h);
            return XGraphics.FromPdfPage(page);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // トップレベル レンダリング
    // ─────────────────────────────────────────────────────────────────────────

    private static void RenderTopLevel(
        RenderState st, PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        double mt = s.MarginTop    ?? 0;
        double mb = s.MarginBottom ?? 0;

        // ページ足りなければ改ページ（簡易推測）
        double approx = ApproxHeight(st.Gfx, s, st.Theme, st.ContentWidth, h, ds);
        st.EnsureSpace(mt + approx + mb);

        st.Y += mt;

        switch (s.Type)
        {
            case "line":
                RenderLine(st, s);
                break;
            case "paragraph":
                st.Y += RenderParagraph(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "row":
                st.Y += RenderRow(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "labelTable":
                st.Y += RenderLabelTable(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, h, ds);
                break;
            case "dataTable":
                st.Y += RenderDataTable(st.Gfx, s, st.Theme,
                    st.ContentLeft, st.Y, st.ContentWidth, ds);
                break;
        }

        st.Y += mb;
    }

    // ── line ─────────────────────────────────────────────────────────────────

    private static void RenderLine(RenderState st, PdfSectionConfig s)
    {
        var color = ResolveXColor(s.Color, st.Theme) ?? st.Theme.Border;
        var pen   = new XPen(color, s.LineWeight);
        st.Gfx.DrawLine(pen, st.ContentLeft, st.Y, st.ContentRight, st.Y);
        st.Y += s.LineWeight;
    }

    // ── paragraph ────────────────────────────────────────────────────────────

    private static double RenderParagraph(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var value = BuildParaText(s, h, ds);
        var main  = (s.Prefix ?? "") + value;
        var color = ResolveXColor(s.Color, t) ?? XColors.Black;
        var style = s.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
        var font  = new XFont(FontFamilyName, s.FontSize, style);
        var brush = new XSolidBrush(color);

        double totalH = DrawWrappedText(gfx, main, font, brush, x, y, width, s.Align);

        if (s.Suffix != null)
        {
            var sfSize  = s.SuffixFontSize ?? s.FontSize;
            var sfFont  = new XFont(FontFamilyName, sfSize, XFontStyleEx.Regular);
            var mainW   = gfx.MeasureString(main, font).Width;
            var lineH   = font.GetHeight() * 1.2;
            double sfX  = s.Align?.ToLowerInvariant() == "center"
                ? x + width / 2 + mainW / 2
                : x + Math.Min(mainW, width);
            gfx.DrawString(s.Suffix, sfFont, brush, sfX, y + lineH * 0.78);
        }

        return totalH;
    }

    private static string BuildParaText(
        PdfSectionConfig s,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Template != null) return InterpolateTemplate(s.Template, h, ds);
        if (s.Field    != null) return FormatValue(ResolveField(s.Field, h, ds), s.Format);
        return s.Text ?? "";
    }

    // ── row ──────────────────────────────────────────────────────────────────

    private static double RenderRow(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Cells.Count == 0) return 0;
        var colW = CalcColWidths(s, totalWidth);

        // 全セルの高さを測定
        double[] heights = s.Cells.Select((c, i) =>
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            return MeasureCellH(gfx, c, t, cw, h, ds);
        }).ToArray();
        double rowH = heights.Max();

        // 描画
        double cx = x;
        for (int i = 0; i < s.Cells.Count; i++)
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            DrawCell(gfx, s.Cells[i], t, cx, y, cw, rowH, h, ds);
            cx += cw;
        }
        return rowH;
    }

    // ── labelTable ───────────────────────────────────────────────────────────

    private static double RenderLabelTable(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float[] cw = s.ColumnWidths ?? [35f, 65f];
        double  lw = cw[0] / 100.0 * totalWidth;
        double  vw = cw.Length > 1 ? cw[1] / 100.0 * totalWidth : totalWidth - lw;

        var border       = new XPen(t.Border, s.BorderWeight);
        var labelBgColor = ResolveXColor(s.LabelBackground, t) ?? t.Label;
        var labelFgColor = s.LabelTextColor != null
            ? (ResolveXColor(s.LabelTextColor, t) ?? XColors.Black)
            : XColors.Black;
        float pad = s.CellPadding;
        float fs  = s.FontSize;

        double totalH = 0, cy = y;

        foreach (var row in s.Rows)
        {
            var rawVal = ResolveField(row.Field, h, ds);
            if (row.OmitIfEmpty && string.IsNullOrWhiteSpace(rawVal)) continue;
            if (row.OmitIfZero  && decimal.TryParse(rawVal, out var dz) && dz == 0m) continue;

            var displayVal = FormatValue(rawVal, row.Format);
            var style = row.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
            var font  = new XFont(FontFamilyName, fs, style);
            double rowH = Math.Max(
                MeasureWrappedH(gfx, row.Label, font, lw - 2 * pad) + 2 * pad,
                Math.Max(MeasureWrappedH(gfx, displayVal, font, vw - 2 * pad) + 2 * pad,
                         fs * 1.5 + 2 * pad));

            // ラベルセル
            gfx.DrawRectangle(new XSolidBrush(labelBgColor), x,      cy, lw, rowH);
            gfx.DrawRectangle(border,                         x,      cy, lw, rowH);
            DrawWrappedText(gfx, row.Label, font, new XSolidBrush(labelFgColor),
                x + pad + 2, cy + pad, lw - 2 * pad - 2, "left");

            // 値セル
            var valueAlign = row.Format is "currency" or "quantity" ? "right" : "left";
            gfx.DrawRectangle(border, x + lw, cy, vw, rowH);
            DrawWrappedText(gfx, displayVal, font, new XSolidBrush(XColors.Black),
                x + lw + pad + 2, cy + pad, vw - 2 * pad - 2, valueAlign);

            cy      += rowH;
            totalH  += rowH;
        }
        return totalH;
    }

    // ── dataTable ────────────────────────────────────────────────────────────

    private static double RenderDataTable(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double totalWidth,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var items = new List<IDictionary<string, object?>>();
        if (s.DataSource != null && ds.TryGetValue(s.DataSource, out var src))
            items.AddRange(src);

        double[] colW   = s.Columns.Select(c => c.Width / 100.0 * totalWidth).ToArray();
        var border      = new XPen(t.Border, s.BorderWeight);
        var labelBg     = ResolveXColor(s.LabelBackground, t) ?? t.Label;
        var labelFg     = s.LabelTextColor != null
            ? (ResolveXColor(s.LabelTextColor, t) ?? XColors.White)
            : XColors.White;
        var oddBg       = ResolveXColor(s.OddRowBackground, t) ?? t.OddRow;
        float pad = s.CellPadding, fs = s.FontSize;
        var font  = new XFont(FontFamilyName, fs, XFontStyleEx.Regular);
        var bfont = new XFont(FontFamilyName, fs, XFontStyleEx.Bold);
        double rowH = fs * 1.4 + 2 * pad;
        double totalH = 0, cy = y;

        // ── ヘッダー行 ──
        double cx = x;
        foreach ((var col, var cw) in s.Columns.Zip(colW))
        {
            gfx.DrawRectangle(new XSolidBrush(labelBg), cx, cy, cw, rowH);
            gfx.DrawRectangle(border,                   cx, cy, cw, rowH);
            DrawWrappedText(gfx, col.Label, bfont, new XSolidBrush(labelFg),
                cx + pad, cy + pad, cw - 2 * pad, col.Align);
            cx += cw;
        }
        cy += rowH; totalH += rowH;

        // ── データ行（minRows まで空行を補完）──
        var display = items.ToList();
        while (display.Count < s.MinRows)
            display.Add(new Dictionary<string, object?>());

        int rowNum = 1;
        foreach (var item in display)
        {
            bool isOdd  = rowNum % 2 == 1;
            bool hasData = item.Count > 0;
            cx = x;
            foreach ((var col, var cw) in s.Columns.Zip(colW))
            {
                var rect = new XRect(cx, cy, cw, rowH);
                if (isOdd && hasData)
                    gfx.DrawRectangle(new XSolidBrush(oddBg), rect);
                gfx.DrawRectangle(border, rect);

                string text;
                if (col.Field == "_rowNumber")
                    text = hasData ? rowNum.ToString() : "";
                else
                {
                    item.TryGetValue(col.Field, out var raw);
                    text = FormatValue(raw?.ToString() ?? "", col.Format);
                }
                DrawWrappedText(gfx, text, font, new XSolidBrush(XColors.Black),
                    cx + pad, cy + pad, cw - 2 * pad, col.Align);
                cx += cw;
            }
            cy += rowH; totalH += rowH;
            if (hasData) rowNum++;
        }
        return totalH;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // セル描画
    // ─────────────────────────────────────────────────────────────────────────

    private static void DrawCell(
        XGraphics gfx, PdfCellConfig cell, ThemeVars t,
        double x, double y, double width, double height,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        // 背景
        if (cell.Background != null)
        {
            var bg = ResolveXColor(cell.Background, t);
            if (bg.HasValue)
                gfx.DrawRectangle(new XSolidBrush(bg.Value), x, y, width, height);
        }
        // ボーダー
        if (cell.BorderWeight is > 0)
            gfx.DrawRectangle(new XPen(t.Border, cell.BorderWeight.Value), x, y, width, height);

        float   basePad = cell.Padding    ?? 0;
        double  pl      = cell.PaddingLeft   ?? basePad;
        double  pr      = cell.PaddingRight  ?? basePad;
        double  pt      = cell.PaddingTop    ?? basePad;
        double  innerW  = Math.Max(width - pl - pr, 1);

        double cy = y + pt;
        foreach (var elem in cell.Elements)
            cy += DrawElementAt(gfx, elem, t, x + pl, cy, innerW, h, ds);
    }

    private static double DrawElementAt(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        double mt = s.MarginTop    ?? 0;
        double mb = s.MarginBottom ?? 0;

        double inner = s.Type switch
        {
            "line"       => DrawInlineLine(gfx, s, t, x, y + mt, width),
            "paragraph"  => RenderParagraph(gfx, s, t, x, y + mt, width, h, ds),
            "row"        => RenderRow(gfx, s, t, x, y + mt, width, h, ds),
            "labelTable" => RenderLabelTable(gfx, s, t, x, y + mt, width, h, ds),
            "dataTable"  => RenderDataTable(gfx, s, t, x, y + mt, width, ds),
            _            => 0,
        };
        return mt + inner + mb;
    }

    private static double DrawInlineLine(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t,
        double x, double y, double width)
    {
        var color = ResolveXColor(s.Color, t) ?? t.Border;
        gfx.DrawLine(new XPen(color, s.LineWeight), x, y, x + width, y);
        return s.LineWeight;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 高さ測定（改ページ判定用）
    // ─────────────────────────────────────────────────────────────────────────

    private static double ApproxHeight(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        return s.Type switch
        {
            "line"       => s.LineWeight,
            "paragraph"  => MeasureParagraphH(gfx, s, t, width, h, ds),
            "row"        => MeasureRowH(gfx, s, t, width, h, ds),
            "labelTable" => MeasureLabelTableH(gfx, s, t, width, h, ds),
            "dataTable"  => (s.FontSize * 1.4 + 2 * s.CellPadding) * (1 + Math.Max(s.MinRows, 3)),
            _            => 0,
        };
    }

    private static double MeasureCellH(
        XGraphics gfx, PdfCellConfig cell, ThemeVars t, double cellWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float   basePad = cell.Padding    ?? 0;
        double  pl      = cell.PaddingLeft   ?? basePad;
        double  pr      = cell.PaddingRight  ?? basePad;
        double  pt      = cell.PaddingTop    ?? basePad;
        double  pb      = cell.PaddingBottom ?? basePad;
        double  innerW  = Math.Max(cellWidth - pl - pr, 1);

        double contentH = cell.Elements.Sum(e =>
        {
            double emt = e.MarginTop ?? 0, emb = e.MarginBottom ?? 0;
            double inner = e.Type switch
            {
                "line"       => e.LineWeight,
                "paragraph"  => MeasureParagraphH(gfx, e, t, innerW, h, ds),
                "row"        => MeasureRowH(gfx, e, t, innerW, h, ds),
                "labelTable" => MeasureLabelTableH(gfx, e, t, innerW, h, ds),
                "dataTable"  => (e.FontSize * 1.4 + 2 * e.CellPadding) * (1 + Math.Max(e.MinRows, 3)),
                _            => 0,
            };
            return emt + inner + emb;
        });

        double total = contentH + pt + pb;
        if (cell.MinHeight.HasValue) total = Math.Max(total, cell.MinHeight.Value);
        return Math.Max(total, 10);
    }

    private static double MeasureParagraphH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double width,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var text = (s.Prefix ?? "") + BuildParaText(s, h, ds);
        var font = new XFont(FontFamilyName, s.FontSize,
            s.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return MeasureWrappedH(gfx, text, font, width);
    }

    private static double MeasureRowH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (s.Cells.Count == 0) return 0;
        var colW = CalcColWidths(s, totalWidth);
        double max = 0;
        for (int i = 0; i < s.Cells.Count; i++)
        {
            double cw = i < colW.Length ? colW[i] : totalWidth;
            max = Math.Max(max, MeasureCellH(gfx, s.Cells[i], t, cw, h, ds));
        }
        return max;
    }

    private static double MeasureLabelTableH(
        XGraphics gfx, PdfSectionConfig s, ThemeVars t, double totalWidth,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        float[] cw = s.ColumnWidths ?? [35f, 65f];
        double  lw = cw[0] / 100.0 * totalWidth;
        double  vw = cw.Length > 1 ? cw[1] / 100.0 * totalWidth : totalWidth - lw;
        float pad = s.CellPadding, fs = s.FontSize;
        var font = new XFont(FontFamilyName, fs, XFontStyleEx.Regular);

        return s.Rows.Where(r =>
        {
            var v = ResolveField(r.Field, h, ds);
            if (r.OmitIfEmpty && string.IsNullOrWhiteSpace(v)) return false;
            if (r.OmitIfZero  && decimal.TryParse(v, out var dz) && dz == 0) return false;
            return true;
        }).Sum(r =>
        {
            var v = ResolveField(r.Field, h, ds);
            return Math.Max(
                MeasureWrappedH(gfx, r.Label, font, lw - 2 * pad) + 2 * pad,
                Math.Max(MeasureWrappedH(gfx, v, font, vw - 2 * pad) + 2 * pad,
                         fs * 1.5 + 2 * pad));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // テキスト描画・測定ユーティリティ
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>折り返しありのテキストを描画し、使用した高さを返します</summary>
    private static double DrawWrappedText(
        XGraphics gfx, string text, XFont font, XBrush brush,
        double x, double y, double width, string? align)
    {
        if (string.IsNullOrEmpty(text)) return font.GetHeight() * 1.2;

        var lines  = WrapText(text, font, Math.Max(width, 1), gfx);
        double lh  = font.GetHeight() * 1.2;

        var xAlign = align?.ToLowerInvariant() switch
        {
            "center" => XStringAlignment.Center,
            "right"  => XStringAlignment.Far,
            _        => XStringAlignment.Near,
        };
        var fmt = new XStringFormat
        {
            Alignment     = xAlign,
            LineAlignment = XLineAlignment.Near,
        };

        double cy = y;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, brush, new XRect(x, cy, width, lh + 2), fmt);
            cy += lh;
        }
        return lh * lines.Count;
    }

    private static double MeasureWrappedH(XGraphics gfx, string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text)) return font.GetHeight() * 1.2;
        return font.GetHeight() * 1.2 * WrapText(text, font, Math.Max(width, 1), gfx).Count;
    }

    /// <summary>改行・折り返し対応のテキスト分割（CJK / ラテン混在対応）</summary>
    private static List<string> WrapText(string text, XFont font, double maxWidth, XGraphics gfx)
    {
        var result = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(para)) { result.Add(""); continue; }
            if (gfx.MeasureString(para, font).Width <= maxWidth) { result.Add(para); continue; }

            // スペースがある場合は単語単位で折り返し（ラテン文字向け）
            var words = para.Split(' ');
            if (words.Length > 1)
            {
                var cur = new StringBuilder();
                foreach (var w in words)
                {
                    var test = cur.Length > 0 ? cur + " " + w : w;
                    if (gfx.MeasureString(test, font).Width > maxWidth && cur.Length > 0)
                    {
                        result.Add(cur.ToString()); cur.Clear(); cur.Append(w);
                    }
                    else { if (cur.Length > 0) cur.Append(' '); cur.Append(w); }
                }
                if (cur.Length > 0) result.Add(cur.ToString());
            }
            else
            {
                // スペースなし→文字単位折り返し（CJK 向け）
                var cur = new StringBuilder();
                foreach (var ch in para)
                {
                    var test = cur.ToString() + ch;
                    if (gfx.MeasureString(test, font).Width > maxWidth && cur.Length > 0)
                    {
                        result.Add(cur.ToString()); cur.Clear(); cur.Append(ch);
                    }
                    else cur.Append(ch);
                }
                if (cur.Length > 0) result.Add(cur.ToString());
            }
        }
        return result.Count > 0 ? result : [""];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // テーマ・色ユーティリティ
    // ─────────────────────────────────────────────────────────────────────────

    private record ThemeVars(
        XColor Primary, XColor Label, XColor LabelText,
        XColor Subtle,  XColor OddRow, XColor Border);

    private static ThemeVars BuildTheme(PdfThemeConfig cfg) => new(
        ParseXColor(cfg.PrimaryColor),    ParseXColor(cfg.LabelColor),
        ParseXColor(cfg.LabelTextColor),  ParseXColor(cfg.SubtleBackground),
        ParseXColor(cfg.OddRowColor),     ParseXColor(cfg.BorderColor));

    private static XColor? ResolveXColor(string? colorRef, ThemeVars t)
    {
        if (string.IsNullOrEmpty(colorRef)) return null;
        return colorRef.ToLowerInvariant() switch
        {
            "primary"   => t.Primary,
            "label"     => t.Label,
            "labeltext" => t.LabelText,
            "subtle"    => t.Subtle,
            "oddrow"    => t.OddRow,
            "border"    => t.Border,
            _           => TryParseXColor(colorRef),
        };
    }

    private static XColor ParseXColor(string hex)
    {
        hex = hex.TrimStart('#');
        return XColor.FromArgb(
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }

    private static XColor? TryParseXColor(string hex)
    {
        try { return ParseXColor(hex); }
        catch { return null; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // その他ユーティリティ
    // ─────────────────────────────────────────────────────────────────────────

    private static (double Width, double Height) ParsePageSize(string size, string orient)
    {
        var (w, h) = size.ToUpperInvariant() switch
        {
            "A3"     => (841.89, 1190.55),
            "LETTER" => (612.00,  792.00),
            "LEGAL"  => (612.00, 1008.00),
            _        => (595.28,  841.89),   // A4
        };
        return orient.ToLowerInvariant() == "landscape" ? (h, w) : (w, h);
    }

    private static double[] CalcColWidths(PdfSectionConfig s, double totalWidth)
    {
        if (s.ColumnWidths is { Length: > 0 })
            return s.ColumnWidths.Select(p => p / 100.0 * totalWidth).ToArray();
        int n = s.Cells.Count;
        return Enumerable.Repeat(totalWidth / n, n).ToArray();
    }

    private static string FormatValue(string? raw, string? format)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return format?.ToLowerInvariant() switch
        {
            "currency" => decimal.TryParse(raw, out var d) ? $"¥{d:N0}" : raw,
            "quantity" => decimal.TryParse(raw, out var q)
                ? (q == Math.Floor(q) ? ((int)q).ToString() : q.ToString("N1"))
                : raw,
            _ => raw,
        };
    }

    private static string ResolveField(
        string? fieldRef,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        if (string.IsNullOrEmpty(fieldRef)) return "";
        var dot = fieldRef.IndexOf('.');
        if (dot > 0)
        {
            var src  = fieldRef[..dot];
            var key  = fieldRef[(dot + 1)..];
            return ds.TryGetValue(src, out var rows) && rows.Count > 0
                ? GetStr(rows[0], key) : "";
        }
        return GetStr(h, fieldRef);
    }

    private static string InterpolateTemplate(
        string tpl,
        IDictionary<string, object?> h,
        IDictionary<string, IList<IDictionary<string, object?>>> ds)
    {
        var result = Regex.Replace(tpl, @"\{([^}]+)\}",
            m => ResolveField(m.Groups[1].Value, h, ds));
        return string.Join("\n",
            result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    private static string GetStr(IDictionary<string, object?>? d, string? key)
    {
        if (d == null || string.IsNullOrEmpty(key)) return "";
        return d.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ユニバーサルフォントリゾルバー
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// すべてのフォントリクエストを事前ロードしたフォントデータで処理するリゾルバー。
    /// Linux 上で Arial 等のプラットフォームフォントが見つからない場合でも
    /// 例外を投げないよう、null を一切返さない設計にしています。
    /// </summary>
    private sealed class UniversalFontResolver : IFontResolver
    {
        private readonly byte[] _regularData;
        private readonly byte[] _boldData;

        public UniversalFontResolver(byte[] regularData, byte[] boldData)
        {
            _regularData = regularData;
            _boldData    = boldData;
        }

        /// <summary>
        /// 要求されたフォントファミリー・スタイルに関わらず、
        /// 常に登録済みフォントの face キーを返します。
        /// これにより Linux 上でも Arial / Helvetica 等の要求が安全に処理されます。
        /// </summary>
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new(isBold ? BoldKey : RegularKey);

        /// <summary>face キーに対応するフォントデータを返します。</summary>
        public byte[]? GetFont(string faceName) => faceName switch
        {
            BoldKey    => _boldData,
            RegularKey => _regularData,
            _          => _regularData,   // 予期しないキーも regular で代用
        };
    }
}
