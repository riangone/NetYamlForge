using System.Collections.Generic;

namespace NetYamlForge.Controllers;

public class ApiListResponse
{
    public List<ApiDto> Data     { get; set; } = new();
    public int Page              { get; set; }
    public int PageSize          { get; set; }
    public int Total             { get; set; }
    public int TotalPages        { get; set; }
}

public class ApiDto
{
    public string?                     Id   { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
}

public class ApiEntityMeta
{
    public string                          Entity            { get; set; } = string.Empty;
    public string                          Table             { get; set; } = string.Empty;
    public string                          DisplayName       { get; set; } = string.Empty;
    public List<string>                    PrimaryKeyColumns { get; set; } = new();
    public Dictionary<string, ApiColumnMeta> Columns         { get; set; } = new();
    public Dictionary<string, ApiFormMeta>   Forms           { get; set; } = new();
}

public class ApiColumnMeta
{
    public string      Type     { get; set; } = string.Empty;
    public string      Label    { get; set; } = string.Empty;
    public bool        Required { get; set; }
    public bool        Editable { get; set; }
    public bool        Identity { get; set; }
    public List<string> Options { get; set; } = new();
}

public class ApiFormMeta
{
    public string      Type     { get; set; } = string.Empty;
    public string      Label    { get; set; } = string.Empty;
    public bool        Required { get; set; }
    public bool        Editable { get; set; }
    public List<string> Options { get; set; } = new();
}
