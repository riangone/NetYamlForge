using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests.Services.BatchJob;

public class QueueStepHandlerBaseTests
{
    public class FakeRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class FakeQueueHandler : QueueStepHandlerBase<FakeRow>
    {
        public override string StepType => "fake_step";
        protected override int DefaultBatchSize => 5;

        public List<FakeRow> Queue { get; set; } = new();
        public List<(FakeRow Row, RowOutcome Outcome)> WrittenOutcomes { get; } = new();
        public List<FakeRow> MarkedRows { get; } = new();

        public Func<FakeRow, BatchJobDefinition, string?, IDbConnection, IDbTransaction, CancellationToken, Task<RowOutcome>>? ProcessRowHandler { get; set; }
        public Func<FakeRow, IDbConnection, IDbTransaction, Task>? MarkProcessingHandler { get; set; }
        public Func<FakeRow, RowOutcome, IDbConnection, IDbTransaction, Task>? WriteOutcomeHandler { get; set; }

        protected override Task<IReadOnlyList<FakeRow>> FetchPendingAsync(
            BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
            int batchSize, CancellationToken ct)
        {
            var rows = Queue.Take(batchSize).ToList();
            return Task.FromResult<IReadOnlyList<FakeRow>>(rows);
        }

        protected override async Task MarkProcessingAsync(FakeRow row, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
        {
            MarkedRows.Add(row);
            if (MarkProcessingHandler != null)
            {
                await MarkProcessingHandler(row, db, tx);
            }
        }

        protected override async Task<RowOutcome> ProcessRowAsync(
            FakeRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
            CancellationToken ct)
        {
            if (ProcessRowHandler != null)
            {
                return await ProcessRowHandler(row, job, projectName, db, tx, ct);
            }
            return RowOutcome.Ok();
        }

        protected override async Task WriteOutcomeAsync(FakeRow row, RowOutcome outcome, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
        {
            WrittenOutcomes.Add((row, outcome));
            if (WriteOutcomeHandler != null)
            {
                await WriteOutcomeHandler(row, outcome, db, tx);
            }
        }
    }

    [Fact]
    public async Task EmptyQueue_ShouldSucceedAndDoNothing()
    {
        var handler = new FakeQueueHandler();
        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.RowsAffected);
        Assert.Empty(handler.MarkedRows);
        Assert.Empty(handler.WrittenOutcomes);
    }

    [Fact]
    public async Task SecondRowThrowsException_ShouldProcessThirdRowAndReportFailure()
    {
        var handler = new FakeQueueHandler
        {
            Queue = new List<FakeRow>
            {
                new() { Id = 1 },
                new() { Id = 2 },
                new() { Id = 3 }
            },
            ProcessRowHandler = (row, job, proj, db, tx, ct) =>
            {
                if (row.Id == 2)
                    throw new Exception("Row 2 crashed");
                return Task.FromResult(RowOutcome.Ok());
            }
        };

        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.RowsAffected);
        Assert.Contains("1 row(s) failed in fake_step", result.ErrorMessage);

        Assert.Equal(3, handler.MarkedRows.Count);
        Assert.Equal(3, handler.WrittenOutcomes.Count);

        Assert.Equal(RowStatus.Ok, handler.WrittenOutcomes[0].Outcome.Status);
        Assert.Equal(RowStatus.Failed, handler.WrittenOutcomes[1].Outcome.Status);
        Assert.Contains("Row 2 crashed", handler.WrittenOutcomes[1].Outcome.Reason);
        Assert.Equal(RowStatus.Ok, handler.WrittenOutcomes[2].Outcome.Status);
    }

    [Fact]
    public async Task WriteOutcomeThrowsException_ShouldNotAbortBatch()
    {
        var handler = new FakeQueueHandler
        {
            Queue = new List<FakeRow>
            {
                new() { Id = 1 },
                new() { Id = 2 }
            },
            WriteOutcomeHandler = (row, outcome, db, tx) =>
            {
                if (row.Id == 1)
                    throw new Exception("Write crashed");
                return Task.CompletedTask;
            }
        };

        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.RowsAffected);
    }

    [Fact]
    public async Task CancellationTriggered_ShouldStopProcessingRemainingRows()
    {
        var cts = new CancellationTokenSource();
        var handler = new FakeQueueHandler
        {
            Queue = new List<FakeRow>
            {
                new() { Id = 1 },
                new() { Id = 2 }
            },
            ProcessRowHandler = (row, job, proj, db, tx, ct) =>
            {
                cts.Cancel();
                return Task.FromResult(RowOutcome.Ok());
            }
        };

        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, cts.Token);

        Assert.Equal(1, result.RowsAffected);
        Assert.Single(handler.MarkedRows);
        Assert.Single(handler.WrittenOutcomes);
    }

    [Fact]
    public async Task SkippedRows_ShouldNotCountAsDoneOrFail()
    {
        var handler = new FakeQueueHandler
        {
            Queue = new List<FakeRow>
            {
                new() { Id = 1 },
                new() { Id = 2 }
            },
            ProcessRowHandler = (row, job, proj, db, tx, ct) =>
            {
                if (row.Id == 1)
                    return Task.FromResult(RowOutcome.Skip("Skipping first"));
                return Task.FromResult(RowOutcome.Ok());
            }
        };

        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.RowsAffected);
        Assert.Null(result.ErrorMessage);

        Assert.Equal(RowStatus.Skipped, handler.WrittenOutcomes[0].Outcome.Status);
        Assert.Equal(RowStatus.Ok, handler.WrittenOutcomes[1].Outcome.Status);
    }

    [Fact]
    public async Task MarkProcessingThrowsException_ShouldFailCurrentRowAndContinue()
    {
        var handler = new FakeQueueHandler
        {
            Queue = new List<FakeRow>
            {
                new() { Id = 1 },
                new() { Id = 2 }
            },
            MarkProcessingHandler = (row, db, tx) =>
            {
                if (row.Id == 1)
                    throw new Exception("Marking crashed");
                return Task.CompletedTask;
            }
        };

        var job = new BatchJobDefinition { BatchSize = 5 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.RowsAffected);
        Assert.Contains("1 row(s) failed in fake_step", result.ErrorMessage);

        Assert.Equal(2, handler.MarkedRows.Count);
        Assert.Equal(2, handler.WrittenOutcomes.Count);
        Assert.Equal(RowStatus.Failed, handler.WrittenOutcomes[0].Outcome.Status);
        Assert.Equal(RowStatus.Ok, handler.WrittenOutcomes[1].Outcome.Status);
    }

    [Fact]
    public async Task ZeroBatchSize_ShouldFallbackToDefaultBatchSize()
    {
        var handler = new FakeQueueHandler();
        for (int i = 1; i <= 10; i++)
        {
            handler.Queue.Add(new FakeRow { Id = i });
        }

        var job = new BatchJobDefinition { BatchSize = 0 };
        var result = new BatchJobResult();
        var mockConn = new Mock<IDbConnection>();
        var mockTx = new Mock<IDbTransaction>();

        await handler.ExecuteAsync(job, "test_proj", mockConn.Object, mockTx.Object, result, CancellationToken.None);

        Assert.Equal(5, result.RowsAffected);
        Assert.Equal(5, handler.MarkedRows.Count);
    }
}
