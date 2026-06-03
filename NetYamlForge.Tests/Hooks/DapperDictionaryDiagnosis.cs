using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.JpiereCs.Hooks;

public class DapperDictionaryDiagnosis
{
    private readonly ITestOutputHelper _output;

    public DapperDictionaryDiagnosis(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CheckDictionaryKeys()
    {
        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync();
        
        await db.ExecuteAsync(@"CREATE TABLE bills (
            id INTEGER PRIMARY KEY, 
            DocumentNo TEXT, 
            DocStatus TEXT, 
            GrandTotal REAL
        )");
        
        await db.ExecuteAsync("INSERT INTO bills (id, DocumentNo, DocStatus, GrandTotal) VALUES (1, 'BILL-001', 'CO', 110000)");
        
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bills");
        _output.WriteLine($"Row count in bills: {count}");
        
        var rows = await db.QueryAsync<dynamic>("SELECT * FROM bills");
        foreach (var row in rows)
        {
            _output.WriteLine($"Dynamic row: id={row.id}, DocumentNo={row.DocumentNo}, GrandTotal={row.GrandTotal}");
        }
        
        var billRow = await db.QuerySingleAsync<dynamic>("SELECT * FROM bills WHERE id = 1");
        var bill = (IDictionary<string, object>)billRow;
        
        _output.WriteLine($"Dictionary Keys count: {bill.Keys.Count}");
        foreach (var kvp in bill)
        {
            var displayValue = kvp.Value == null ? "null" : 
                               kvp.Value == DBNull.Value ? "DBNull.Value" : 
                               kvp.Value.ToString();
            _output.WriteLine($"  Key: '{kvp.Key}', Value: {displayValue}");
        }
        
        Assert.NotEmpty(bill);
    }
}
