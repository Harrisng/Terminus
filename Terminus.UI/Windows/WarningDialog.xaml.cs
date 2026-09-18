using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class WarningDialog : Window
{
    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x0;
    private const uint MB_ICONEXCLAMATION = 0x30;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern bool RemoveMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    /// <summary>
    /// 警告模式（有延後/關機按鈕）下為 false，用戶必須選一個操作才能關閉。
    /// </summary>
    private bool _allowClose = true;

    public WarningDialog(string title, string message, string? quotaText = null,
        bool showDelayButton = false, bool showShutdownButton = false, string? icon = null,
        TimeSpan? quotaRemaining = null, bool isUnlimitedDelay = false)
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

        // 設定延遲按鈕可見性（依配額決定顯示哪些選項）
        if (showDelayButton)
        {
            SetupDelayButtons(quotaRemaining, isUnlimitedDelay);
        }
        else
        {
            Delay30Button.Visibility = Visibility.Collapsed;
            Delay60Button.Visibility = Visibility.Collapsed;
            Delay90Button.Visibility = Visibility.Collapsed;
        }

        if (!showShutdownButton)
            ShutdownButton.Visibility = Visibility.Collapsed;

        // 警告模式：必須選操作才能關閉，隱藏「關閉」按鈕
        if (showDelayButton || showShutdownButton)
        {
            _allowClose = false;
            CloseButton.Visibility = Visibility.Collapsed;
            // 播放提示音
            MessageBeep(MB_ICONEXCLAMATION);
        }
    }

    /// <summary>
    /// 依配額設定延遲按鈕可見性。無限制時全部顯示。
    /// </summary>
    private void SetupDelayButtons(TimeSpan? quotaRemaining, bool isUnlimited)
    {
        var q = isUnlimited ? TimeSpan.MaxValue : (quotaRemaining ?? TimeSpan.Zero);

        Delay30Button.Visibility = q >= TimeSpan.FromMinutes(30) ? Visibility.Visible : Visibility.Collapsed;
        Delay60Button.Visibility = q >= TimeSpan.FromMinutes(60) ? Visibility.Visible : Visibility.Collapsed;
        Delay90Button.Visibility = q >= TimeSpan.FromMinutes(90) ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 警告模式下移除標題列 X 按鈕（灰色不可點）
        if (!_allowClose)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var menu = GetSystemMenu(handle, false);
            if (menu != IntPtr.Zero)
                RemoveMenu(menu, SC_CLOSE, MF_BYCOMMAND);
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
        Delay30Button.Visibility = Visibility.Collapsed;
        Delay60Button.Visibility = Visibility.Collapsed;
        Delay90Button.Visibility = Visibility.Collapsed;
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
            {
                var duration = sender == Delay30Button ? TimeSpan.FromMinutes(30)
                    : sender == Delay60Button ? TimeSpan.FromMinutes(60)
                    : sender == Delay90Button ? TimeSpan.FromMinutes(90)
                    : TimeSpan.FromMinutes(30);
                await orchestrator.OnDelayRequestedAsync(duration);
            }
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
