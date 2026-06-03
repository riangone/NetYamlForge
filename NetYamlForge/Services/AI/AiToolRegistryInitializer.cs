using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;
using NetYamlForge.Services.AI.ToolValidation;
using Dapper;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI 插件注册表初始化托管服务
/// 在系统启动时，自动为所有项目注册通用的 Tool 执行体（query_data 和 create_appointment_request）
/// </summary>
public class AiToolRegistryInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiToolRegistryInitializer> _logger;

    public AiToolRegistryInitializer(IServiceProvider serviceProvider, ILogger<AiToolRegistryInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing AI Tool registry with default implementations...");

        using var scope = _serviceProvider.CreateScope();
        var projectManager = scope.ServiceProvider.GetRequiredService<ProjectManager>();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var appointmentService = scope.ServiceProvider.GetRequiredService<IAppointmentService>();

        var projects = projectManager.GetAll();
        foreach (var project in projects)
        {
            var projectId = project.Name;

            // 1. 注册 query_data 工具
            var queryDataTool = new ToolDefinition
            {
                Name = "query_data",
                Description = "データベースエンティティデータ取得ツール",
                ExecuteAsync = async (toolCall) =>
                {
#pragma warning disable DCS001
                    try
                    {
                        var entity = toolCall["entity"]?.ToString();
                        var action = toolCall["action"]?.ToString();
                        
                        if (string.IsNullOrEmpty(entity))
                            return ToolCallResult.Fail("Entity name is required");

                        using var db = dbConnectionFactory.CreateConnection(projectId);
                        db.Open();

                        string sql = $"SELECT * FROM {entity}";
                        var parameters = new DynamicParameters();
                        var filterConditions = new List<string>();

                        if (toolCall["filters"] is JsonArray filters)
                        {
                            int paramIndex = 0;
                            foreach (var filter in filters)
                            {
                                if (filter is JsonObject filterObj)
                                {
                                    var field = filterObj["field"]?.ToString();
                                    var op = filterObj["operator"]?.ToString() ?? "=";
                                    var value = filterObj["value"]?.ToString();

                                    if (!string.IsNullOrEmpty(field) && value != null)
                                    {
                                        var paramName = $"@p{paramIndex++}";
                                        if (string.Equals(op, "like", StringComparison.OrdinalIgnoreCase))
                                        {
                                            filterConditions.Add($"{field} LIKE {paramName}");
                                            parameters.Add(paramName, $"%{value}%");
                                        }
                                        else
                                        {
                                            filterConditions.Add($"{field} {op} {paramName}");
                                            parameters.Add(paramName, value);
                                        }
                                    }
                                }
                            }
                        }

                        if (filterConditions.Count > 0)
                        {
                            sql += " WHERE " + string.Join(" AND ", filterConditions);
                        }

                        if (string.Equals(action, "count", StringComparison.OrdinalIgnoreCase))
                        {
                            sql = $"SELECT COUNT(*) FROM ({sql}) AS count_query";
                            var count = await db.ExecuteScalarAsync<int>(sql, parameters);
                            return ToolCallResult.Success(new { count });
                        }

                        var data = await db.QueryAsync(sql, parameters);
                        return ToolCallResult.Success(data);
                    }
                    catch (Exception ex)
                    {
                        return ToolCallResult.Fail($"Failed to query data: {ex.Message}");
                    }
#pragma warning restore DCS001
                }
            };
            toolRegistry.Register(projectId, queryDataTool);

            // 2. 注册 create_appointment_request 工具
            var createAppointmentTool = new ToolDefinition
            {
                Name = "create_appointment_request",
                Description = "試乗・整備サービス予約作成ツール",
                ExecuteAsync = async (toolCall) =>
                {
                    try
                    {
                        var model = toolCall["vehicle_model"]?.ToString() ?? toolCall["vehicle"]?.ToString();
                        var dateStr = toolCall["preferred_date"]?.ToString() ?? toolCall["date"]?.ToString();
                        var timeStr = toolCall["preferred_time"]?.ToString() ?? toolCall["time"]?.ToString();
                        var name = toolCall["customer_name"]?.ToString() ?? toolCall["name"]?.ToString();
                        var phone = toolCall["customer_phone"]?.ToString() ?? toolCall["phone"]?.ToString();

                        DateTime preferredDateTime = DateTime.Today.AddDays(1).AddHours(10);
                        if (DateTime.TryParse($"{dateStr} {timeStr}", out var parsedDt))
                        {
                            preferredDateTime = parsedDt;
                        }
                        else if (DateTime.TryParse(dateStr, out var parsedD))
                        {
                            preferredDateTime = parsedD;
                        }

                        var request = new AppointmentRequest
                        {
                            CustomerId = name ?? "Unknown",
                            VehicleId = model ?? "Default Model",
                            PreferredDateTime = preferredDateTime,
                            ServiceType = "test_drive"
                        };

                        var apptResult = await appointmentService.CreateAppointmentAsync(request, projectId);
                        if (apptResult.Success)
                        {
                            return ToolCallResult.Success(new
                            {
                                appointment_id = apptResult.AppointmentId,
                                confirmation_number = apptResult.ConfirmationNumber,
                                status = apptResult.Status,
                                confirmed_date_time = apptResult.ConfirmedDateTime
                            });
                        }
                        
                        return ToolCallResult.Fail(apptResult.ErrorMessage ?? "Failed to create appointment");
                    }
                    catch (Exception ex)
                    {
                        return ToolCallResult.Fail($"Failed to create appointment: {ex.Message}");
                    }
                }
            };
            toolRegistry.Register(projectId, createAppointmentTool);
        }

        _logger.LogInformation("AI Tool registry successfully initialized for all projects.");
        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
