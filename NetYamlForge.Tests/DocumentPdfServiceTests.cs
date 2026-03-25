using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>DocumentPdfService のフォント解決と PDF 生成を検証します。</summary>
public class DocumentPdfServiceTests
{
    private static readonly DocumentPdfService Svc = new();

    private static Dictionary<string, object?> InvoiceHeader() => new()
    {
        ["InvoiceNo"]    = "INV-TEST-001",
        ["CustomerId"]   = "1",
        ["Title"]        = "テスト請求書",
        ["IssueDate"]    = "2026-01-01",
        ["DueDate"]      = "2026-02-01",
        ["Total"]        = "110000",
        ["TaxAmount"]    = "10000",
        ["Subtotal"]     = "100000",
        ["Status"]       = "issued",
        ["CompanyStamp"] = "株式会社テスト",
        ["PreparedBy"]   = "田中 太郎",
    };

    private static Dictionary<string, IList<IDictionary<string, object?>>> EmptyDs() => new();

    private static void AssertIsPdf(byte[] pdf, string label)
    {
        Assert.True(pdf.Length > 100, $"[{label}] PDF too small: {pdf.Length} bytes");
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("estimate")]
    [InlineData("delivery")]
    [InlineData("contract")]
    [InlineData("invoices/invoice-standard")]
    [InlineData("invoices/invoice-blue")]
    [InlineData("invoices/invoice-bank")]
    [InlineData("orders/purchase-order4")]
    [InlineData("receipts/receipt-02")]
    [InlineData("others/delivery-slip")]
    [InlineData("others/fax-cover")]
    [InlineData("others/minutes-03")]
    [InlineData("others/resume-jis")]
    public void Generate_AllGlobalTemplates_ReturnValidPdf(string templateName)
    {
        var template = Svc.LoadGlobalTemplate(templateName);
        Assert.NotNull(template);

        var pdf = Svc.Generate(template, InvoiceHeader(), EmptyDs());
        AssertIsPdf(pdf, templateName);
    }
}
