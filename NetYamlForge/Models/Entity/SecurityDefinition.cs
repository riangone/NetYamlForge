using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

public class ValidatorDefinition
{
    public string Type { get; set; } = default!;
    public string? Pattern { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Condition { get; set; }
    public string? HookName { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SecurityDefinition
{
    public PermissionsDefinition? Permissions { get; set; }
    public RowLevelSecurityDefinition? RowLevelSecurity { get; set; }
}

public class PermissionsDefinition
{
    public List<string>? Read { get; set; }
    public List<string>? Write { get; set; }
    public List<string>? Delete { get; set; }
}

public class RowLevelSecurityDefinition
{
    public bool Enabled { get; set; }
    public List<RowLevelSecurityPolicy>? Policies { get; set; }
}

public class RowLevelSecurityPolicy
{
    public string? Role { get; set; }
    public string? FilterClause { get; set; }
}

public class FieldSecurityDefinition
{
    public string? ReadMask { get; set; }
    public List<string>? ReadRoles { get; set; }
    public List<string>? WriteRoles { get; set; }
}

public class WorkflowDefinition
{
    public bool Enabled { get; set; }
    public string StateField { get; set; } = "status";
    public string InitialState { get; set; } = "Draft";
    public List<WorkflowState> States { get; set; } = new();
    public List<WorkflowTransition> Transitions { get; set; } = new();
}

public class WorkflowState
{
    public string Name { get; set; } = default!;
    public string Label { get; set; } = default!;
}

public class WorkflowTransition
{
    public string Name { get; set; } = default!;
    public string Label { get; set; } = default!;
    public List<string> From { get; set; } = new();
    public string To { get; set; } = default!;
    public List<string> Roles { get; set; } = new();
    public List<WorkflowGuard> Guards { get; set; } = new();
    public List<WorkflowAction> Actions { get; set; } = new();
}

public class WorkflowGuard
{
    public string Type { get; set; } = default!;
    public string? ScriptPath { get; set; }
}

public class WorkflowAction
{
    public string Type { get; set; } = default!;
    public string? Template { get; set; }
    public string? Name { get; set; }
}

public class RateLimitingDefinition
{
    public bool Enabled { get; set; }
    public string Strategy { get; set; } = "SlidingWindow";
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 10;
    public string LimitBy { get; set; } = "IP";
}
