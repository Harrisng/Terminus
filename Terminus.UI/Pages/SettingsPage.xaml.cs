using System.Windows;
using System.Windows.Controls;
using Terminus.Core.Services;
using Terminus.UI.Services;
using Terminus.UI.Themes;
using Terminus.UI.Windows;

namespace Terminus.UI.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService = null!;
    private readonly CacheService _cacheService = null!;
    private readonly ThemeService _themeService = null!;
    private readonly LanguageService? _languageService;
    private bool _isInitialized = false;

    public SettingsPage(SettingsService settingsService, CacheService cacheService, ThemeService themeService, LanguageService? languageService = null)
    {
        try
        {
            _settingsService = settingsService;
            _cacheService = cacheService;
            _themeService = themeService;
            _languageService = languageService;

            InitializeComponent();

            LoadSettings();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 初始化失敗", ex);
            var dialog = new WarningDialog("初始化錯誤",
                $"設定頁面初始化失敗：{ex.Message}", icon: "❌");
            WarningDialog.ShowSingleton(dialog);
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
        CutoffHourTextBox.Text = _settingsService.GetEarlyClassCutoffHour().ToString();
        DelayQuotaTextBox.Text = _settingsService.GetEarlyClassDelayQuotaMinutes().ToString();
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
                ShowWarning("驗證錯誤", "請輸入日曆 URL");
                return;
            }

            if (!int.TryParse(SleepTimeTextBox.Text, out var sleepMinutes) || sleepMinutes <= 0)
            {
                ShowWarning("驗證錯誤", "睡眠時間無效");
                return;
            }

            if (!int.TryParse(PreSleepTextBox.Text, out var preSleepMinutes) || preSleepMinutes < 0)
            {
                ShowWarning("驗證錯誤", "睡前準備時間無效");
                return;
            }

            if (!int.TryParse(WashTextBox.Text, out var washMinutes) || washMinutes <= 0)
            {
                ShowWarning("驗證錯誤", "梳洗時間無效");
                return;
            }

            if (!int.TryParse(BreakfastTextBox.Text, out var breakfastMinutes) || breakfastMinutes <= 0)
            {
                ShowWarning("驗證錯誤", "早餐時間無效");
                return;
            }

            if (!int.TryParse(CommuteTextBox.Text, out var commuteMinutes) || commuteMinutes <= 0)
            {
                ShowWarning("驗證錯誤", "通勤時間無效");
                return;
            }

            if (!int.TryParse(CutoffHourTextBox.Text, out var cutoffHour) || cutoffHour < 0 || cutoffHour > 23)
            {
                ShowWarning("驗證錯誤", "早課分界時刻無效（需 0-23）");
                return;
            }

            if (!int.TryParse(DelayQuotaTextBox.Text, out var delayQuota) || delayQuota < 0)
            {
                ShowWarning("驗證錯誤", "早課日延後額度無效");
                return;
            }

            _settingsService.SetCalendarUrl(url);
            _settingsService.SetSleepTime(TimeSpan.FromMinutes(sleepMinutes));
            _settingsService.SetPreSleepBuffer(TimeSpan.FromMinutes(preSleepMinutes));
            _settingsService.SetWashTime(TimeSpan.FromMinutes(washMinutes));
            _settingsService.SetBreakfastTime(TimeSpan.FromMinutes(breakfastMinutes));
            _settingsService.SetCommuteTime(TimeSpan.FromMinutes(commuteMinutes));
            _settingsService.SetEarlyClassCutoffHour(cutoffHour);
            _settingsService.SetEarlyClassDelayQuotaMinutes(delayQuota);
            _settingsService.SetStartWithWindows(StartWithWindowsCheckBox.IsChecked ?? false);

            // Immediately restart orchestrator with new URL
            var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
            {
                App.ApplyOrchestratorSettings(orchestrator, _settingsService);
                await orchestrator.RestartAsync(url);
            }

            var successDialog = new WarningDialog("成功", "設定已儲存，日曆數據已即時載入。", icon: "✅");
            WarningDialog.ShowSingleton(successDialog);

            // Navigate to dashboard to show updated data
            if (App.MainWindow != null)
            {
                App.MainWindow.NavigateToDashboard();
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 儲存設定失敗", ex);
            var dialog = new WarningDialog("錯誤", $"儲存設定失敗：{ex.Message}", icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WarningDialog("確認", "確定要清除所有快取的日曆數據嗎？", icon: "🗑️");
        dialog.SetConfirmMode("確認清除", isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            _cacheService.ClearAll();
            var successDialog = new WarningDialog("成功", "快取已清除成功。", icon: "✅");
            WarningDialog.ShowSingleton(successDialog);
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
            var logsWindow = new LogsWindow
            {
                Owner = Window.GetWindow(this)
            };
            logsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 無法開啟日誌視窗", ex);
            var dialog = new WarningDialog("錯誤", $"無法開啟日誌視窗：{ex.Message}", icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void ViewDelayHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var historyWindow = new DelayHistoryWindow
            {
                Owner = Window.GetWindow(this)
            };
            historyWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 無法開啟延遲歷史視窗", ex);
            var dialog = new WarningDialog("錯誤", $"無法開啟延遲歷史視窗：{ex.Message}", icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    /// <summary>顯示警告對話框（取代 MessageBox）。</summary>
    private void ShowWarning(string title, string message)
    {
        var dialog = new WarningDialog(title, message, icon: "⚠️");
        WarningDialog.ShowSingleton(dialog);
    }
}
