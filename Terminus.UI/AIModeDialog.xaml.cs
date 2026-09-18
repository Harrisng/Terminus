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
            MessageBox.Show("請輸入任務描述。", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
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
