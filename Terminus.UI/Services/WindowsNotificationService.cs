using Terminus.Core.Services;
using System.Windows;
using Terminus.UI.Windows;

namespace Terminus.UI.Services;

/// <summary>
/// All notifications use WarningDialog (dark-themed, topmost) for consistent UI.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    private readonly ILocalizationService _localization;

    public WindowsNotificationService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo)
    {
        var timeStr = FormatTimeSpan(timeUntilWarning);
        ShowDialog(_localization.Get("Notification_PreWarningTitle"), "ℹ️",
            _localization.Get("Notification_PreWarningBody", timeStr, firstClassInfo));
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(WarningNotificationArgs args)
    {
        var quotaText = args.IsUnlimitedDelay
            ? _localization.Get("Notification_UnlimitedQuota")
            : _localization.Get("Notification_QuotaRemainingFormat", FormatTimeSpan(args.QuotaRemaining));
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
            ? _localization.Get("Notification_UnlimitedQuota")
            : _localization.Get("Notification_QuotaRemainingFormat", FormatTimeSpan(quotaRemaining));
        ShowDialog(_localization.Get("Notification_DelayedTitle"), "✅",
            _localization.Get("Notification_DelayedBody", FormatTimeSpan(delayedUntil)), quotaText);
        return Task.CompletedTask;
    }

    public Task ShowAIModeStartedAsync(string taskDescription)
    {
        ShowDialog(_localization.Get("Notification_AIModeStartedTitle"), "🤖",
            _localization.Get("Notification_AIModeStartedBody", taskDescription));
        return Task.CompletedTask;
    }

    public Task ShowAIModeCompletedAsync(string taskDescription, bool success)
    {
        var status = success
            ? _localization.Get("Notification_AIModeSuccess")
            : _localization.Get("Notification_AIModeFailed");
        ShowDialog(_localization.Get("Notification_AIModeCompletedTitle"), success ? "✅" : "⚠️",
            _localization.Get("Notification_AIModeCompletedBody", taskDescription, status));
        return Task.CompletedTask;
    }

    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining)
    {
        var message = _localization.Get("Warning_ForceShutdownBody", FormatTimeSpan(timeRemaining));
        ShowForceDialog(_localization.Get("Warning_ForceShutdownTitle"), "🚨", message);
        return Task.CompletedTask;
    }

    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours)
    {
        var cacheInfo = cacheAgeHours.HasValue
            ? _localization.Get("Notification_CacheAgeFormat", cacheAgeHours.Value)
            : _localization.Get("Notification_NoCache");
        ShowDialog(_localization.Get("Notification_FetchErrorTitle"), "⚠️",
            _localization.Get("Notification_FetchErrorBody", consecutiveFailures, cacheInfo));
        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 顯示強制（不可關閉、無操作按鈕）對話框，用於硬關機倒數提醒。
    /// </summary>
    private void ShowForceDialog(string title, string icon, string message)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var dialog = new WarningDialog(title, message, icon: icon, forceNonClosable: true);
            WarningDialog.ShowSingleton(dialog);
        });
    }

    private void ShowDialog(string title, string icon, string message,
        string? quotaText = null, bool showDelayButton = false, bool showShutdownButton = false,
        TimeSpan? quotaRemaining = null, bool isUnlimitedDelay = false)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var dialog = new WarningDialog(title, message, quotaText,
                showDelayButton, showShutdownButton, icon,
                quotaRemaining, isUnlimitedDelay,
                onDelay: async duration =>
                {
                    var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
                    if (orchestrator != null)
                        await orchestrator.OnDelayRequestedAsync(duration);
                },
                onShutdown: async () =>
                {
                    var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
                    if (orchestrator != null)
                        await orchestrator.OnShutdownNowRequestedAsync();
                });
            WarningDialog.ShowSingleton(dialog);
        });
    }

    /// <summary>
    /// Formats a TimeSpan as localized "X 小時 Y 分鐘" / "Y 分鐘" / "Z 秒".
    /// </summary>
    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return _localization.Get("Format_DayHour", (int)ts.TotalDays, ts.Hours);
        if (ts.TotalHours >= 1)
            return _localization.Get("Format_HoursMinutes", (int)ts.TotalHours, ts.Minutes);
        if (ts.TotalMinutes >= 1)
            return _localization.Get("Format_MinuteOnly", (int)ts.TotalMinutes);
        return _localization.Get("Format_Second", (int)ts.TotalSeconds);
    }
}
