using Terminus.Core.Services;
using System.Runtime.InteropServices;
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
        // 全螢幕遊戲/簡報中不打擾（行為迴圈不受影響，自動延後照舊）
        if (IsUserInFullScreenApp())
        {
            LoggerService.Info($"通知已抑制（偵測到全螢幕應用）：{title}");
            return;
        }
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
    /// 偵測使用者是否在全螢幕遊戲/簡報中（Windows 通知系統同款 API）。
    /// QUNS_BUSY=2、QUNS_RUNNING_D3D_FULL_SCREEN=3、QUNS_PRESENTATION_MODE=4。
    /// </summary>
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private static bool IsUserInFullScreenApp()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4)
                return true;
        }
        catch { }
        return IsForegroundBorderlessFullScreen();
    }

    // ── 無邊框全螢幕遊戲兜底：前景視窗鋪滿螢幕且無標題列 ──
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private static bool IsForegroundBorderlessFullScreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return false; // 自己不算
            var style = GetWindowLong(hwnd, GWL_STYLE);
            if ((style & WS_CAPTION) == WS_CAPTION) return false; // 有標題列＝一般視窗
            if (!GetWindowRect(hwnd, out var r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
                && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }
        catch
        {
            return false;
        }
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
