// 自動生成スタブ: dotnet run -- --scaffold-missing-hooks --project=redmineclone で生成
// 各クラスの ExecuteAsync にビジネスロジックを実装してください。

using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.RedmineClone.Hooks;

public sealed class CloseIssueHandler : ICustomActionHandler
{
    private readonly ILogger<CloseIssueHandler> _logger;
    public string Name => "close_issue";

    public CloseIssueHandler(ILogger<CloseIssueHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class ReopenIssueHandler : ICustomActionHandler
{
    private readonly ILogger<ReopenIssueHandler> _logger;
    public string Name => "reopen_issue";

    public ReopenIssueHandler(ILogger<ReopenIssueHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}

public sealed class ResolveIssueHandler : ICustomActionHandler
{
    private readonly ILogger<ResolveIssueHandler> _logger;
    public string Name => "resolve_issue";

    public ResolveIssueHandler(ILogger<ResolveIssueHandler> logger) => _logger = logger;

    public Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // TODO: 実装してください
        return Task.FromResult(ActionHandlerResult.Success());
    }
}
