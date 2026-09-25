using System.Windows;
using Terminus.UI.Windows;

namespace Terminus.UI;

public partial class AIModeDialog : Window
{
    public string CommandLine { get; private set; } = string.Empty;
    public string WorkingDirectory { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public AIModeDialog()
    {
        InitializeComponent();
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
