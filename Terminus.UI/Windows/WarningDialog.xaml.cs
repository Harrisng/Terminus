using System.Windows;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class WarningDialog : Window
{
    private readonly WarningNotificationArgs _args;

    public WarningDialog(WarningNotificationArgs args)
    {
        InitializeComponent();
        _args = args;

        TitleText.Text = args.Title;
        MessageText.Text = args.Message;

        var quotaText = args.IsUnlimitedDelay
            ? "延後次數：無限制"
            : $"剩餘配額：{args.QuotaRemaining.TotalMinutes:F0} 分鐘";
        QuotaText.Text = quotaText;

        // Hide buttons based on args
        if (!args.ShowDelayButton)
            DelayButton.Visibility = Visibility.Collapsed;
        if (!args.ShowShutdownButton)
            ShutdownButton.Visibility = Visibility.Collapsed;
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
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
