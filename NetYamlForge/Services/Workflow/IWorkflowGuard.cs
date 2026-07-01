using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Workflow;

public interface IWorkflowGuard
{
    Task<bool> EvaluateAsync(Dictionary<string, object> context);
}
