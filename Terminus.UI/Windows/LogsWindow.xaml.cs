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

        var emptyText = TryFindResource("Logs_Empty") as string ?? "（無日誌記錄）";
        if (!string.IsNullOrEmpty(logs) && logs != emptyText && logs != "（日誌為空）")
        {
            var lineCount = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            var fmt = TryFindResource("Logs_LineCountFormat") as string ?? "已載入 {0} 行日誌";
            StatusText.Text = string.Format(fmt, lineCount);
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
        var title = TryFindResource("Logs_ClearConfirmTitle") as string ?? "確認清空";
        var body = TryFindResource("Logs_ClearConfirmBody") as string ?? "確定要清空所有日誌嗎？\n此操作無法復原。";
        var dialog = new WarningDialog(title, body, icon: "🗑️");
        var btnText = TryFindResource("Logs_Clear") as string ?? "清空日誌";
        dialog.SetConfirmMode(btnText, isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            LoggerService.Clear();
            LoadLogs();
        }
    }
}
