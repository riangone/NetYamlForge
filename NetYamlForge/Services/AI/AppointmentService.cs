using System.Data;
using Dapper;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 予約サービス実装
/// </summary>
public class AppointmentService : IAppointmentService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<AppointmentService> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public AppointmentService(
        IDbConnectionFactory dbConnectionFactory,
        ProjectScope projectScope,
        ILogger<AppointmentService> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _projectScope = projectScope;
        _logger = logger;
    }

    private string ResolveProject(string? projectId)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
            return projectId;
        if (_projectScope.IsSet)
            return _projectScope.Current.Name;
        return DefaultProjectId;
    }

    /// <inheritdoc />
    public async Task<List<TimeSlot>> GetAvailableSlotsAsync(DateTime startDate, int days = 7, string? projectId = null)
    {
        var project = ResolveProject(projectId);
        var slots = new List<TimeSlot>();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 営業時間設定（9:00-19:00、1 時間枠）
            var openHour = 9;
            var closeHour = 19;
            var slotDuration = 60; // 分

            for (int day = 0; day < days; day++)
            {
                var currentDate = startDate.Date.AddDays(day);

                // 土日祝日は休業
                if (currentDate.DayOfWeek == DayOfWeek.Saturday || 
                    currentDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                for (int hour = openHour; hour < closeHour; hour++)
                {
                    var slotStart = currentDate.AddHours(hour - openHour);
                    var slotEnd = slotStart.AddMinutes(slotDuration);

                    // 既に予約済みかチェック
                    var isBooked = await IsSlotBookedAsync(db, slotStart, slotEnd);

                    slots.Add(new TimeSlot
                    {
                        StartTime = slotStart,
                        EndTime = slotEnd,
                        IsAvailable = !isBooked
                    });
                }
            }

            return slots;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "空き枠検索に失敗");
            return slots;
        }
    }

    /// <inheritdoc />
    public async Task<AppointmentResult> CreateAppointmentAsync(AppointmentRequest request, string? projectId = null)
    {
        var project = ResolveProject(projectId);
        var appointmentId = GenerateAppointmentId();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 予約枠の重複チェック
            var isConflict = await IsSlotBookedAsync(db, request.PreferredDateTime, 
                request.PreferredDateTime.AddHours(1));

            if (isConflict)
            {
                return new AppointmentResult
                {
                    Success = false,
                    ErrorMessage = "選択された時間帯は既に予約済みです"
                };
            }

            // 予約作成
            var sql = @"
                INSERT INTO service_appointments (
                    appointment_id, customer_id, vehicle_id, appointment_type,
                    preferred_date, status, duration_minutes, created_at, updated_at
                ) VALUES (
                    @AppointmentId, @CustomerId, @VehicleId, @AppointmentType,
                    @PreferredDateTime, 'confirmed', 60, datetime('now'), datetime('now')
                )";

            await db.ExecuteAsync(sql, new
            {
                AppointmentId = appointmentId,
                CustomerId = request.CustomerId,
                VehicleId = request.VehicleId,
                AppointmentType = request.ServiceType,
                PreferredDateTime = request.PreferredDateTime
            });

            _logger.LogInformation("予約作成：{AppointmentId}, 顧客：{CustomerId}", 
                appointmentId, request.CustomerId);

            return new AppointmentResult
            {
                Success = true,
                AppointmentId = appointmentId,
                ConfirmationNumber = appointmentId,
                ConfirmedDateTime = request.PreferredDateTime,
                Status = "confirmed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約作成に失敗：{CustomerId}", request.CustomerId);
            return new AppointmentResult
            {
                Success = false,
                ErrorMessage = "予約作成中にエラーが発生しました"
            };
        }
    }

    /// <inheritdoc />
    public async Task<AppointmentResult> UpdateAppointmentAsync(
        string appointmentId, 
        AppointmentUpdateRequest request, 
        string? projectId = null)
    {
        var project = ResolveProject(projectId);

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            // 既存予約の取得
            var existingSql = "SELECT * FROM service_appointments WHERE appointment_id = @AppointmentId";
            var existing = await db.QueryFirstOrDefaultAsync(existingSql, new { AppointmentId = appointmentId });

            if (existing == null)
            {
                return new AppointmentResult
                {
                    Success = false,
                    ErrorMessage = "予約が見つかりません"
                };
            }

            // 更新フィールド構築
            var updateFields = new List<string>();
            var parameters = new Dictionary<string, object>
            {
                ["AppointmentId"] = appointmentId
            };

            if (request.NewDateTime.HasValue)
            {
                // 新しい時間帯の重複チェック
                var isConflict = await IsSlotBookedAsync(db, request.NewDateTime.Value, 
                    request.NewDateTime.Value.AddHours(1));

                if (isConflict)
                {
                    return new AppointmentResult
                    {
                        Success = false,
                        ErrorMessage = "選択された時間帯は既に予約済みです"
                    };
                }

                updateFields.Add("preferred_date = @NewDateTime");
                parameters["NewDateTime"] = request.NewDateTime.Value;
            }

            if (!string.IsNullOrEmpty(request.ServiceType))
            {
                updateFields.Add("appointment_type = @ServiceType");
                parameters["ServiceType"] = request.ServiceType;
            }

            if (updateFields.Count == 0)
            {
                return new AppointmentResult
                {
                    Success = false,
                    ErrorMessage = "更新するフィールドがありません"
                };
            }

            updateFields.Add("updated_at = datetime('now')");

            var updateSql = $@"
                UPDATE service_appointments
                SET {string.Join(", ", updateFields)}
                WHERE appointment_id = @AppointmentId";

            await db.ExecuteAsync(updateSql, parameters);

            _logger.LogInformation("予約更新：{AppointmentId}", appointmentId);

            return new AppointmentResult
            {
                Success = true,
                AppointmentId = appointmentId,
                Status = "confirmed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約更新に失敗：{AppointmentId}", appointmentId);
            return new AppointmentResult
            {
                Success = false,
                ErrorMessage = "予約更新中にエラーが発生しました"
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelAppointmentAsync(string appointmentId, string? projectId = null)
    {
        var project = ResolveProject(projectId);

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                UPDATE service_appointments
                SET 
                    status = 'cancelled',
                    cancelled_at = datetime('now'),
                    updated_at = datetime('now')
                WHERE appointment_id = @AppointmentId";

            var rows = await db.ExecuteAsync(sql, new { AppointmentId = appointmentId });

            if (rows > 0)
            {
                _logger.LogInformation("予約キャンセル：{AppointmentId}", appointmentId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約キャンセルに失敗：{AppointmentId}", appointmentId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AppointmentInfo?> GetAppointmentAsync(string appointmentId, string? projectId = null)
    {
        var project = ResolveProject(projectId);

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(project);
            db.Open();

            var sql = @"
                SELECT
                    appointment_id,
                    customer_id,
                    vehicle_id,
                    appointment_type as service_type,
                    preferred_date as appointment_date_time,
                    status,
                    duration_minutes
                FROM service_appointments
                WHERE appointment_id = @AppointmentId";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { AppointmentId = appointmentId });

            if (result == null)
                return null;

            return new AppointmentInfo
            {
                AppointmentId = result.appointment_id,
                CustomerId = result.customer_id,
                ServiceType = result.service_type,
                AppointmentDateTime = result.appointment_date_time,
                Status = result.status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予約取得に失敗：{AppointmentId}", appointmentId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SlotAvailability> CheckAvailabilityAsync(
        string appointmentType,
        DateTime preferredDate,
        string preferredTime,
        string? projectId = null)
    {
        var project = ResolveProject(projectId);

        using var db = _dbConnectionFactory.CreateConnection(project);

        // 计算时间段(假设每个预约 1 小时)
        var start = preferredDate.Date + ParseTime(preferredTime);
        var end = start.AddHours(1);

        // 查询冲突预约
        var conflictCount = await db.QuerySingleAsync<int>(@"
            SELECT COUNT(*) FROM service_appointments
            WHERE appointment_type = @Type
              AND status NOT IN ('cancelled', 'no_show')
              AND preferred_date >= @Start
              AND preferred_date < @End",
            new { Type = appointmentType, Start = start, End = end });

        var maxSlotsPerTime = 2; // 默认每个时间段最大预约数

        return new SlotAvailability
        {
            IsAvailable = conflictCount < maxSlotsPerTime,
            ConflictCount = conflictCount,
            MaxSlots = maxSlotsPerTime,
            AlternativeSlots = await FindAlternativeSlotsAsync(appointmentType, preferredDate, projectId)
        };
    }

    /// <summary>
    /// 查找可替代的预约时间段
    /// </summary>
    private async Task<List<TimeSlotOption>> FindAlternativeSlotsAsync(
        string appointmentType,
        DateTime preferredDate,
        string? projectId = null)
    {
        // 返回同一天的可用时间段(9:00-18:00,每小时一段)
        var slots = new List<TimeSlotOption>();
        for (int hour = 9; hour < 18; hour++)
        {
            var time = $"{hour:D2}:00";
            var availability = await CheckAvailabilityAsync(appointmentType, preferredDate, time, projectId);
            if (availability.IsAvailable)
            {
                slots.Add(new TimeSlotOption { Time = time, IsAvailable = true });
            }
        }
        return slots;
    }

    /// <summary>
    /// 解析时间字符串为 TimeSpan
    /// </summary>
    private static TimeSpan ParseTime(string timeStr)
    {
        if (TimeSpan.TryParse(timeStr, out var result))
            return result;

        // 默认返回 9:00
        return TimeSpan.FromHours(9);
    }

    /// <summary>
    /// 予約枠が既に埋まっているかチェック
    /// </summary>
    private async Task<bool> IsSlotBookedAsync(IDbConnection db, DateTime startTime, DateTime endTime)
    {
        var sql = @"
            SELECT COUNT(*)
            FROM service_appointments
            WHERE status NOT IN ('cancelled', 'no_show')
            AND (
                (preferred_date >= @StartTime AND preferred_date < @EndTime)
                OR (preferred_date <= @StartTime AND datetime(preferred_date, '+' || duration_minutes || ' minutes') > @StartTime)
            )";

        var count = await db.ExecuteScalarAsync<int>(sql, new
        {
            StartTime = startTime,
            EndTime = endTime
        });

        return count > 0;
    }

    private static string GenerateAppointmentId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var guid = Guid.NewGuid().ToString("N")[..4];
        return $"APT-{timestamp}-{guid}";
    }
}
