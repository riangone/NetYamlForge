using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Workflow;

public interface IWorkflowEngine
{
    Task<WorkflowTransitionResult> CanTransitionAsync(
        string entityName, 
        string recordId, 
        string actionName, 
        Dictionary<string, object> context);

    Task<WorkflowTransitionResult> TriggerTransitionAsync(
        string entityName, 
        string recordId, 
        string actionName, 
        Dictionary<string, object> context);
}

public class WorkflowTransitionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FromState { get; set; }
    public string? ToState { get; set; }
}
