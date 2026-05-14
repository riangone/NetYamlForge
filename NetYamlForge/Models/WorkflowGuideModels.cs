namespace NetYamlForge.Models;

public enum WorkflowStepStatus
{
    NotStarted,
    InProgress,
    Completed,
    Current
}

public class WorkflowFlow
{
    public string FlowId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowStep
{
    public int StepNo { get; set; }
    public string FlowId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public List<string> ResponsibleRoles { get; set; } = new();
    public int DraftCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.NotStarted;
    public bool IsResponsible { get; set; }
}

public class WorkflowGuideViewModel
{
    public List<WorkflowFlow> Flows { get; set; } = new();
    public List<string> UserRoles { get; set; } = new();
    public string UserName { get; set; } = "";
}
