using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Projects.JpiereCs.Hooks;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.JpiereCs.Hooks;

/// <summary>
/// JpiereCs Hookの統合テスト（インメモリSQLite使用）
/// </summary>
public class JpiereCsHookIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public JpiereCsHookIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE contracts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_no TEXT,
                name TEXT,
                contract_type TEXT DEFAULT 'SAL',
                doc_status TEXT DEFAULT 'DR',
                contract_status TEXT DEFAULT 'WP',
                business_partner_id INTEGER,
                date_acct TEXT,
                period_date_from TEXT,
                period_date_to TEXT,
                total_doc_amt REAL DEFAULT 0,
                monthly_revenue_amt REAL DEFAULT 0,
                is_active INTEGER DEFAULT 1,
                created_at TEXT,
                updated_at TEXT
            );
            CREATE TABLE contract_lines (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                contract_id INTEGER,
                line_no INTEGER,
                product_id INTEGER,
                qty REAL DEFAULT 1,
                unit_price REAL DEFAULT 0,
                line_amt REAL DEFAULT 0,
                tax_amt REAL DEFAULT 0,
                is_active INTEGER DEFAULT 1
            );
            CREATE TABLE estimations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_no TEXT,
                estimation_date TEXT,
                version INTEGER DEFAULT 1,
                doc_status TEXT DEFAULT 'DR',
                business_partner_id INTEGER,
                total_lines REAL DEFAULT 0,
                grand_total REAL DEFAULT 0,
                tax_base_amt REAL DEFAULT 0,
                tax_amt REAL DEFAULT 0,
                linked_contract_id INTEGER,
                is_active INTEGER DEFAULT 1,
                created_at TEXT,
                updated_at TEXT
            );
            CREATE TABLE estimation_lines (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                estimation_id INTEGER,
                line_no INTEGER,
                product_id INTEGER,
                qty_ordered REAL DEFAULT 1,
                unit_price REAL DEFAULT 0,
                line_amt REAL DEFAULT 0,
                tax_amt REAL DEFAULT 0,
                is_active INTEGER DEFAULT 1
            );
            CREATE TABLE bills (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_no TEXT,
                doc_status TEXT DEFAULT 'DR',
                business_partner_id INTEGER,
                date_billed TEXT,
                date_due TEXT,
                payment_term_days INTEGER DEFAULT 30,
                grand_total REAL DEFAULT 0,
                pay_amt REAL DEFAULT 0,
                outstanding_amt REAL DEFAULT 0,
                is_active INTEGER DEFAULT 1,
                created_at TEXT,
                updated_at TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    // Contract Document No Hook Test
    [Fact]
    public async Task ContractDocumentNoHook_GeneratesCorrectFormat()
    {
        var hook = new ContractDocumentNoHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };
        await hook.BeforeAsync(ctx, _connection, null);
        var docNo = ctx.Values["DocumentNo"]?.ToString();

        Assert.NotNull(docNo);
        // Verify format: CON-YYYYMM-XXXX
        Assert.Matches(@"^CON-\d{6}-\d{4}$", docNo);
    }

    // Estimation Document No Hook Test
    [Fact]
    public async Task EstimationDocumentNoHook_GeneratesSequentialNumbers()
    {
        var hook = new EstimationDocumentNoHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };
        await hook.BeforeAsync(ctx, _connection, null);
        Assert.Matches(@"^EST-\d{6}-0001$", ctx.Values["DocumentNo"]?.ToString());
    }

    // Bill Document No Hook Test
    [Fact]
    public async Task BillDocumentNoHook_GeneratesSequentialNumbers()
    {
        var hook = new BillDocumentNoHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };
        await hook.BeforeAsync(ctx, _connection, null);
        Assert.Matches(@"^BILL-\d{6}-0001$", ctx.Values["DocumentNo"]?.ToString());
    }

    // Bill Due Date Hook Test
    [Fact]
    public async Task BillDueDateHook_CalculatesDueDate()
    {
        var hook = new BillDueDateHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>
            {
                ["DateBilled"] = "2026-04-01",
                ["PaymentTermDays"] = 30
            },
            Operation = CrudOperation.Create
        };

        await hook.BeforeAsync(ctx, _connection, null);
        Assert.Equal("2026-05-01", ctx.Values["DateDue"]);
    }

    // Bill Outstanding Hook Test
    [Theory]
    [InlineData(100000, 50000, 50000)]
    [InlineData(100000, 100000, 0)]
    [InlineData(100000, 120000, 0)]
    public async Task BillOutstandingHook_CalculatesCorrectly(double grandTotal, double payAmt, double expected)
    {
        var hook = new BillOutstandingHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>
            {
                ["GrandTotal"] = grandTotal,
                ["PayAmt"] = payAmt
            },
            Operation = CrudOperation.Update
        };

        await hook.BeforeAsync(ctx, _connection, null);
        Assert.Equal(expected, ctx.Values["OutstandingAmt"]);
    }

    // Contract Amount Calculate Hook Test
    [Fact]
    public async Task ContractAmountCalculateHook_SkipsWithoutId()
    {
        var hook = new ContractAmountCalculateHook();

        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var result = await hook.BeforeAsync(ctx, _connection, null);
        Assert.False(result.Cancel);
    }
}
