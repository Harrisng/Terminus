using System.Windows;
using System.Windows.Input;
using Terminus.Core.Services;
using Terminus.UI.Services;

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
                MessageBox.Show("請輸入日曆 URL", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate time values
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

            // Save settings
            _settingsService.SetCalendarUrl(url);
            _settingsService.SetSleepTime(TimeSpan.FromMinutes(sleepMinutes));
            _settingsService.SetPreSleepBuffer(TimeSpan.FromMinutes(preSleepMinutes));
            _settingsService.SetWashTime(TimeSpan.FromMinutes(washMinutes));
            _settingsService.SetBreakfastTime(TimeSpan.FromMinutes(breakfastMinutes));
            _settingsService.SetCommuteTime(TimeSpan.FromMinutes(commuteMinutes));
            _settingsService.SetStartWithWindows(StartWithWindowsCheckBox.IsChecked ?? false);

            MessageBox.Show("設置保存成功。請重啟 Terminus 以使更改生效。",
                "成功", MessageBoxButton.OK, MessageBoxImage.Information);

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存設置失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("確定要清除所有緩存的日曆數據嗎？",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _cacheService.ClearAll();
            MessageBox.Show("緩存已清除成功。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
