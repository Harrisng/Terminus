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
    private readonly ILocalizationService _localization;

    public TrayIconService(BehaviorOrchestrator orchestrator, ILocalizationService localization)
    {
        _orchestrator = orchestrator;
        _localization = localization;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadApplicationIcon(),
            ToolTipText = _localization.Get("Tray_Tooltip"),
            Visibility = Visibility.Visible
        };

        // Create context menu
        var contextMenu = new ContextMenu();

        // Status item
        var statusItem = new MenuItem
        {
            Header = _localization.Get("Tray_StatusLabelInitial"),
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(new Separator());

        // Show main window
        var showItem = new MenuItem { Header = _localization.Get("Tray_ShowMainWindow") };
        showItem.Click += (s, e) => BringMainWindowToFront(navigateToSettings: false);
        contextMenu.Items.Add(showItem);

        // Settings
        var settingsItem = new MenuItem { Header = _localization.Get("Tray_Settings") };
        settingsItem.Click += (s, e) => BringMainWindowToFront(navigateToSettings: true);
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new Separator());

        // Quick actions
        var delayItem = new MenuItem { Header = _localization.Get("Tray_QuickDelay") };
        delayItem.Click += async (s, e) =>
        {
            var success = await _orchestrator.OnDelayRequestedAsync();
            if (!success)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var feedback = new Windows.WarningDialog(
                        _localization.Get("Warning_UnableToDelay"),
                        _localization.Get("Warning_NotInWarningState"), icon: "ℹ️");
                    Windows.WarningDialog.ShowSingleton(feedback);
                });
            }
        };
        contextMenu.Items.Add(delayItem);

        var shutdownItem = new MenuItem { Header = _localization.Get("Tray_ShutdownNow") };
        shutdownItem.Click += (s, e) =>
        {
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var dialog = new Windows.WarningDialog(
                    _localization.Get("Warning_ConfirmShutdown"),
                    _localization.Get("Warning_ConfirmShutdownMsg"), icon: "⚡");
                dialog.SetConfirmMode(_localization.Get("Warning_ShutdownConfirmText"), isDanger: true);
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
        var exitItem = new MenuItem { Header = _localization.Get("Tray_Exit") };
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
                    Core.Models.BehaviorState.Idle => _localization.Get("State_Idle"),
                    Core.Models.BehaviorState.PreWarning => _localization.Get("State_PreWarning"),
                    Core.Models.BehaviorState.Warning => _localization.Get("State_Warning"),
                    Core.Models.BehaviorState.Delayed => _localization.Get("State_Delayed"),
                    Core.Models.BehaviorState.AutoDelaying => _localization.Get("State_AutoDelaying"),
                    Core.Models.BehaviorState.ForceShutdown => _localization.Get("State_ForceShutdown"),
                    Core.Models.BehaviorState.ShuttingDown => _localization.Get("State_ShuttingDown"),
                    Core.Models.BehaviorState.AIMode => _localization.Get("State_AIMode"),
                    Core.Models.BehaviorState.Disabled => _localization.Get("State_Disabled"),
                    _ => e.NewState.ToString()
                };
                statusItem.Header = _localization.Get("Tray_StatusLabel", stateText);
                _trayIcon.ToolTipText = _localization.Get("Tray_TooltipWithState", stateText);
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
            var dialog = new Windows.WarningDialog(
                _localization.Get("Common_Error"),
                _localization.Get("Tray_MainWindowNotInit"), icon: "❌");
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
