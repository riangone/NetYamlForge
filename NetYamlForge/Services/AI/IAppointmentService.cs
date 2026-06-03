namespace NetYamlForge.Services.AI;

/// <summary>
/// 予約サービスインターフェース
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// 空き枠を検索
    /// </summary>
    Task<List<TimeSlot>> GetAvailableSlotsAsync(DateTime startDate, int days = 7, string? projectId = null);

    /// <summary>
    /// 予約を作成
    /// </summary>
    Task<AppointmentResult> CreateAppointmentAsync(AppointmentRequest request, string? projectId = null);

    /// <summary>
    /// 予約を変更
    /// </summary>
    Task<AppointmentResult> UpdateAppointmentAsync(string appointmentId, AppointmentUpdateRequest request, string? projectId = null);

    /// <summary>
    /// 予約をキャンセル
    /// </summary>
    Task<bool> CancelAppointmentAsync(string appointmentId, string? projectId = null);

    /// <summary>
    /// 予約詳細を取得
    /// </summary>
    Task<AppointmentInfo?> GetAppointmentAsync(string appointmentId, string? projectId = null);

    /// <summary>
    /// 指定时间段的预约可用性检查(档期冲突检测)
    /// </summary>
    Task<SlotAvailability> CheckAvailabilityAsync(
        string appointmentType,
        DateTime preferredDate,
        string preferredTime,
        string? projectId = null);
}

/// <summary>
/// 时间段可用性结果
/// </summary>
public class SlotAvailability
{
    public bool IsAvailable { get; set; }
    public int ConflictCount { get; set; }
    public int MaxSlots { get; set; }
    public List<TimeSlotOption> AlternativeSlots { get; set; } = new();
}

/// <summary>
/// 可选时间段
/// </summary>
public class TimeSlotOption
{
    public string Time { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}

/// <summary>
/// 予約リクエスト
/// </summary>
public class AppointmentRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string? VehicleId { get; set; }
    public DateTime PreferredDateTime { get; set; }
    public Dictionary<string, string>? Details { get; set; }
}

/// <summary>
/// 予約更新リクエスト
/// </summary>
public class AppointmentUpdateRequest
{
    public DateTime? NewDateTime { get; set; }
    public string? ServiceType { get; set; }
    public Dictionary<string, string>? Details { get; set; }
}

/// <summary>
/// 予約結果
/// </summary>
public class AppointmentResult
{
    public bool Success { get; set; }
    public string? AppointmentId { get; set; }
    public string? ConfirmationNumber { get; set; }
    public DateTime? ConfirmedDateTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 予約情報
/// </summary>
public class AppointmentInfo
{
    public string AppointmentId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public DateTime AppointmentDateTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string>? Details { get; set; }
}

/// <summary>
/// 時間枠
/// </summary>
public class TimeSlot
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
