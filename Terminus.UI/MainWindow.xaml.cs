using System.Windows;
using System.Windows.Input;
using Terminus.Core.Services;
using Terminus.UI.Pages;
using Terminus.UI.Themes;

namespace Terminus.UI;

public partial class MainWindow : Window
{
    private readonly BehaviorOrchestrator _orchestrator;
    private readonly CalendarDataService _calendarService;
    private readonly ThemeService _themeService;

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
            MessageBox.Show($"主視窗初始化失敗：{ex.Message}\n\n{ex.StackTrace}",
                "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ThemeText.Text = "深色模式";
        }
        else
        {
            ThemeIcon.Text = "☀️";
            ThemeText.Text = "淺色模式";
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
            PageTitle.Text = "儀表板";
            SetActiveNav(NavDashboard);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"載入儀表板失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
            PageTitle.Text = "月曆";
            SetActiveNav(NavCalendar);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"載入月曆失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    _settingsPage = new SettingsPage(settingsService, cacheService, _themeService);
                }
            }
            if (_settingsPage != null)
            {
                MainFrame.Navigate(_settingsPage);
                PageTitle.Text = "設定";
                SetActiveNav(NavSettings);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"載入設定頁面失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetActiveNav(System.Windows.Controls.Button activeButton)
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
