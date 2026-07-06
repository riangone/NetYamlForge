// ファイル概要：PDFsharp (MIT ライセンス) を使用して YAML 帳票テンプレートから PDF を生成するサービス。
// 5 つのプリミティブ（line / paragraph / row / labelTable / dataTable）を XGraphics で描画します。
// Google Fonts (Noto Sans JP) を使用して日本語表示に対応しています。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetYamlForge.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

/// <summary>
/// PDF 帳票テンプレートから PDF を生成するサービスインターフェース。
/// </summary>
public interface IDocumentPdfService
{
    /// <summary>
    /// プロジェクトの pdf-templates/ ディレクトリから PDF テンプレートを読み込みます。
    /// サブディレクトリも検索します（例: pdf-templates/invoices/invoice.yaml）。
    /// </summary>
    PdfTemplateConfig? LoadTemplate(string projectDir, string templateName);

    byte[] Generate(
        PdfTemplateConfig template,
        IDictionary<string, object?> header,
        IDictionary<string, IList<IDictionary<string, object?>>> dataSources,
        string? projectDir = null);
}

/// <summary>
/// PDFsharp を使用して YAML 帳票テンプレートから PDF を生成するサービス。
/// Google Fonts の Noto Sans JP (TTF) を使用して日本語表示に対応しています。
/// </summary>
public partial class DocumentPdfService : IDocumentPdfService
{
    internal const string FontFamilyName = PdfFontLoader.FontFamilyName;

    static DocumentPdfService()
    {
        // フォントローダーを初期化
        PdfFontLoader.LoadFonts();
    }

    // ── テンプレート読み込み ──────────────────────────────────────────────────

    public PdfTemplateConfig? LoadTemplate(string projectDir, string templateName)
    {
        var templatesDir = Path.Combine(projectDir, "pdf-templates");

        // 直下を先に検索
        var direct = Path.Combine(templatesDir, templateName + ".yaml");
        if (File.Exists(direct)) return LoadYaml(direct);

        // サブディレクトリも再帰検索（例: invoices/invoice-standard）
        if (Directory.Exists(templatesDir))
        {
            var found = Directory.GetFiles(templatesDir, templateName + ".yaml", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found != null) return LoadYaml(found);
        }

        return null;
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
}
