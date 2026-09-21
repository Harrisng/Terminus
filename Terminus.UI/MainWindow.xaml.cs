using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Terminus.Core.Services;
using Terminus.UI.Pages;
using Terminus.UI.Services;
using Terminus.UI.Themes;

namespace Terminus.UI;

public partial class MainWindow : Window
{
    private readonly BehaviorOrchestrator _orchestrator = null!;
    private readonly CalendarDataService _calendarService = null!;
    private readonly ThemeService _themeService = null!;

    private DashboardPage? _dashboardPage;
    private CalendarPage? _calendarPage;
    private SettingsPage? _settingsPage;

    public MainWindow(BehaviorOrchestrator orchestrator, CalendarDataService calendarService, ThemeService themeService)
    {
        try
        {
            _orchestrator = orchestrator;
            _calendarService = calendarService;
            _themeService = themeService;

            InitializeComponent();

            _themeService.ThemeChanged += OnThemeChanged;
            UpdateThemeButton();

            NavigateToDashboard();
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 初始化失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_InitError") as string ?? "初始化錯誤",
                string.Format(TryFindResource("Window_InitErrorBody") as string ?? "主視窗初始化失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
    {
        if (_themeService.CurrentTheme == AppTheme.Dark)
        {
            ThemeIcon.Text = "🌙";
            ThemeText.SetResourceReference(TextBlock.TextProperty, "Nav_DarkMode");
        }
        else
        {
            ThemeIcon.Text = "☀️";
            ThemeText.SetResourceReference(TextBlock.TextProperty, "Nav_LightMode");
        }
    }

    public void NavigateToDashboard()
    {
        try
        {
            if (_dashboardPage == null)
            {
                _dashboardPage = new DashboardPage(_orchestrator);
            }
            MainFrame.Navigate(_dashboardPage);
            PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Dashboard");
            SetActiveNav(NavDashboard);
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入儀表板失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_DashboardLoadFailed") as string ?? "載入儀表板失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    public void RefreshDashboard()
    {
        _dashboardPage?.UpdateDisplaySafe();
    }

    private void NavigateToCalendar()
    {
        try
        {
            if (_calendarPage == null)
            {
                _calendarPage = new CalendarPage(_calendarService);
            }
            _calendarPage.RefreshEvents();
            MainFrame.Navigate(_calendarPage);
            PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Calendar");
            SetActiveNav(NavCalendar);
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入月曆失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_CalendarLoadFailed") as string ?? "載入月曆失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    public void NavigateToSettings()
    {
        try
        {
            if (_settingsPage == null)
            {
                var settingsService = App.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                var cacheService = App.Services.GetService(typeof(CacheService)) as CacheService;
                if (settingsService != null && cacheService != null)
                {
                    var languageService = App.Services.GetService(typeof(LanguageService)) as LanguageService;
                    _settingsPage = new SettingsPage(settingsService, cacheService, _themeService, languageService);
                }
            }
            if (_settingsPage != null)
            {
                MainFrame.Navigate(_settingsPage);
                PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Settings");
                SetActiveNav(NavSettings);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入設定頁面失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_SettingsLoadFailed") as string ?? "載入設定頁面失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    private void SetActiveNav(Button activeButton)
    {
        var navButtons = new[] { NavDashboard, NavCalendar, NavSettings };
        foreach (var btn in navButtons)
        {
            btn.Style = (Style)FindResource("NavItemStyle");
        }
        activeButton.Style = (Style)FindResource("NavItemActiveStyle");
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateToDashboard();
    }

    private void NavCalendar_Click(object sender, RoutedEventArgs e)
    {
        NavigateToCalendar();
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _themeService.ToggleTheme();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
