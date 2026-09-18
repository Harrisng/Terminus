using System.Windows;
using System.Windows.Controls;
using Terminus.Core.Services;
using Terminus.UI.Services;
using Terminus.UI.Themes;

namespace Terminus.UI.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService;
    private readonly CacheService _cacheService;
    private readonly ThemeService _themeService;
    private bool _isInitialized = false;

    public SettingsPage(SettingsService settingsService, CacheService cacheService, ThemeService themeService)
    {
        try
        {
            _settingsService = settingsService;
            _cacheService = cacheService;
            _themeService = themeService;

            InitializeComponent();

            LoadSettings();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定頁面初始化失敗：{ex.Message}\n\n{ex.StackTrace}",
                "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSettings()
    {
        CalendarUrlTextBox.Text = _settingsService.GetCalendarUrl();
        SleepTimeTextBox.Text = _settingsService.GetSleepTime().TotalMinutes.ToString();
        PreSleepTextBox.Text = _settingsService.GetPreSleepBuffer().TotalMinutes.ToString();
        WashTextBox.Text = _settingsService.GetWashTime().TotalMinutes.ToString();
        BreakfastTextBox.Text = _settingsService.GetBreakfastTime().TotalMinutes.ToString();
        CommuteTextBox.Text = _settingsService.GetCommuteTime().TotalMinutes.ToString();
        StartWithWindowsCheckBox.IsChecked = _settingsService.GetStartWithWindows();
        DarkModeCheckBox.IsChecked = _themeService.CurrentTheme == AppTheme.Dark;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = CalendarUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("請輸入日曆 URL", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(SleepTimeTextBox.Text, out var sleepMinutes) || sleepMinutes <= 0)
            {
                MessageBox.Show("睡眠時間無效", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PreSleepTextBox.Text, out var preSleepMinutes) || preSleepMinutes < 0)
            {
                MessageBox.Show("睡前準備時間無效", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(WashTextBox.Text, out var washMinutes) || washMinutes <= 0)
            {
                MessageBox.Show("梳洗時間無效", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(BreakfastTextBox.Text, out var breakfastMinutes) || breakfastMinutes <= 0)
            {
                MessageBox.Show("早餐時間無效", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(CommuteTextBox.Text, out var commuteMinutes) || commuteMinutes <= 0)
            {
                MessageBox.Show("通勤時間無效", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settingsService.SetCalendarUrl(url);
            _settingsService.SetSleepTime(TimeSpan.FromMinutes(sleepMinutes));
            _settingsService.SetPreSleepBuffer(TimeSpan.FromMinutes(preSleepMinutes));
            _settingsService.SetWashTime(TimeSpan.FromMinutes(washMinutes));
            _settingsService.SetBreakfastTime(TimeSpan.FromMinutes(breakfastMinutes));
            _settingsService.SetCommuteTime(TimeSpan.FromMinutes(commuteMinutes));
            _settingsService.SetStartWithWindows(StartWithWindowsCheckBox.IsChecked ?? false);

            // Immediately restart orchestrator with new URL
    var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
            {
                await orchestrator.RestartAsync(url);
            }

            MessageBox.Show("設定已儲存，日曆數據已即時載入。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);

            // Navigate to dashboard to show updated data
            if (App.MainWindow != null)
            {
                App.MainWindow.NavigateToDashboard();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"儲存設定失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("確定要清除所有快取的日曆數據嗎？",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _cacheService.ClearAll();
            MessageBox.Show("快取已清除成功。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DarkModeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _themeService == null) return;
        _themeService.SetTheme(AppTheme.Dark);
    }

    private void DarkModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _themeService == null) return;
        _themeService.SetTheme(AppTheme.Light);
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logsWindow = new Windows.LogsWindow
            {
                Owner = Window.GetWindow(this)
            };
            logsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法開啟日誌視窗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
