using System.ComponentModel;
using System.Windows;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class WarningDialog : Window
{
    /// <summary>
    /// 警告模式（有延後/關機按鈕）下為 false，用戶必須選一個操作才能關閉。
    /// </summary>
    private bool _allowClose = true;

    public WarningDialog(string title, string message, string? quotaText = null,
        bool showDelayButton = false, bool showShutdownButton = false, string? icon = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = icon ?? "⚠️";

        if (!string.IsNullOrEmpty(quotaText))
        {
            QuotaText.Text = quotaText;
        }
        else
        {
            QuotaBorder.Visibility = Visibility.Collapsed;
        }

        if (!showDelayButton)
            DelayButton.Visibility = Visibility.Collapsed;
        if (!showShutdownButton)
            ShutdownButton.Visibility = Visibility.Collapsed;

        // 警告模式：必須選操作才能關閉，隱藏「關閉」按鈕
        if (showDelayButton || showShutdownButton)
        {
            _allowClose = false;
            CloseButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 設定確認模式：顯示「確認」與「取消」按鈕，隱藏其他操作按鈕。
    /// isDanger=true 時確認按鈕改為紅色（用於關機等危險操作）。
    /// </summary>
    public void SetConfirmMode(string confirmText, bool isDanger = false)
    {
        ConfirmButton.Content = confirmText;
        ConfirmButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        DelayButton.Visibility = Visibility.Collapsed;
        ShutdownButton.Visibility = Visibility.Collapsed;
        _allowClose = true;

        if (isDanger)
        {
            ConfirmButton.ApplyTemplate();
            var bd = ConfirmButton.Template.FindName("bd", ConfirmButton) as System.Windows.Controls.Border;
            bd?.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "StatusError");
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        DialogResult = false;
        Close();
    }

    private async void DelayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
                await orchestrator.OnDelayRequestedAsync();
        }
        catch { }
        _allowClose = true;
        Close();
    }

    private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
                await orchestrator.OnShutdownNowRequestedAsync();
        }
        catch { }
        _allowClose = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close();
    }
}
