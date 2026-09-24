namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// Agent 自动化编排调度服务实现
/// </summary>
public class AgentOrchestrationService : IAgentOrchestrationService
{
    private readonly IRandomRoleBinder _roleBinder;
    private readonly IRolePromptBuilder _promptBuilder;
    private readonly ICliChainService _cliChainService;
    private readonly ILogger<AgentOrchestrationService> _logger;

    public AgentOrchestrationService(
        IRandomRoleBinder roleBinder,
        IRolePromptBuilder promptBuilder,
        ICliChainService cliChainService,
        ILogger<AgentOrchestrationService> logger)
    {
        _roleBinder = roleBinder;
        _promptBuilder = promptBuilder;
        _cliChainService = cliChainService;
        _logger = logger;
    }

    public AgentTaskAllocation AllocateRoles(IReadOnlyList<string>? customProviders = null)
    {
        return _roleBinder.Shuffle(customProviders);
    }

    public async Task<AgentTaskExecutionResult> ExecutePipelineAsync(
        AgentTaskExecutionRequest request,
        Func<AgentExecutionLogEntry, Task>? onStageCompleted = null,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<AgentExecutionLogEntry>();
        var allocation = _roleBinder.Shuffle(request.CustomProviders);
        var timeoutSec = request.TimeoutSeconds ?? 300;

        _logger.LogInformation(
            "[AgentStudio] Starting pipeline for task '{Title}'. Timeout: {Timeout}s. Allocation: Architect={Arch}, Developer={Dev}, Tester={Test}",
            request.Title,
            timeoutSec,
            allocation.Architect.Provider,
            allocation.Developer.Provider,
            allocation.Tester.Provider);

        // ===== Stage 1: Architect =====
        var archPrompt = _promptBuilder.BuildPrompt(
            RandomRoleBinder.RoleArchitect,
            request.Title,
            request.Description,
            priorContext: null,
            projectName: request.ProjectName);

        _logger.LogInformation("[AgentStudio] Running Stage 1 (Architect) via {Provider}...", allocation.Architect.Provider);

        var archResult = await _cliChainService.PromptAsync(
            prompt: archPrompt,
            imagePath: null,
            projectName: request.ProjectName,
            preferredProvider: allocation.Architect.Provider,
            model: null,
            variant: null,
            timeoutSeconds: timeoutSec,
            cancellationToken: cancellationToken);

        var archLog = new AgentExecutionLogEntry(
            Stage: "Architect",
            Role: RandomRoleBinder.RoleArchitect,
            Provider: archResult.Provider ?? allocation.Architect.Provider,
            Success: archResult.Success,
            Output: archResult.Text,
            Error: archResult.Error,
            Timestamp: DateTime.UtcNow);
        logs.Add(archLog);

        if (onStageCompleted != null)
        {
            try { await onStageCompleted(archLog); }
            catch (Exception ex) { _logger.LogWarning(ex, "[AgentStudio] onStageCompleted callback failed for Stage 1"); }
        }

        if (!archResult.Success)
        {
            _logger.LogError("[AgentStudio] Stage 1 (Architect) failed: {Error}", archResult.Error);
            return new AgentTaskExecutionResult(
                Success: false,
                Allocation: allocation,
                ArchitectOutput: archResult.Text,
                DeveloperOutput: null,
                TesterOutput: null,
                Logs: logs,
                Error: $"Architect stage failed: {archResult.Error}");
        }

        // ===== Stage 2: Developer =====
        var devPrompt = _promptBuilder.BuildPrompt(
            RandomRoleBinder.RoleDeveloper,
            request.Title,
            request.Description,
            priorContext: archResult.Text,
            projectName: request.ProjectName);

        _logger.LogInformation("[AgentStudio] Running Stage 2 (Developer) via {Provider}...", allocation.Developer.Provider);

        var devResult = await _cliChainService.PromptAsync(
            prompt: devPrompt,
            imagePath: null,
            projectName: request.ProjectName,
            preferredProvider: allocation.Developer.Provider,
            model: null,
            variant: null,
            timeoutSeconds: timeoutSec,
            cancellationToken: cancellationToken);

        var devLog = new AgentExecutionLogEntry(
            Stage: "Developer",
            Role: RandomRoleBinder.RoleDeveloper,
            Provider: devResult.Provider ?? allocation.Developer.Provider,
            Success: devResult.Success,
            Output: devResult.Text,
            Error: devResult.Error,
            Timestamp: DateTime.UtcNow);
        logs.Add(devLog);

        if (onStageCompleted != null)
        {
            try { await onStageCompleted(devLog); }
            catch (Exception ex) { _logger.LogWarning(ex, "[AgentStudio] onStageCompleted callback failed for Stage 2"); }
        }

        if (!devResult.Success)
        {
            _logger.LogError("[AgentStudio] Stage 2 (Developer) failed: {Error}", devResult.Error);
            return new AgentTaskExecutionResult(
                Success: false,
                Allocation: allocation,
                ArchitectOutput: archResult.Text,
                DeveloperOutput: devResult.Text,
                TesterOutput: null,
                Logs: logs,
                Error: $"Developer stage failed: {devResult.Error}");
        }

        // ===== Stage 3: Tester =====
        var testerContext = $"### Architect Specification:\n{archResult.Text}\n\n### Developer Implementation Output:\n{devResult.Text}";
        var testerPrompt = _promptBuilder.BuildPrompt(
            RandomRoleBinder.RoleTester,
            request.Title,
            request.Description,
            priorContext: testerContext,
            projectName: request.ProjectName);

        _logger.LogInformation("[AgentStudio] Running Stage 3 (Tester) via {Provider}...", allocation.Tester.Provider);

        var testerResult = await _cliChainService.PromptAsync(
            prompt: testerPrompt,
            imagePath: null,
            projectName: request.ProjectName,
            preferredProvider: allocation.Tester.Provider,
            model: null,
            variant: null,
            timeoutSeconds: timeoutSec,
            cancellationToken: cancellationToken);

        var testerLog = new AgentExecutionLogEntry(
            Stage: "Tester",
            Role: RandomRoleBinder.RoleTester,
            Provider: testerResult.Provider ?? allocation.Tester.Provider,
            Success: testerResult.Success,
            Output: testerResult.Text,
            Error: testerResult.Error,
            Timestamp: DateTime.UtcNow);
        logs.Add(testerLog);

        if (onStageCompleted != null)
        {
            try { await onStageCompleted(testerLog); }
            catch (Exception ex) { _logger.LogWarning(ex, "[AgentStudio] onStageCompleted callback failed for Stage 3"); }
        }

        if (!testerResult.Success)
        {
            _logger.LogError("[AgentStudio] Stage 3 (Tester) failed: {Error}", testerResult.Error);
            return new AgentTaskExecutionResult(
                Success: false,
                Allocation: allocation,
                ArchitectOutput: archResult.Text,
                DeveloperOutput: devResult.Text,
                TesterOutput: testerResult.Text,
                Logs: logs,
                Error: $"Tester stage failed: {testerResult.Error}");
        }

        _logger.LogInformation("[AgentStudio] Pipeline completed successfully for task '{Title}'!", request.Title);

        return new AgentTaskExecutionResult(
            Success: true,
            Allocation: allocation,
            ArchitectOutput: archResult.Text,
            DeveloperOutput: devResult.Text,
            TesterOutput: testerResult.Text,
            Logs: logs);
    }
}
