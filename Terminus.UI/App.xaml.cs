using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using Terminus.Core.Services;
using Terminus.UI.Services;
using Terminus.UI.Themes;
using NodaTime;

namespace Terminus.UI;

public partial class App : Application
{
    private IHost? _host;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        DispatcherUnhandledException += (s, args) =>
        {
            Terminus.Core.Services.LoggerService.Error("App: DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"發生錯誤：{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Terminus 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Terminus.Core.Services.LoggerService.Error("App: AppDomain.UnhandledException", ex ?? new Exception("Unknown"));
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Terminus.Core.Services.LoggerService.Error("App: UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        try
        {
            // Disable WPF hardware acceleration to fix GPU rendering bug with dark colors
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Avalon.Graphics");
                key?.SetValue("DisableHWAcceleration", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }

            // Build dependency injection container
            _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<IClock>(SystemClock.Instance);
                services.AddSingleton<CacheService>();
                services.AddSingleton<System.Net.Http.HttpClient>();
                services.AddSingleton<ICalService>(sp => new ICalService(sp.GetRequiredService<System.Net.Http.HttpClient>()));
                services.AddSingleton<CalendarDataService>();
                services.AddSingleton<ShutdownService>();
                services.AddSingleton<RebootDetectionService>();
                services.AddSingleton<INotificationService, WindowsNotificationService>();
                services.AddSingleton<BehaviorOrchestrator>();

                // UI services
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<SettingsWindow>();
            })
            .Build();

            await _host.StartAsync();

            // Initialize theme service first
            var themeService = _host.Services.GetRequiredService<ThemeService>();

            // Create main window (but don't show yet for existing users)
            _mainWindow = _host.Services.GetRequiredService<MainWindow>();

            // Initialize tray icon
            _trayIconService = _host.Services.GetRequiredService<TrayIconService>();
            _trayIconService.Initialize();

            // Check if first run
            var settings = _host.Services.GetRequiredService<SettingsService>();
            var calendarUrl = settings.GetCalendarUrl();

            Terminus.Core.Services.LoggerService.Info($"App: 啟動, CalendarURL={(string.IsNullOrEmpty(calendarUrl) ? "空" : "已設置")}");

            // Always show main window on startup
            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Focus();

            if (string.IsNullOrEmpty(calendarUrl))
            {
                // First run: show settings page
                Terminus.Core.Services.LoggerService.Info("App: 首次使用, 跳轉到設定頁面");
                _mainWindow.NavigateToSettings();
            }
            else
            {
                // Existing user: start orchestrator and show dashboard
                Terminus.Core.Services.LoggerService.Info("App: 已有用戶, 啟動 orchestrator 並跳轉到儀表板");
                var orchestrator = _host.Services.GetRequiredService<BehaviorOrchestrator>();
                await orchestrator.StartAsync(calendarUrl);
                _mainWindow.NavigateToDashboard();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"啟動錯誤: {ex.Message}\n\n{ex.StackTrace}",
                "Terminus 啟動失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    public static MainWindow? MainWindow => ((App)Current)._mainWindow;
}
