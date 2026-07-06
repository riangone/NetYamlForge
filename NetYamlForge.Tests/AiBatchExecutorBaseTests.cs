using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests;

public class AiBatchExecutorBaseTests
{
    private class FakeCliChainService : ICliChainService
    {
        public bool Success { get; set; } = true;
        public string Text { get; set; } = "ai-result";
        public string? PromptCalled { get; private set; }

        public Task<CliChainResult> PromptAsync(string prompt, string? imagePath = null, string? projectName = null, CancellationToken cancellationToken = default)
        {
            PromptCalled = prompt;
            return Task.FromResult(new CliChainResult(Success, Text, "fake", null));
        }
    }

    private class ConcreteBatchExecutor : AiBatchExecutorBase<string, string>
    {
        public override string StepType => "test-step";

        public ConcreteBatchExecutor(ICliChainService cli) : base(cli, NullLogger.Instance)
        {
        }

        public string? LoadedInput { get; set; } = "input-data";
        public string? PersistedInput { get; private set; }
        public string? PersistedResult { get; private set; }

        protected override Task<string?> LoadInputAsync(BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx, CancellationToken ct)
        {
            return Task.FromResult(LoadedInput);
        }

        protected override string BuildPrompt(string input)
        {
            return $"prompt:{input}";
        }

        protected override string? ParseResult(string raw)
        {
            return raw == "invalid" ? null : $"parsed:{raw}";
        }

        protected override Task PersistAsync(string input, string result, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx, CancellationToken ct)
        {
            PersistedInput = input;
            PersistedResult = result;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldProcessFlowSuccessfully()
    {
        // Arrange
        var fakeCli = new FakeCliChainService { Success = true, Text = "ai-response" };
        var executor = new ConcreteBatchExecutor(fakeCli);
        var job = new BatchJobDefinition { Id = "job-1" };
        var result = new BatchJobResult();

        // Act
        await executor.ExecuteAsync(job, "test-proj", null!, null!, result, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.RowsAffected);
        Assert.Equal("prompt:input-data", fakeCli.PromptCalled);
        Assert.Equal("input-data", executor.PersistedInput);
        Assert.Equal("parsed:ai-response", executor.PersistedResult);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenCliFails()
    {
        // Arrange
        var fakeCli = new FakeCliChainService { Success = false };
        var executor = new ConcreteBatchExecutor(fakeCli);
        var job = new BatchJobDefinition { Id = "job-1" };
        var result = new BatchJobResult();

        // Act
        await executor.ExecuteAsync(job, "test-proj", null!, null!, result, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.RowsAffected);
        Assert.Null(executor.PersistedResult);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenParsingFails()
    {
        // Arrange
        var fakeCli = new FakeCliChainService { Success = true, Text = "invalid" };
        var executor = new ConcreteBatchExecutor(fakeCli);
        var job = new BatchJobDefinition { Id = "job-1" };
        var result = new BatchJobResult();

        // Act
        await executor.ExecuteAsync(job, "test-proj", null!, null!, result, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.RowsAffected);
        Assert.Null(executor.PersistedResult);
    }
}
