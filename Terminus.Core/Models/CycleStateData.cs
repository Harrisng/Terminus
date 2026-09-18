namespace Terminus.Core.Models;

/// <summary>
/// 週期狀態資料（DPAPI 加密存檔用）。
/// </summary>
public class CycleStateData
{
    public string CycleId { get; set; } = "";
    public int QuotaRemainingMinutes { get; set; }
    public int DelayCount { get; set; }
    public int AutoDelayCount { get; set; }
    public string LastUpdated { get; set; } = "";
    public List<DelayRecord> History { get; set; } = new();
    /// <summary>當前狀態（重啟還原用）</summary>
    public string? State { get; set; }
    /// <summary>下次動作時間 ISO 格式（重啟還原用）</summary>
    public string? NextActionTime { get; set; }
}

/// <summary>
/// 延遲歷史記錄。
/// </summary>
public class DelayRecord
{
    /// <summary>點擊時間</summary>
    public string Timestamp { get; set; } = "";
    /// <summary>延遲分鐘數</summary>
    public int DurationMinutes { get; set; }
    /// <summary>延後到幾點幾分</summary>
    public string DelayedTo { get; set; } = "";
    /// <summary>"manual" or "auto"</summary>
    public string Type { get; set; } = "";
}
