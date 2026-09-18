using Terminus.Core.Services;
using System.Windows;
using Terminus.UI.Windows;

namespace Terminus.UI.Services;

/// <summary>
/// Windows notification service.
/// Warnings use a topmost WarningDialog with action buttons.
/// Other notifications use MessageBox parented to the MainWindow.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo)
    {
        ShowNotification("睡眠週期預警",
            $"主警告將在 {timeUntilWarning.TotalMinutes:F0} 分鐘後出現\n{firstClassInfo}",
            MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(WarningNotificationArgs args)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var dialog = new WarningDialog(args);
            dialog.Show();
            dialog.Activate();
        });
        return Task.CompletedTask;
    }

    public Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited)
    {
        var quotaText = isUnlimited
            ? "無限次延後"
            : $"剩餘配額: {quotaRemaining.TotalMinutes:F0} 分鐘";

        ShowNotification("已延後",
            $"下次警告: {delayedUntil.TotalMinutes:F0} 分鐘後\n{quotaText}",
            MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task ShowAIModeStartedAsync(string taskDescription)
    {
        ShowNotification("AI 通宵模式已啟動",
            $"{taskDescription}\n任務完成後系統將自動關機",
            MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task ShowAIModeCompletedAsync(string taskDescription, bool success)
    {
        var status = success ? "✓ 已成功完成" : "✗ 失敗";
        ShowNotification("AI 模式已完成",
            $"{taskDescription}\n{status}",
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining)
    {
        ShowNotification("⚠ 即將關機",
            $"系統將在 {timeRemaining.TotalSeconds:F0} 秒後關機",
            MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours)
    {
        var cacheInfo = cacheAgeHours.HasValue
            ? $"使用 {cacheAgeHours.Value:F1} 小時前的快取"
            : "無可用快取";

        ShowNotification("⚠ 日曆獲取錯誤",
            $"連續失敗 {consecutiveFailures} 次\n{cacheInfo}",
            MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync()
    {
        return Task.CompletedTask;
    }

    private void ShowNotification(string title, string message, MessageBoxImage icon)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var mainWindow = App.MainWindow;
            if (mainWindow != null)
            {
                // Bring MainWindow to front so the MessageBox is visible
                if (!mainWindow.IsVisible)
                    mainWindow.Show();
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
                mainWindow.Activate();

                MessageBox.Show(mainWindow, message, title, MessageBoxButton.OK, icon);
            }
            else
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            }
        });
    }
}
