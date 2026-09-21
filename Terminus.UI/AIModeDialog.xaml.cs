using System.Windows;

namespace Terminus.UI;

public partial class AIModeDialog : Window
{
    public string TaskDescription { get; private set; } = string.Empty;

    public AIModeDialog()
    {
        InitializeComponent();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var description = TaskDescriptionTextBox.Text.Trim();

        if (string.IsNullOrEmpty(description))
        {
            var dialog = new Windows.WarningDialog("輸入不完整", "請輸入任務描述。", icon: "⚠️");
            Windows.WarningDialog.ShowSingleton(dialog);
            return;
        }

        TaskDescription = description;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
