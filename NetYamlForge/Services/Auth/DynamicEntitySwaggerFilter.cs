using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NetYamlForge.Services.Auth;

public class DynamicEntitySwaggerFilter : IDocumentFilter
{
    private readonly IServiceProvider _serviceProvider;

    public DynamicEntitySwaggerFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var pm = scope.ServiceProvider.GetRequiredService<ProjectManager>();
        
        foreach (var proj in pm.GetAll())
        {
            var projectName = proj.Name;
            var metadataProvider = proj.EntityMetadata;
            if (metadataProvider == null) continue;
            
            foreach (var kv in metadataProvider.GetAll())
            {
                var entityName = kv.Key;
                var meta = kv.Value;
                
                var apiMode = (meta.Api ?? "disabled").ToLowerInvariant();
                if (apiMode == "disabled")
                {
                    continue;
                }
                
                var isReadonly = apiMode == "readonly";
                var tag = $"{projectName} - {meta.DisplayName}";
                
                // 1. GET & POST /api/{project}/{entity}
                var listPath = $"/api/{projectName}/{entityName}";
                var listPathItem = new OpenApiPathItem();
                
                // GET List
                listPathItem.AddOperation(OperationType.Get, new OpenApiOperation
                {
                    Summary = $"Get list of {meta.DisplayName} for project {projectName}",
                    Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                    Parameters = new List<OpenApiParameter>
                    {
                        new OpenApiParameter { Name = "search", In = ParameterLocation.Query, Required = false, Schema = new OpenApiSchema { Type = "string" } },
                        new OpenApiParameter { Name = "sort", In = ParameterLocation.Query, Required = false, Schema = new OpenApiSchema { Type = "string" } },
                        new OpenApiParameter { Name = "dir", In = ParameterLocation.Query, Required = false, Schema = new OpenApiSchema { Type = "string" } },
                        new OpenApiParameter { Name = "page", In = ParameterLocation.Query, Required = false, Schema = new OpenApiSchema { Type = "integer", Default = new Microsoft.OpenApi.Any.OpenApiInteger(1) } },
                        new OpenApiParameter { Name = "pageSize", In = ParameterLocation.Query, Required = false, Schema = new OpenApiSchema { Type = "integer", Default = new Microsoft.OpenApi.Any.OpenApiInteger(20) } }
                    },
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse { Description = "Success" },
                        ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                        ["403"] = new OpenApiResponse { Description = "Forbidden" }
                    }
                });
                
                // POST Create
                if (!isReadonly)
                {
                    listPathItem.AddOperation(OperationType.Post, new OpenApiOperation
                    {
                        Summary = $"Create a new {meta.DisplayName} for project {projectName}",
                        Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = meta.Forms.ToDictionary(
                                            f => f.Key,
                                            f => new OpenApiSchema { Type = f.Value.Type.ToLowerInvariant() == "int" ? "integer" : "string" }
                                        )
                                    }
                                }
                            }
                        },
                        Responses = new OpenApiResponses
                        {
                            ["201"] = new OpenApiResponse { Description = "Created" },
                            ["400"] = new OpenApiResponse { Description = "Bad Request" },
                            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                            ["403"] = new OpenApiResponse { Description = "Forbidden" }
                        }
                    });
                }
                
                swaggerDoc.Paths[listPath] = listPathItem;
                
                // 2. GET, PUT, PATCH, DELETE /api/{project}/{entity}/{id}
                var itemPath = $"/api/{projectName}/{entityName}/{{id}}";
                var itemPathItem = new OpenApiPathItem();
                
                // GET By Id
                itemPathItem.AddOperation(OperationType.Get, new OpenApiOperation
                {
                    Summary = $"Get {meta.DisplayName} by id for project {projectName}",
                    Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                    Parameters = new List<OpenApiParameter>
                    {
                        new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = "string" } }
                    },
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse { Description = "Success" },
                        ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                        ["403"] = new OpenApiResponse { Description = "Forbidden" },
                        ["404"] = new OpenApiResponse { Description = "Not Found" }
                    }
                });
                
                if (!isReadonly)
                {
                    // PUT Update
                    itemPathItem.AddOperation(OperationType.Put, new OpenApiOperation
                    {
                        Summary = $"Update {meta.DisplayName} by id for project {projectName}",
                        Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                        Parameters = new List<OpenApiParameter>
                        {
                            new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = "string" } }
                        },
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = meta.Forms.ToDictionary(
                                            f => f.Key,
                                            f => new OpenApiSchema { Type = f.Value.Type.ToLowerInvariant() == "int" ? "integer" : "string" }
                                        )
                                    }
                                }
                            }
                        },
                        Responses = new OpenApiResponses
                        {
                            ["200"] = new OpenApiResponse { Description = "Updated" },
                            ["400"] = new OpenApiResponse { Description = "Bad Request" },
                            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                            ["403"] = new OpenApiResponse { Description = "Forbidden" },
                            ["404"] = new OpenApiResponse { Description = "Not Found" }
                        }
                    });

                    // PATCH Partial Update
                    itemPathItem.AddOperation(OperationType.Patch, new OpenApiOperation
                    {
                        Summary = $"Partially update {meta.DisplayName} by id for project {projectName}",
                        Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                        Parameters = new List<OpenApiParameter>
                        {
                            new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = "string" } }
                        },
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = meta.Forms.ToDictionary(
                                            f => f.Key,
                                            f => new OpenApiSchema { Type = f.Value.Type.ToLowerInvariant() == "int" ? "integer" : "string" }
                                        )
                                    }
                                }
                            }
                        },
                        Responses = new OpenApiResponses
                        {
                            ["200"] = new OpenApiResponse { Description = "Updated" },
                            ["400"] = new OpenApiResponse { Description = "Bad Request" },
                            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                            ["403"] = new OpenApiResponse { Description = "Forbidden" },
                            ["404"] = new OpenApiResponse { Description = "Not Found" }
                        }
                    });

                    // DELETE
                    itemPathItem.AddOperation(OperationType.Delete, new OpenApiOperation
                    {
                        Summary = $"Delete {meta.DisplayName} by id for project {projectName}",
                        Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                        Parameters = new List<OpenApiParameter>
                        {
                            new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = "string" } }
                        },
                        Responses = new OpenApiResponses
                        {
                            ["204"] = new OpenApiResponse { Description = "Deleted" },
                            ["400"] = new OpenApiResponse { Description = "Bad Request" },
                            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                            ["403"] = new OpenApiResponse { Description = "Forbidden" },
                            ["404"] = new OpenApiResponse { Description = "Not Found" }
                        }
                    });
                }
                
                swaggerDoc.Paths[listPath] = listPathItem;

                // 3. Custom Actions
                if (!isReadonly && meta.Actions != null && meta.Actions.Count > 0)
                {
                    foreach (var action in meta.Actions)
                    {
                        var actionPath = $"/api/{projectName}/{entityName}/{{id}}/actions/{action.Key}";
                        var actionPathItem = new OpenApiPathItem();
                        actionPathItem.AddOperation(OperationType.Post, new OpenApiOperation
                        {
                            Summary = $"Invoke action '{action.Value.Label}' on {meta.DisplayName} for project {projectName}",
                            Tags = new List<OpenApiTag> { new OpenApiTag { Name = tag } },
                            Parameters = new List<OpenApiParameter>
                            {
                                new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true, Schema = new OpenApiSchema { Type = "string" } }
                            },
                            RequestBody = new OpenApiRequestBody
                            {
                                Required = false,
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/json"] = new OpenApiMediaType
                                    {
                                        Schema = new OpenApiSchema
                                        {
                                            Type = "object",
                                            Description = "Inputs for the action"
                                        }
                                    }
                                }
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "Executed" },
                                ["400"] = new OpenApiResponse { Description = "Bad Request" },
                                ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                                ["403"] = new OpenApiResponse { Description = "Forbidden" },
                                ["404"] = new OpenApiResponse { Description = "Not Found" }
                            }
                        });
                        swaggerDoc.Paths[actionPath] = actionPathItem;
                    }
                }
            }
        }
    }
}
