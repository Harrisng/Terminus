using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;
using System.Windows.Controls;
using Terminus.Core.Services;

namespace Terminus.UI.Services;

/// <summary>
/// System tray icon service with context menu.
/// </summary>
public class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly BehaviorOrchestrator _orchestrator;

    public TrayIconService(BehaviorOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadApplicationIcon(),
            ToolTipText = "Terminus - 睡眠週期管理",
            Visibility = Visibility.Visible
        };

        // Create context menu
        var contextMenu = new ContextMenu();

        // Status item
        var statusItem = new MenuItem
        {
            Header = "狀態：閒置中",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(new Separator());

        // Show main window
        var showItem = new MenuItem { Header = "📊 開啟主視窗" };
        showItem.Click += (s, e) => BringMainWindowToFront(navigateToSettings: false);
        contextMenu.Items.Add(showItem);

        // Settings
        var settingsItem = new MenuItem { Header = "⚙️ 設定" };
        settingsItem.Click += (s, e) => BringMainWindowToFront(navigateToSettings: true);
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new Separator());

        // Quick actions
        var delayItem = new MenuItem { Header = "⏰ 延後 30 分鐘" };
        delayItem.Click += async (s, e) =>
        {
            var success = await _orchestrator.OnDelayRequestedAsync();
            if (!success)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var feedback = new Windows.WarningDialog("無法延遲",
                        "目前不在警告階段，無法手動延遲。\n請等待警告出現後再操作。", icon: "ℹ️");
                    Windows.WarningDialog.ShowSingleton(feedback);
                });
            }
        };
        contextMenu.Items.Add(delayItem);

        var shutdownItem = new MenuItem { Header = "⚡ 立即關機" };
        shutdownItem.Click += (s, e) =>
        {
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var dialog = new Windows.WarningDialog("確認關機", "確定要立即關機嗎？\n此操作無法復原。", icon: "⚡");
                dialog.SetConfirmMode("立即關機", isDanger: true);
                dialog.ShowDialog();
                if (dialog.DialogResult == true)
                {
                    _ = _orchestrator.OnShutdownNowRequestedAsync();
                }
            });
        };
        contextMenu.Items.Add(shutdownItem);

        contextMenu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem { Header = "❌ 結束程式" };
        exitItem.Click += async (s, e) =>
        {
            await _orchestrator.StopAsync();
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;

        // Left click - show main window
        _trayIcon.TrayLeftMouseUp += (s, e) => BringMainWindowToFront(navigateToSettings: false);

        // Subscribe to state changes
        _orchestrator.StateChanged += (s, e) =>
        {
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var stateText = e.NewState switch
                {
                    Core.Models.BehaviorState.Idle => "閒置中",
                    Core.Models.BehaviorState.PreWarning => "預警階段",
                    Core.Models.BehaviorState.Warning => "警告中",
                    Core.Models.BehaviorState.Delayed => "已延後",
                    Core.Models.BehaviorState.AutoDelaying => "自動延後中",
                    Core.Models.BehaviorState.ForceShutdown => "強制關機倒數",
                    Core.Models.BehaviorState.ShuttingDown => "關機中",
                    Core.Models.BehaviorState.AIMode => "AI 模式",
                    Core.Models.BehaviorState.Disabled => "已停用",
                    _ => e.NewState.ToString()
                };
                statusItem.Header = $"狀態：{stateText}";
                _trayIcon.ToolTipText = $"Terminus - {stateText}";
            });
        };
    }

    /// <summary>
    /// 將主視窗帶到前景。navigateToSettings=true 時同時跳轉到設定頁。
    /// （合併原本幾乎相同的 ShowMainWindow / ShowSettings 兩個方法）
    /// </summary>
    private void BringMainWindowToFront(bool navigateToSettings)
    {
        var mainWindow = App.MainWindow;
        if (mainWindow == null)
        {
            LoggerService.Warn("TrayIconService: 主視窗尚未初始化");
            var dialog = new Windows.WarningDialog("錯誤", "主視窗尚未初始化", icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
            return;
        }

        if (!mainWindow.IsVisible)
            mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;

        // Topmost trick to force foreground, THEN activate/focus after
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;
        mainWindow.Activate();
        mainWindow.Focus();

        if (navigateToSettings)
            mainWindow.NavigateToSettings();
    }

    private System.Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Terminus.ico");
            if (System.IO.File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath, 32, 32);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"TrayIconService: 無法載入圖示: {ex.Message}");
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
