using NodaTime;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

// ════════════════════════════════════════════════════════════
// 當地語系化服務
// ════════════════════════════════════════════════════════════

/// <summary>
/// 當地語系化服務介面。Core 專案用此介面取得使用者可見字串，避免直接依賴 WPF。
/// UI 專案負責實作（從 ResourceDictionary 查詢 + 格式化）。
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 依資源 key 取得當前語言字串。可傳入格式化參數（與 string.Format 相同語法）。
    /// 找不到 key 時回傳 "[key]" 標記，避免拋例外。
    /// </summary>
    string Get(string key, params object[] args);
}

/// <summary>
/// 預設實作：不依賴 WPF，直接回傳 key。給測試或未初始化的環境使用。
/// </summary>
public sealed class NullLocalizationService : ILocalizationService
{
    public string Get(string key, params object[] args) =>
        args.Length > 0 ? $"[{key}]{string.Join(",", args)}" : $"[{key}]";
}

// ════════════════════════════════════════════════════════════
// 週期狀態持久化服務
// ════════════════════════════════════════════════════════════

/// <summary>
/// 週期狀態持久化服務 — 負責以 DPAPI 加密存檔／還原配額、延遲次數、狀態與歷史記錄。
/// </summary>
public interface ICycleStateService
{
    /// <summary>將當前週期狀態加密存檔，可附帶一筆延遲歷史記錄。</summary>
    void SaveCycleState(BehaviorContext context, string? delayType = null, int delayMinutes = 0, string? delayedTo = null);

    /// <summary>若加密檔案存的是同一週期，恢復配額／延遲次數到 SleepCycle。</summary>
    void TryRestoreCycleState(SleepCycle cycle);

    /// <summary>從加密檔案還原上次存檔的狀態和下次動作時間到 BehaviorContext。</summary>
    void RestoreContextState(BehaviorContext context, ZonedDateTime hkNow);

    /// <summary>讀取加密狀態檔案（解密後的 CycleStateData，或 null）。</summary>
    CycleStateData? ReadState();

    /// <summary>直接寫入一筆 CycleStateData（加密）。</summary>
    void WriteState(CycleStateData state);
}

// ════════════════════════════════════════════════════════════
// 通知服務
// ════════════════════════════════════════════════════════════

/// <summary>
/// Notification service interface for Windows native notifications (T8).
/// Implementation will use Windows.UI.Notifications in the UI layer.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a pre-warning notification (30 minutes before main warning).
    /// </summary>
    Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo);

    /// <summary>
    /// Shows the main warning notification with action buttons.
    /// </summary>
    Task ShowWarningAsync(WarningNotificationArgs args);

    /// <summary>
    /// Shows a delay confirmation notification.
    /// </summary>
    Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited);

    /// <summary>
    /// Shows an AI mode started notification.
    /// </summary>
    Task ShowAIModeStartedAsync(string taskDescription);

    /// <summary>
    /// Shows an AI mode completed notification with webhook.
    /// </summary>
    Task ShowAIModeCompletedAsync(string taskDescription, bool success);

    /// <summary>
    /// Shows a shutdown imminent warning.
    /// </summary>
    Task ShowShutdownImminentAsync(TimeSpan timeRemaining);

    /// <summary>
    /// Shows a fetch error warning.
    /// </summary>
    Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours);

    /// <summary>
    /// Clears all active notifications.
    /// </summary>
    Task ClearAllNotificationsAsync();
}

public class WarningNotificationArgs
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required TimeSpan QuotaRemaining { get; init; }
    public required bool IsUnlimitedDelay { get; init; }
    public required bool ShowDelayButton { get; init; }
    public required bool ShowShutdownButton { get; init; }
    public required bool ShowAIModeButton { get; init; }
}

/// <summary>
/// Stub implementation for testing without Windows notifications.
/// </summary>
public class StubNotificationService : INotificationService
{
    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo)
    {
        Console.WriteLine($"[PRE-WARNING] Main warning in {timeUntilWarning.TotalMinutes:F0} minutes. {firstClassInfo}");
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(WarningNotificationArgs args)
    {
        Console.WriteLine($"[WARNING] {args.Title}: {args.Message}");
        Console.WriteLine($"  Quota: {args.QuotaRemaining.TotalMinutes:F0} min remaining (Unlimited: {args.IsUnlimitedDelay})");
        return Task.CompletedTask;
    }

    public Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited)
    {
        Console.WriteLine($"[DELAY] Delayed for {delayedUntil.TotalMinutes:F0} minutes. Quota: {quotaRemaining.TotalMinutes:F0} min");
        return Task.CompletedTask;
    }

    public Task ShowAIModeStartedAsync(string taskDescription)
    {
        Console.WriteLine($"[AI MODE] Started: {taskDescription}");
        return Task.CompletedTask;
    }

    public Task ShowAIModeCompletedAsync(string taskDescription, bool success)
    {
        Console.WriteLine($"[AI MODE] Completed: {taskDescription} (Success: {success})");
        return Task.CompletedTask;
    }

    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining)
    {
        Console.WriteLine($"[SHUTDOWN] Imminent in {timeRemaining.TotalSeconds:F0} seconds");
        return Task.CompletedTask;
    }

    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours)
    {
        Console.WriteLine($"[FETCH ERROR] {consecutiveFailures} consecutive failures. Cache age: {cacheAgeHours:F1} hours");
        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync()
    {
        Console.WriteLine("[CLEAR] All notifications cleared");
        return Task.CompletedTask;
    }
}
