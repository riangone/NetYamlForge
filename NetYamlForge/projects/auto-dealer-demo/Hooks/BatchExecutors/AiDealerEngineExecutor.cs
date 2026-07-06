using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// AI 全面主導の汽車販売管理エンジン。
/// リードスコアリング・育成タスク生成・見積生成を、それぞれの専用子バッチ実行器に委譲します。
/// </summary>
public class AiDealerEngineExecutor : AiExecutorBase
{
    public override string StepType => "ai_dealer_engine";

    private readonly ILogger<AiDealerEngineExecutor> _logger;

    public AiDealerEngineExecutor(ICliChainService cliChain, ILogger<AiDealerEngineExecutor> logger) 
        : base(cliChain, logger)
    {
        _logger = logger;
    }

    public override async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var mode = job.Settings.Params?.GetValueOrDefault("mode") ?? "lead_scoring";
        _logger.LogInformation("[AiDealerEngine] Start: mode={Mode}, project={Project}", mode, projectName);

        IBatchStepHandler executor = mode switch
        {
            "lead_scoring"     => new LeadScoringExecutor(Cli, _logger),
            "nurturing"        => new NurturingExecutor(Cli, _logger),
            "quote_generation" => new QuoteGenerationExecutor(Cli, _logger),
            _                  => throw new NotSupportedException($"Unknown mode: {mode}")
        };

        await executor.ExecuteAsync(job, projectName, db, tx, result, ct);
        _logger.LogInformation("[AiDealerEngine] Complete: mode={Mode}, success={Success}, rows={RowsAffected}", mode, result.Success, result.RowsAffected);
    }
}
