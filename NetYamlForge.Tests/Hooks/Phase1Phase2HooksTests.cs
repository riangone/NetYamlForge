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
/// Phase 1 & Phase 2 Hook 测试统一文件
/// 所有表定义使用与 Hook 查询匹配的列名
/// </summary>
public class Phase1Phase2HooksTests
{
    public class BillCompleteHookTests : IDisposable
    {
        private readonly BillCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public BillCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();

            // 使用PascalCase列名匹配Hook中的TryGetValue
            _db.Execute(@"CREATE TABLE bills (id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentNo TEXT, DocStatus TEXT, GrandTotal REAL, TaxBaseAmt REAL, TaxAmt REAL, DateBilled TEXT)");
            // accounts表使用lowercase因为Hook查询使用lowercase
            _db.Execute(@"CREATE TABLE accounts (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, is_active INTEGER)");
            _db.Execute(@"CREATE TABLE journals (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT, doc_status TEXT, journal_type TEXT, date_acct TEXT, description TEXT, source_table TEXT, source_id INTEGER, total_debit REAL, total_credit REAL, is_balanced INTEGER)");
            _db.Execute(@"CREATE TABLE journal_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, journal_id INTEGER, line_no INTEGER, account_id INTEGER, debit_amt REAL, credit_amt REAL, description TEXT)");

            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (1, '1100', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (2, '4100', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (3, '2400', 1)");
        }

        public void Dispose() => _db.Dispose();

        [Fact] public void Name_ReturnsCorrectValue() => Assert.Equal("bill_complete", _hook.Name);

        [Fact] public async Task AfterAsync_StatusNotCO_DoesNothing()
        {
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "DR" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(0, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
        }

        [Fact] public async Task AfterAsync_StatusCO_CreatesJournal()
        {
            _db.Execute("INSERT INTO bills (id, DocumentNo, DocStatus, GrandTotal, TaxBaseAmt, TaxAmt, DateBilled) VALUES (1, 'BILL-001', 'CO', 110000, 100000, 10000, '2026-04-01')");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
            Assert.Equal(3, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journal_lines"));
            var j = _db.QuerySingle<dynamic>("SELECT * FROM journals WHERE id = 1");
            Assert.Equal(110000.0, Convert.ToDouble(j.total_debit));
            Assert.Equal(110000.0, Convert.ToDouble(j.total_credit));
        }
    }

    public class RecognitionCompleteHookTests : IDisposable
    {
        private readonly RecognitionCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public RecognitionCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();
            _db.Execute(@"CREATE TABLE recognitions (id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentNo TEXT, DocStatus TEXT, GrandTotal REAL, DateAcct TEXT)");
            _db.Execute(@"CREATE TABLE accounts (id INTEGER PRIMARY KEY, code TEXT, is_active INTEGER)");
            _db.Execute(@"CREATE TABLE journals (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT, doc_status TEXT, journal_type TEXT, date_acct TEXT, description TEXT, source_table TEXT, source_id INTEGER, total_debit REAL, total_credit REAL, is_balanced INTEGER)");
            _db.Execute(@"CREATE TABLE journal_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, journal_id INTEGER, line_no INTEGER, account_id INTEGER, debit_amt REAL, credit_amt REAL, description TEXT)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (1, '1100', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (2, '4100', 1)");
        }

        public void Dispose() => _db.Dispose();

        [Fact] public void Name_ReturnsCorrectValue() => Assert.Equal("recognition_complete", _hook.Name);

        [Fact] public async Task AfterAsync_StatusCO_CreatesJournal()
        {
            _db.Execute("INSERT INTO recognitions (id, DocumentNo, DocStatus, GrandTotal, DateAcct) VALUES (1, 'REC-001', 'CO', 500000, '2026-04-01')");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
            Assert.Equal(2, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journal_lines"));
        }
    }

    public class PurchaseReceiptCompleteHookTests : IDisposable
    {
        private readonly PurchaseReceiptCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public PurchaseReceiptCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();
            _db.Execute(@"CREATE TABLE purchase_receipts (id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentNo TEXT, DocStatus TEXT, PurchaseOrderId INTEGER, DateReceived TEXT)");
            _db.Execute(@"CREATE TABLE purchase_receipt_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, ReceiptId INTEGER, PoLineId INTEGER, ProductId INTEGER, QtyReceived REAL, UnitCost REAL)");
            _db.Execute(@"CREATE TABLE purchase_order_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, PurchaseOrderId INTEGER, QtyOrdered REAL, QtyReceived REAL DEFAULT 0)");
            _db.Execute(@"CREATE TABLE products (id INTEGER PRIMARY KEY, ProductType TEXT)");
            _db.Execute(@"CREATE TABLE stock_moves (id INTEGER PRIMARY KEY AUTOINCREMENT, move_type TEXT, product_id INTEGER, qty REAL, unit_cost REAL, date_moved TEXT, source_table TEXT, source_id INTEGER, description TEXT)");
            _db.Execute(@"CREATE TABLE purchase_orders (id INTEGER PRIMARY KEY AUTOINCREMENT, doc_status TEXT)");
        }

        public void Dispose() => _db.Dispose();

        [Fact] public void Name_ReturnsCorrectValue() => Assert.Equal("purchase_receipt_complete", _hook.Name);

        [Fact] public async Task AfterAsync_StatusCO_CreatesStockMoves()
        {
            _db.Execute("INSERT INTO purchase_orders (id, doc_status) VALUES (1, 'IP')");
            _db.Execute("INSERT INTO purchase_receipts (id, DocumentNo, DocStatus, PurchaseOrderId, DateReceived) VALUES (1, 'REC-001', 'CO', 1, '2026-04-01')");
            _db.Execute("INSERT INTO purchase_order_lines (id, PurchaseOrderId, QtyOrdered, QtyReceived) VALUES (1, 1, 10, 0)");
            _db.Execute("INSERT INTO products (id, ProductType) VALUES (1, 'I')");
            _db.Execute("INSERT INTO purchase_receipt_lines (id, ReceiptId, PoLineId, ProductId, QtyReceived, UnitCost) VALUES (1, 1, 1, 1, 5, 1000)");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM stock_moves WHERE move_type = 'IN'"));
            Assert.Equal(5.0, _db.ExecuteScalar<double>("SELECT QtyReceived FROM purchase_order_lines WHERE id = 1"));
        }
    }

    public class APInvoiceCompleteHookTests : IDisposable
    {
        private readonly APInvoiceCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public APInvoiceCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();
            _db.Execute(@"CREATE TABLE ap_invoices (id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentNo TEXT, DocStatus TEXT, GrandTotal REAL, TaxBaseAmt REAL, TaxAmt REAL, DateInvoiced TEXT)");
            _db.Execute(@"CREATE TABLE accounts (id INTEGER PRIMARY KEY, code TEXT, is_active INTEGER)");
            _db.Execute(@"CREATE TABLE journals (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT, doc_status TEXT, journal_type TEXT, date_acct TEXT, description TEXT, source_table TEXT, source_id INTEGER, total_debit REAL, total_credit REAL, is_balanced INTEGER)");
            _db.Execute(@"CREATE TABLE journal_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, journal_id INTEGER, line_no INTEGER, account_id INTEGER, debit_amt REAL, credit_amt REAL, description TEXT)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (1, '5100', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (2, '2410', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (3, '2100', 1)");
        }

        public void Dispose() => _db.Dispose();

        [Fact] public void Name_ReturnsCorrectValue() => Assert.Equal("ap_invoice_complete", _hook.Name);

        [Fact] public async Task AfterAsync_StatusCO_CreatesJournal()
        {
            _db.Execute("INSERT INTO ap_invoices (id, DocumentNo, DocStatus, GrandTotal, TaxBaseAmt, TaxAmt, DateInvoiced) VALUES (1, 'APBILL-001', 'CO', 110000, 100000, 10000, '2026-04-01')");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
            Assert.Equal(3, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journal_lines"));
        }
    }

    public class PaymentCompleteHookTests : IDisposable
    {
        private readonly PaymentCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public PaymentCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();
            _db.Execute(@"CREATE TABLE payments (id INTEGER PRIMARY KEY AUTOINCREMENT, DocumentNo TEXT, PaymentType TEXT, DocStatus TEXT, PayAmt REAL, PaymentDate TEXT, BillId INTEGER, ApInvoiceId INTEGER)");
            _db.Execute(@"CREATE TABLE accounts (id INTEGER PRIMARY KEY, code TEXT, is_active INTEGER)");
            _db.Execute(@"CREATE TABLE journals (id INTEGER PRIMARY KEY AUTOINCREMENT, document_no TEXT, doc_status TEXT, journal_type TEXT, date_acct TEXT, description TEXT, source_table TEXT, source_id INTEGER, total_debit REAL, total_credit REAL, is_balanced INTEGER)");
            _db.Execute(@"CREATE TABLE journal_lines (id INTEGER PRIMARY KEY AUTOINCREMENT, journal_id INTEGER, line_no INTEGER, account_id INTEGER, debit_amt REAL, credit_amt REAL, description TEXT)");
            _db.Execute(@"CREATE TABLE bills (id INTEGER PRIMARY KEY, GrandTotal REAL, PayAmt REAL DEFAULT 0, OutstandingAmt REAL DEFAULT 0, DocStatus TEXT)");
            _db.Execute(@"CREATE TABLE ap_invoices (id INTEGER PRIMARY KEY, GrandTotal REAL, PayAmt REAL DEFAULT 0, OutstandingAmt REAL DEFAULT 0)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (1, '1900', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (2, '1100', 1)");
            _db.Execute("INSERT INTO accounts (id, code, is_active) VALUES (3, '2100', 1)");
        }

        public void Dispose() => _db.Dispose();

        [Fact] public void Name_ReturnsCorrectValue() => Assert.Equal("payment_complete", _hook.Name);

        [Fact] public async Task AfterAsync_ARPayment_UpdatesBill()
        {
            _db.Execute("INSERT INTO bills (id, GrandTotal, PayAmt, OutstandingAmt, DocStatus) VALUES (1, 100000, 0, 100000, 'CO')");
            _db.Execute("INSERT INTO payments (id, DocumentNo, PaymentType, DocStatus, PayAmt, PaymentDate, BillId) VALUES (1, 'PAY-001', 'AR', 'CO', 50000, '2026-04-01', 1)");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
            var bill = _db.QuerySingle<dynamic>("SELECT * FROM bills WHERE id = 1");
            Assert.Equal(50000.0, Convert.ToDouble(bill.PayAmt));
        }

        [Fact] public async Task AfterAsync_APPayment_UpdatesInvoice()
        {
            _db.Execute("INSERT INTO ap_invoices (id, GrandTotal, PayAmt, OutstandingAmt) VALUES (1, 100000, 0, 100000)");
            _db.Execute("INSERT INTO payments (id, DocumentNo, PaymentType, DocStatus, PayAmt, PaymentDate, ApInvoiceId) VALUES (1, 'PAY-002', 'AP', 'CO', 30000, '2026-04-01', 1)");
            var ctx = new EntityHookContext { Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" }, Operation = CrudOperation.Update };
            await _hook.AfterAsync(ctx, _db, null);
            Assert.Equal(1, _db.ExecuteScalar<int>("SELECT COUNT(*) FROM journals"));
            var inv = _db.QuerySingle<dynamic>("SELECT * FROM ap_invoices WHERE id = 1");
            Assert.Equal(30000.0, Convert.ToDouble(inv.PayAmt));
        }
    }
}
