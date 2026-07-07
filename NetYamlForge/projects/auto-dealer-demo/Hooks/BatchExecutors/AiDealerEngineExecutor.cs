using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

public class AiDealerEngineExecutor : AiExecutorBase
{
    public override string StepType => "ai_dealer_engine";

    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;

    public AiDealerEngineExecutor(ICliChainService cliChain, ILoggerFactory loggerFactory, ILogger<AiDealerEngineExecutor> logger) 
        : base(cliChain, logger)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public override async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var mode = job.Settings.Params != null && job.Settings.Params.TryGetValue("mode", out var m) ? m : "lead_scoring";
        _logger.LogInformation("[AiDealerEngine] Start: mode={Mode}, project={Project}", mode, projectName);

        IBatchStepHandler executor = mode switch
        {
            "lead_scoring"     => new LeadScoringExecutor(Cli, _loggerFactory),
            "nurturing"        => new NurturingExecutor(Cli, _loggerFactory),
            "quote_generation" => new QuoteGenerationExecutor(Cli, _loggerFactory),
            _                  => throw new NotSupportedException($"Unknown mode: {mode}")
        };

        await executor.ExecuteAsync(job, projectName, db, tx, result, ct);
        _logger.LogInformation("[AiDealerEngine] Complete: mode={Mode}, success={Success}, rows={RowsAffected}", mode, result.Success, result.RowsAffected);
    }
}
