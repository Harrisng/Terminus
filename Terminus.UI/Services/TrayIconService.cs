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
        showItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        // Settings
        var settingsItem = new MenuItem { Header = "⚙️ 設定" };
        settingsItem.Click += (s, e) => ShowSettings();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new Separator());

        // Quick actions
        var delayItem = new MenuItem { Header = "⏰ 延遲 30 分鐘" };
        delayItem.Click += async (s, e) => await _orchestrator.OnDelayRequestedAsync();
        contextMenu.Items.Add(delayItem);

        var shutdownItem = new MenuItem { Header = "⚡ 立即關機" };
        shutdownItem.Click += async (s, e) => await _orchestrator.OnShutdownNowRequestedAsync();
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
        _trayIcon.TrayLeftMouseUp += (s, e) => ShowMainWindow();

        // Subscribe to state changes
        _orchestrator.StateChanged += (s, e) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var stateText = e.NewState switch
                {
                    Core.Models.BehaviorState.Idle => "閒置中",
                    Core.Models.BehaviorState.PreWarning => "預警階段",
                    Core.Models.BehaviorState.Warning => "警告中",
                    Core.Models.BehaviorState.Delayed => "已延後",
                    Core.Models.BehaviorState.AutoDelaying => "自動延後中",
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

    private void ShowMainWindow()
    {
        var mainWindow = App.MainWindow;
        if (mainWindow == null)
        {
            MessageBox.Show("主視窗尚未初始化", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }

    private void ShowSettings()
    {
        var mainWindow = App.MainWindow;
        if (mainWindow == null)
        {
            MessageBox.Show("主視窗尚未初始化", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!mainWindow.IsVisible)
            mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;

        mainWindow.Topmost = true;
        mainWindow.Topmost = false;
        mainWindow.Activate();
        mainWindow.Focus();
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
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
