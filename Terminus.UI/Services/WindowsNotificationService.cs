using Terminus.Core.Services;
using System.Windows;
using Terminus.UI.Windows;

namespace Terminus.UI.Services;

/// <summary>
/// All notifications use WarningDialog (dark-themed, topmost) for consistent UI.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo)
    {
        var timeStr = FormatTimeSpan(timeUntilWarning);
        ShowDialog("睡眠週期預警", "ℹ️",
            $"主警告將在 {timeStr} 後出現\n{firstClassInfo}");
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(WarningNotificationArgs args)
    {
        var quotaText = args.IsUnlimitedDelay
            ? "延後次數：無限制"
            : $"剩餘配額：{FormatTimeSpan(args.QuotaRemaining)}";
        ShowDialog(args.Title, "⚠️", args.Message, quotaText,
            showDelayButton: args.ShowDelayButton,
            showShutdownButton: args.ShowShutdownButton,
            quotaRemaining: args.QuotaRemaining,
            isUnlimitedDelay: args.IsUnlimitedDelay);
        return Task.CompletedTask;
    }

    public Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited)
    {
        var quotaText = isUnlimited
            ? "延後次數：無限制"
            : $"剩餘配額：{FormatTimeSpan(quotaRemaining)}";
        ShowDialog("已延後", "✅",
            $"下次警告：{FormatTimeSpan(delayedUntil)} 後", quotaText);
        return Task.CompletedTask;
    }

    public Task ShowAIModeStartedAsync(string taskDescription)
    {
        ShowDialog("AI 通宵模式", "🤖",
            $"{taskDescription}\n任務完成後系統將自動關機");
        return Task.CompletedTask;
    }

    public Task ShowAIModeCompletedAsync(string taskDescription, bool success)
    {
        var status = success ? "✓ 已成功完成" : "✗ 失敗";
        ShowDialog("AI 模式已完成", success ? "✅" : "⚠️",
            $"{taskDescription}\n{status}");
        return Task.CompletedTask;
    }

    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining)
    {
        ShowDialog("即將關機", "🚨",
            $"系統將在 {FormatTimeSpan(timeRemaining)} 後關機");
        return Task.CompletedTask;
    }

    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours)
    {
        var cacheInfo = cacheAgeHours.HasValue
            ? $"使用 {cacheAgeHours.Value:F1} 小時前的快取"
            : "無可用快取";
        ShowDialog("日曆獲取錯誤", "⚠️",
            $"連續失敗 {consecutiveFailures} 次\n{cacheInfo}");
        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync()
    {
        return Task.CompletedTask;
    }

    private void ShowDialog(string title, string icon, string message,
        string? quotaText = null, bool showDelayButton = false, bool showShutdownButton = false,
        TimeSpan? quotaRemaining = null, bool isUnlimitedDelay = false)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var dialog = new WarningDialog(title, message, quotaText,
                showDelayButton, showShutdownButton, icon,
                quotaRemaining, isUnlimitedDelay);
            dialog.Show();
            dialog.Activate();
        });
    }

    /// <summary>
    /// Formats a TimeSpan as "X 小時 Y 分鐘" or "Y 分鐘" or "Z 秒".
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays} 天 {ts.Hours} 小時";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} 小時 {ts.Minutes} 分鐘";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes} 分鐘";
        return $"{(int)ts.TotalSeconds} 秒";
    }
}
