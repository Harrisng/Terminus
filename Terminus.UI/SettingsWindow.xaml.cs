using System.Windows;
using System.Windows.Input;
using Terminus.Core.Services;
using Terminus.UI.Services;
using Terminus.UI.Windows;

namespace Terminus.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly CacheService _cacheService;

    public SettingsWindow(SettingsService settingsService, CacheService cacheService)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _cacheService = cacheService;

        LoadSettings();
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
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate calendar URL
            var url = CalendarUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_EmptyUrl");
                return;
            }

            // Validate time values
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

            // Save settings
            _settingsService.SetCalendarUrl(url);
            _settingsService.SetSleepTime(TimeSpan.FromMinutes(sleepMinutes));
            _settingsService.SetPreSleepBuffer(TimeSpan.FromMinutes(preSleepMinutes));
            _settingsService.SetWashTime(TimeSpan.FromMinutes(washMinutes));
            _settingsService.SetBreakfastTime(TimeSpan.FromMinutes(breakfastMinutes));
            _settingsService.SetCommuteTime(TimeSpan.FromMinutes(commuteMinutes));
            _settingsService.SetStartWithWindows(StartWithWindowsCheckBox.IsChecked ?? false);

            var successTitle = TryFindResource("Common_Success") as string ?? "成功";
            var successBody = TryFindResource("Settings_SettingsSavedSuccess") as string ?? "設定已儲存，日曆數據已即時載入。";
            var successDialog = new WarningDialog(successTitle, successBody, icon: "✅");
            WarningDialog.ShowSingleton(successDialog);

            // Restart orchestrator immediately so first-run user sees data
            var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
            {
                App.ApplyOrchestratorSettings(orchestrator, _settingsService);
                await orchestrator.RestartAsync(url);
            }

            Close();
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsWindow: 儲存設定失敗", ex);
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var bodyFmt = TryFindResource("Settings_SaveFailed") as string ?? "儲存設定失敗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
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
