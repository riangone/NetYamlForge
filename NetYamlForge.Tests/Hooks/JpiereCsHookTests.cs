using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using NetYamlForge.Projects.JpiereCs.Hooks;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.JpiereCs.Hooks;

public class ContractDocumentNoHookTests : IDisposable
{
    private readonly NetYamlForge.Projects.JpiereCs.Hooks.ContractDocumentNoHook _hook = new();
    private readonly SqliteConnection _db;

    public ContractDocumentNoHookTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        _db.Execute("CREATE TABLE contracts (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT)");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("contract_document_no", _hook.Name);
    }

    [Fact]
    public async Task BeforeAsync_ExistingDocNo_SkipsGeneration()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?> { ["DocumentNo"] = "CUSTOM-001" },
            Operation = CrudOperation.Create
        };

        var result = await _hook.BeforeAsync(ctx, _db, null);

        Assert.False(result.Cancel);
        // DocumentNo should remain unchanged
        Assert.Equal("CUSTOM-001", ctx.Values["DocumentNo"]?.ToString());
    }

    [Fact]
    public async Task BeforeAsync_NoDocNo_GeneratesNumber()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var result = await _hook.BeforeAsync(ctx, _db, null);

        Assert.False(result.Cancel);
        Assert.Matches(@"^CON-\d{6}-0001$", ctx.Values["DocumentNo"]?.ToString());
    }

    [Fact]
    public async Task AfterAsync_ReturnsCompletedTask()
    {
        var ctx = new EntityHookContext { Values = new Dictionary<string, object?>() };
        var mockDb = new Mock<IDbConnection>();

        await _hook.AfterAsync(ctx, mockDb.Object, null);
        // Hook returns Task.CompletedTask, no exception means success
    }
}

public class ContractAmountCalculateHookTests
{
    private readonly ContractAmountCalculateHook _hook = new();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("contract_amount_calculate", _hook.Name);
    }

    [Fact]
    public async Task BeforeAsync_NoId_SkipsCalculation()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var mockDb = new Mock<IDbConnection>();
        var result = await _hook.BeforeAsync(ctx, mockDb.Object, null);

        Assert.True(result.Cancel == false);
    }

    [Fact]
    public async Task AfterAsync_CompletesWithoutError()
    {
        var ctx = new EntityHookContext { Values = new Dictionary<string, object?>() };
        var mockDb = new Mock<IDbConnection>();

        await _hook.AfterAsync(ctx, mockDb.Object, null);
        // Hook returns Task.CompletedTask, no exception means success
    }
}

public class EstimationDocumentNoHookTests : IDisposable
{
    private readonly NetYamlForge.Projects.JpiereCs.Hooks.EstimationDocumentNoHook _hook = new();
    private readonly SqliteConnection _db;

    public EstimationDocumentNoHookTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        _db.Execute("CREATE TABLE estimations (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT)");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("estimation_document_no", _hook.Name);
    }

    [Fact]
    public async Task BeforeAsync_NoDocNo_GeneratesEstimationNumber()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var result = await _hook.BeforeAsync(ctx, _db, null);

        Assert.False(result.Cancel);
        Assert.Matches(@"^EST-\d{6}-0001$", ctx.Values["DocumentNo"]?.ToString());
    }
}

public class BillDocumentNoHookTests : IDisposable
{
    private readonly NetYamlForge.Projects.JpiereCs.Hooks.BillDocumentNoHook _hook = new();
    private readonly SqliteConnection _db;

    public BillDocumentNoHookTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        _db.Execute("CREATE TABLE bills (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT)");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("bill_document_no", _hook.Name);
    }

    [Fact]
    public async Task BeforeAsync_NoDocNo_GeneratesBillNumber()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var result = await _hook.BeforeAsync(ctx, _db, null);

        Assert.False(result.Cancel);
        Assert.Matches(@"^BILL-\d{6}-0001$", ctx.Values["DocumentNo"]?.ToString());
    }
}

public class BillDueDateHookTests
{
    private readonly BillDueDateHook _hook = new();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("bill_due_date", _hook.Name);
    }

    [Fact]
    public async Task BeforeAsync_CalculatesDueDate()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>
            {
                ["DateBilled"] = "2026-04-01",
                ["PaymentTermDays"] = 30
            },
            Operation = CrudOperation.Create
        };

        var mockDb = new Mock<IDbConnection>();
        var result = await _hook.BeforeAsync(ctx, mockDb.Object, null);

        Assert.True(result.Cancel == false);
        Assert.Equal("2026-05-01", ctx.Values["DateDue"]);
    }

    [Fact]
    public async Task BeforeAsync_NoDateBilled_SkipsCalculation()
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>(),
            Operation = CrudOperation.Create
        };

        var mockDb = new Mock<IDbConnection>();
        var result = await _hook.BeforeAsync(ctx, mockDb.Object, null);

        Assert.True(result.Cancel == false);
        Assert.False(ctx.Values.ContainsKey("DateDue"));
    }
}

public class BillOutstandingHookTests
{
    private readonly BillOutstandingHook _hook = new();

    [Fact]
    public void Name_ReturnsCorrectValue()
    {
        Assert.Equal("bill_outstanding", _hook.Name);
    }

    [Theory]
    [InlineData(100000, 50000, 50000)]
    [InlineData(100000, 100000, 0)]
    [InlineData(100000, 120000, 0)]
    public async Task BeforeAsync_CalculatesOutstanding(double grandTotal, double payAmt, double expectedOutstanding)
    {
        var ctx = new EntityHookContext
        {
            Values = new Dictionary<string, object?>
            {
                ["GrandTotal"] = grandTotal,
                ["PayAmt"] = payAmt
            },
            Operation = CrudOperation.Update
        };

        var mockDb = new Mock<IDbConnection>();
        var result = await _hook.BeforeAsync(ctx, mockDb.Object, null);

        Assert.True(result.Cancel == false);
        Assert.Equal(expectedOutstanding, ctx.Values["OutstandingAmt"]);
    }
}
