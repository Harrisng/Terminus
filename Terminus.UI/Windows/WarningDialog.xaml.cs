using System.Windows;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class WarningDialog : Window
{
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
