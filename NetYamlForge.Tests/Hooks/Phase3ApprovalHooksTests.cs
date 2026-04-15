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

public class Phase3ApprovalHooksTests
{
    public class PurchaseOrderApprovalHookTests : IDisposable
    {
        private readonly PurchaseOrderApprovalHook _hook = new();
        private readonly SqliteConnection _db;

        public PurchaseOrderApprovalHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();

            _db.Execute(@"CREATE TABLE purchase_orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                DocumentNo TEXT,
                DocStatus TEXT,
                GrandTotal REAL,
                ApprovalStatus TEXT,
                ApprovedBy TEXT,
                ApprovedAt TEXT
            )");

            _db.Execute(@"CREATE TABLE approval_requests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_table TEXT,
                source_id INTEGER,
                requester TEXT,
                current_step INTEGER,
                total_steps INTEGER,
                status TEXT,
                grand_total REAL
            )");

            _db.Execute(@"CREATE TABLE approval_steps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                request_id INTEGER,
                step_no INTEGER,
                approver_role TEXT,
                label TEXT,
                status TEXT
            )");
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Name_ReturnsCorrectValue() => Assert.Equal("purchase_order_approval", _hook.Name);

        [Fact]
        public async Task AfterAsync_StatusNotCO_DoesNothing()
        {
            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "DR" },
                Operation = CrudOperation.Update
            };

            await _hook.AfterAsync(ctx, _db, null);

            var count = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM approval_requests");
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task AfterAsync_SmallAmount_AutoApproves()
        {
            _db.Execute("INSERT INTO purchase_orders (id, DocumentNo, DocStatus, GrandTotal) VALUES (1, 'PO-001', 'CO', 50000)");

            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" },
                Operation = CrudOperation.Update,
                UserName = "test_user"
            };

            await _hook.AfterAsync(ctx, _db, null);

            var order = _db.QuerySingle<dynamic>("SELECT * FROM purchase_orders WHERE id = 1");
            Assert.Equal("APPROVED", order.ApprovalStatus);
        }

        [Fact]
        public async Task AfterAsync_MediumAmount_CreatesOneStep()
        {
            _db.Execute("INSERT INTO purchase_orders (id, DocumentNo, DocStatus, GrandTotal) VALUES (1, 'PO-002', 'CO', 500000)");

            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" },
                Operation = CrudOperation.Update,
                UserName = "test_user"
            };

            await _hook.AfterAsync(ctx, _db, null);

            var reqCount = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM approval_requests");
            var stepCount = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM approval_steps");
            var roles = _db.Query<string>("SELECT approver_role FROM approval_steps ORDER BY step_no").AsList();

            Assert.Equal(1, reqCount);
            Assert.Equal(1, stepCount);
            Assert.Equal(new[] { "approver" }, roles);

            var order = _db.QuerySingle<dynamic>("SELECT * FROM purchase_orders WHERE id = 1");
            Assert.Equal("PENDING", order.ApprovalStatus);
        }

        [Fact]
        public async Task AfterAsync_LargeAmount_CreatesTwoSteps()
        {
            _db.Execute("INSERT INTO purchase_orders (id, DocumentNo, DocStatus, GrandTotal) VALUES (1, 'PO-003', 'CO', 1500000)");

            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?> { ["Id"] = 1, ["DocStatus"] = "CO" },
                Operation = CrudOperation.Update,
                UserName = "test_user"
            };

            await _hook.AfterAsync(ctx, _db, null);

            var reqCount = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM approval_requests");
            var stepCount = _db.ExecuteScalar<int>("SELECT COUNT(*) FROM approval_steps");
            var roles = _db.Query<string>("SELECT approver_role FROM approval_steps ORDER BY step_no").AsList();

            Assert.Equal(1, reqCount);
            Assert.Equal(2, stepCount);
            Assert.Equal(new[] { "approver", "admin" }, roles);
        }
    }

    public class ApprovalStepCompleteHookTests : IDisposable
    {
        private readonly ApprovalStepCompleteHook _hook = new();
        private readonly SqliteConnection _db;

        public ApprovalStepCompleteHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();

            _db.Execute(@"CREATE TABLE approval_requests (
                id INTEGER PRIMARY KEY,
                source_table TEXT,
                source_id INTEGER,
                current_step INTEGER,
                total_steps INTEGER,
                status TEXT
            )");

            _db.Execute(@"CREATE TABLE approval_steps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                request_id INTEGER,
                step_no INTEGER,
                status TEXT
            )");

            _db.Execute(@"CREATE TABLE purchase_orders (
                id INTEGER PRIMARY KEY,
                ApprovalStatus TEXT
            )");
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Name_ReturnsCorrectValue() => Assert.Equal("approval_step_complete", _hook.Name);

        [Fact]
        public async Task AfterAsync_AllStepsApproved_UpdatesRequestAndOrder()
        {
            _db.Execute("INSERT INTO approval_requests (id, source_table, source_id, current_step, total_steps, status) VALUES (1, 'purchase_orders', 1, 1, 1, 'PENDING')");
            _db.Execute("INSERT INTO approval_steps (id, request_id, step_no, status) VALUES (1, 1, 1, 'PENDING')");
            _db.Execute("INSERT INTO purchase_orders (id, ApprovalStatus) VALUES (1, 'PENDING')");

            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?>
                {
                    ["RequestId"] = 1,
                    ["StepNo"] = 1,
                    ["Status"] = "APPROVED"
                },
                Operation = CrudOperation.Update
            };

            await _hook.AfterAsync(ctx, _db, null);

            var req = _db.QuerySingle<dynamic>("SELECT * FROM approval_requests WHERE id = 1");
            Assert.Equal("APPROVED", req.status);

            var order = _db.QuerySingle<dynamic>("SELECT * FROM purchase_orders WHERE id = 1");
            Assert.Equal("APPROVED", order.ApprovalStatus);
        }
    }

    public class ApprovalRejectHookTests : IDisposable
    {
        private readonly ApprovalRejectHook _hook = new();
        private readonly SqliteConnection _db;

        public ApprovalRejectHookTests()
        {
            _db = new SqliteConnection("Data Source=:memory:");
            _db.Open();

            _db.Execute(@"CREATE TABLE approval_requests (
                id INTEGER PRIMARY KEY,
                source_table TEXT,
                source_id INTEGER,
                status TEXT
            )");

            _db.Execute(@"CREATE TABLE purchase_orders (
                id INTEGER PRIMARY KEY,
                ApprovalStatus TEXT
            )");
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Name_ReturnsCorrectValue() => Assert.Equal("approval_reject", _hook.Name);

        [Fact]
        public async Task AfterAsync_StatusRejected_UpdatesRequestAndOrder()
        {
            _db.Execute("INSERT INTO approval_requests (id, source_table, source_id, status) VALUES (1, 'purchase_orders', 1, 'PENDING')");
            _db.Execute("INSERT INTO purchase_orders (id, ApprovalStatus) VALUES (1, 'PENDING')");

            var ctx = new EntityHookContext
            {
                Values = new Dictionary<string, object?>
                {
                    ["RequestId"] = 1,
                    ["Status"] = "REJECTED"
                },
                Operation = CrudOperation.Update
            };

            await _hook.AfterAsync(ctx, _db, null);

            var req = _db.QuerySingle<dynamic>("SELECT * FROM approval_requests WHERE id = 1");
            Assert.Equal("REJECTED", req.status);

            var order = _db.QuerySingle<dynamic>("SELECT * FROM purchase_orders WHERE id = 1");
            Assert.Equal("REJECTED", order.ApprovalStatus);
        }
    }
}
