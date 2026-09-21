using System.Windows;
using System.Windows.Controls;
using Terminus.Core.Services;

namespace Terminus.UI.Windows;

public partial class DelayHistoryWindow : Window
{
    public DelayHistoryWindow()
    {
        InitializeComponent();
        LoadHistory();
    }

    private void LoadHistory()
    {
        try
        {
            var cycleStateService = App.Services.GetService(typeof(ICycleStateService)) as ICycleStateService;
            var state = cycleStateService?.ReadState();

            if (state == null)
            {
                StatusText.Text = TryFindResource("History_Empty") as string ?? "無延遲記錄";
                var noDataBody = TryFindResource("History_NoDataBody") as string ?? "尚無延遲歷史記錄。\n記錄會在每次延遲操作時自動加密存檔。";
                var noData = new TextBlock
                {
                    Text = noDataBody,
                    FontSize = 13,
                    Margin = new Thickness(16, 16, 16, 16),
                    TextWrapping = TextWrapping.Wrap
                };
                noData.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                HistoryPanel.Children.Add(noData);
                return;
            }

            var cycleFmt = TryFindResource("History_CycleIdFormat") as string ?? "週期：{0}";
            CycleIdText.Text = string.Format(cycleFmt, state.CycleId);
            var statusFmt = TryFindResource("History_StatusFormat") as string ?? "更新時間：{0} · 手動 {1} 次 · 自動 {2} 次";
            StatusText.Text = string.Format(statusFmt, state.LastUpdated, state.DelayCount, state.AutoDelayCount);

            if (state.History.Count == 0)
            {
                var emptyBody = TryFindResource("History_EmptyCycleBody") as string ?? "本週期尚無延遲記錄。";
                var noHistory = new TextBlock
                {
                    Text = emptyBody,
                    FontSize = 13,
                    Margin = new Thickness(16, 16, 16, 16)
                };
                noHistory.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                HistoryPanel.Children.Add(noHistory);
                return;
            }

            var manualLabel = TryFindResource("History_Manual") as string ?? "手動";
            var autoLabel = TryFindResource("History_Auto") as string ?? "自動";
            var arrowFmt = TryFindResource("History_DelayedToFormat") as string ?? "→ {0}";
            var minutesFmt = TryFindResource("Format_QuotaRemaining") as string ?? "{0:F0} 分鐘";

            // 顯示每一筆延遲記錄（從新到舊）
            foreach (var record in state.History.AsEnumerable().Reverse())
            {
                var item = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(4, 4, 4, 4)
                };
                item.SetResourceReference(Border.BackgroundProperty, "BgTertiary");

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 時間戳
                var timeText = new TextBlock
                {
                    Text = record.Timestamp,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                Grid.SetColumn(timeText, 0);

                // 類型標籤
                var typeText = new TextBlock
                {
                    Text = record.Type == "manual" ? manualLabel : autoLabel,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(6, 2, 6, 2)
                };
                typeText.SetResourceReference(TextBlock.ForegroundProperty,
                    record.Type == "manual" ? "StatusInfo" : "StatusWarning");
                Grid.SetColumn(typeText, 1);

                // 延遲分鐘數
                var durText = new TextBlock
                {
                    Text = string.Format(minutesFmt, record.DurationMinutes),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                durText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                Grid.SetColumn(durText, 2);

                // 延後到
                var toText = new TextBlock
                {
                    Text = string.Format(arrowFmt, record.DelayedTo),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                toText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                Grid.SetColumn(toText, 3);

                grid.Children.Add(timeText);
                grid.Children.Add(typeText);
                grid.Children.Add(durText);
                grid.Children.Add(toText);
                item.Child = grid;
                HistoryPanel.Children.Add(item);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("DelayHistoryWindow: 載入歷史失敗", ex);
            var fmt = TryFindResource("History_LoadFailedFormat") as string ?? "載入失敗：{0}";
            StatusText.Text = string.Format(fmt, ex.Message);
        }
    }
}
