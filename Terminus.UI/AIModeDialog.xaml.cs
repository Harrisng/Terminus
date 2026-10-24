using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Terminus.UI.Windows;

namespace Terminus.UI;

public partial class AIModeDialog : Window
{
    /// <summary>使用者選擇「監控正在執行的 AI 進程」模式。</summary>
    public bool UseProcessDetection { get; private set; }

    public string CommandLine { get; private set; } = string.Empty;
    public string WorkingDirectory { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    private readonly IReadOnlyList<string> _runningProcesses;

    /// <summary>拖動視窗：僅在非互動控件（背景、文字、面板）上觸發。</summary>
    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not (TextBlock or Border or Grid or StackPanel)) return;
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* 拖動中重入時忽略 */ }
        }
    }

    /// <summary>WPF Border 不裁剪圓角，手動給內容 Grid 加圓角 Clip，避免環境光溢出角落。</summary>
    private void RootClipHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootClipHost.Clip = new RectangleGeometry(
            new Rect(0, 0, RootClipHost.ActualWidth, RootClipHost.ActualHeight),
            21, 21);  // 外圓角 22 − 1px 描邊
    }

    public AIModeDialog(IReadOnlyList<string>? runningProcesses = null)
    {
        InitializeComponent();
        _runningProcesses = runningProcesses ?? new List<string>();

        if (_runningProcesses.Count > 0)
        {
            RunningProcessesText.Text = string.Join("、", _runningProcesses);
            MonitorButton.Visibility = Visibility.Visible;
        }
        else
        {
            RunningProcessesText.Text = TryFindResource("AIMode_NoRunningProcess") as string ?? "目前沒有偵測到正在執行的 AI 進程";
            MonitorButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>監控正在執行的 AI 進程，等它們結束後再關機。</summary>
    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        UseProcessDetection = true;
        Description = $"AI 進程執行中：{string.Join(", ", _runningProcesses)}";
        DialogResult = true;
        Close();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var cmd = CommandLineTextBox.Text.Trim();
        if (string.IsNullOrEmpty(cmd))
        {
            var title = TryFindResource("AIMode_InputIncomplete") as string ?? "輸入不完整";
            var body = TryFindResource("AIMode_CommandRequired") as string ?? "請輸入要執行的命令行。";
            var dialog = new WarningDialog(title, body, icon: "⚠️");
            WarningDialog.ShowSingleton(dialog);
            return;
        }

        UseProcessDetection = false;
        CommandLine = cmd;
        WorkingDirectory = WorkDirTextBox.Text.Trim();
        Description = string.IsNullOrEmpty(DescriptionTextBox.Text.Trim())
            ? cmd
            : DescriptionTextBox.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
