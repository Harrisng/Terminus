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
            var title = TryFindResource("Window_InitError") as string ?? "初始化錯誤";
            var bodyFmt = TryFindResource("Settings_InitError") as string ?? "設定頁面初始化失敗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
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

        // Initialize language combo (suppress SelectionChanged during init)
        if (_languageService != null)
        {
            var current = _languageService.CurrentLanguage;
            ComboBoxItem? item = current switch
            {
                LanguageCodes.SimplifiedChinese => LangZhCNItem,
                LanguageCodes.English => LangEnUSItem,
                _ => LangZhTWItem
            };
            if (item != null)
                LanguageComboBox.SelectedItem = item;
        }
    }

    /// <summary>語言下拉變更：即時切換並持久化。</summary>
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _languageService == null) return;
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
        var code = item.Tag as string;
        if (string.IsNullOrEmpty(code)) return;
        if (code == _languageService.CurrentLanguage) return;
        _languageService.SetLanguage(code);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = CalendarUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_EmptyUrl");
                return;
            }

            if (!int.TryParse(SleepTimeTextBox.Text, out var sleepMinutes) || sleepMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidSleep");
                return;
            }

            if (!int.TryParse(PreSleepTextBox.Text, out var preSleepMinutes) || preSleepMinutes < 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidPreSleep");
                return;
            }

            if (!int.TryParse(WashTextBox.Text, out var washMinutes) || washMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidWash");
                return;
            }

            if (!int.TryParse(BreakfastTextBox.Text, out var breakfastMinutes) || breakfastMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidBreakfast");
                return;
            }

            if (!int.TryParse(CommuteTextBox.Text, out var commuteMinutes) || commuteMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidCommute");
                return;
            }

            if (!int.TryParse(CutoffHourTextBox.Text, out var cutoffHour) || cutoffHour < 0 || cutoffHour > 23)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidCutoff");
                return;
            }

            if (!int.TryParse(DelayQuotaTextBox.Text, out var delayQuota) || delayQuota < 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidQuota");
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

            var successTitle = TryFindResource("Common_Success") as string ?? "成功";
            var successBody = TryFindResource("Settings_SettingsSavedSuccess") as string ?? "設定已儲存，日曆數據已即時載入。";
            var successDialog = new WarningDialog(successTitle, successBody, icon: "✅");
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
            var errTitle = TryFindResource("Common_Error") as string ?? "錯誤";
            var errFmt = TryFindResource("Settings_SaveFailed") as string ?? "儲存設定失敗：{0}";
            var dialog = new WarningDialog(errTitle, string.Format(errFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TryFindResource("Common_Confirm") as string ?? "確認";
        var body = TryFindResource("Settings_ClearCacheConfirm") as string ?? "確定要清除所有快取的日曆數據嗎？";
        var dialog = new WarningDialog(title, body, icon: "🗑️");
        var btnText = TryFindResource("Settings_ConfirmClearCache") as string ?? "確認清除";
        dialog.SetConfirmMode(btnText, isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            _cacheService.ClearAll();
            var successTitle = TryFindResource("Common_Success") as string ?? "成功";
            var successBody = TryFindResource("Settings_CacheClearedSuccess") as string ?? "快取已清除成功。";
            var successDialog = new WarningDialog(successTitle, successBody, icon: "✅");
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
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var bodyFmt = TryFindResource("Settings_LogsOpenFail") as string ?? "無法開啟日誌視窗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
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
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var bodyFmt = TryFindResource("Settings_HistoryOpenFail") as string ?? "無法開啟延遲歷史視窗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    /// <summary>顯示警告對話框（取代 MessageBox）。傳入資源 key。</summary>
    private void ShowWarning(string titleKey, string messageKey)
    {
        var title = TryFindResource(titleKey) as string ?? titleKey;
        var message = TryFindResource(messageKey) as string ?? messageKey;
        var dialog = new WarningDialog(title, message, icon: "⚠️");
        WarningDialog.ShowSingleton(dialog);
    }
}
