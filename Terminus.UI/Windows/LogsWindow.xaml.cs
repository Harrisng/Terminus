using System.Windows;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class LogsWindow : Window
{
    public LogsWindow()
    {
        InitializeComponent();
        LogPathText.Text = LoggerService.GetLogFilePath();
        LoadLogs();
    }

    private void LoadLogs()
    {
        var logs = LoggerService.ReadRecentLogs(1000);
        LogTextBox.Text = logs;

        if (!string.IsNullOrEmpty(logs) && logs != "（無日誌記錄）" && logs != "（日誌為空）")
        {
            var lineCount = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            StatusText.Text = $"已載入 {lineCount} 行日誌";
            LogTextBox.ScrollToEnd();
        }
        else
        {
            StatusText.Text = logs;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WarningDialog("確認清空", "確定要清空所有日誌嗎？\n此操作無法復原。", icon: "🗑️");
        dialog.SetConfirmMode("清空日誌", isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            LoggerService.Clear();
            LoadLogs();
        }
    }
}
