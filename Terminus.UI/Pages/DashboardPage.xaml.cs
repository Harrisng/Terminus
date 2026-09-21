using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Terminus.Core.Services;
using Terminus.Core.Logic;
using Terminus.Core.Models;
using Terminus.UI.Windows;
using NodaTime;

namespace Terminus.UI.Pages;

public partial class DashboardPage : Page
{
    private readonly BehaviorOrchestrator _orchestrator = null!;
    private readonly DispatcherTimer _clockTimer = null!;  // 每秒更新時鐘文字
    private readonly DispatcherTimer _stateTimer = null!; // 每 10 秒重算狀態/進度

    // ── Brush 快取（避免每秒 FindResource） ──
    private Brush? _brushStatusWarning;
    private Brush? _brushStatusIdle;
    private Brush? _brushStatusInfo;
    private Brush? _brushStatusError;
    private Brush? _brushStatusSuccess;
    private Brush? _brushTextSecondary;
    private Brush? _brushTextTertiary;
    private bool _brushesCached = false;

    private bool _lastHasData = false;

    public DashboardPage(BehaviorOrchestrator orchestrator)
    {
        try
        {
            InitializeComponent();
            _orchestrator = orchestrator;

            _orchestrator.StateChanged += OnStateChanged;

            // 時鐘 Timer：每秒只更新日期時間文字，工作量極小
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += UpdateClock;
            _clockTimer.Start();

            // 狀態 Timer：每 10 秒重新整理狀態顯示與預覽
            _stateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _stateTimer.Tick += UpdateDisplay;
            _stateTimer.Start();

            UpdateDisplay(null, EventArgs.Empty);
            LoadSchedulePreview();
        }
        catch (Exception ex)
        {
            LoggerService.Error("DashboardPage: 初始化失敗", ex);
            var dialog = new WarningDialog("初始化錯誤",
                $"儀表板頁面初始化失敗：{ex.Message}", icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    /// <summary>每秒只更新日期時間文字，不重新計算狀態/Brush。</summary>
    private void UpdateClock(object? sender, EventArgs e)
    {
        CurrentDateText.Text = DateTime.Now.ToString("yyyy年MM月dd日 dddd HH:mm");
    }

    /// <summary>快取常用 Brush 物件，避免每秒呼叫 FindResource。</summary>
    private void EnsureBrushesCached()
    {
        if (_brushesCached) return;
        _brushStatusWarning = (Brush)FindResource("StatusWarning");
        _brushStatusIdle = (Brush)FindResource("StatusIdle");
        _brushStatusInfo = (Brush)FindResource("StatusInfo");
        _brushStatusError = (Brush)FindResource("StatusError");
        _brushStatusSuccess = (Brush)FindResource("StatusSuccess");
        _brushTextSecondary = (Brush)FindResource("TextSecondary");
        _brushTextTertiary = (Brush)FindResource("TextTertiary");
        _brushesCached = true;
    }

    private void OnStateChanged(object? sender, BehaviorStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateDisplay(null, EventArgs.Empty);
            LoadSchedulePreview();
        });
    }

    public void UpdateDisplaySafe()
    {
        try
        {
            UpdateDisplay(null, EventArgs.Empty);
            LoadSchedulePreview();
        }
        catch (Exception ex)
        {
            LoggerService.Error("DashboardPage: UpdateDisplaySafe 失敗", ex);
        }
    }

    private void UpdateDisplay(object? sender, EventArgs e)
    {
        EnsureBrushesCached();

        var now = DateTime.Now;

        var context = _orchestrator.GetCurrentContext();

        if (context == null)
        {
            StateText.Text = "未啟動";
            StateText.Foreground = _brushStatusWarning!;
            StateIndicator.Fill = _brushStatusWarning!;
            NextActionText.Text = "請先設定";
            QuotaText.Text = "--";
            CycleIdText.Text = "--";
            DelayCountText.Text = "手動 0 次 · 自動 0 次";
            WarningTimeText.Text = "--";
            TomorrowClassTimeText.Text = "--";
            ClassificationText.Text = "請配置日曆";
            ClassificationText.Foreground = _brushStatusWarning!;
            ClassificationIndicator.Fill = _brushStatusWarning!;
            return;
        }

        var stateInfo = GetStateDisplayInfo(context.State);
        StateText.Text = stateInfo.Text;
        StateText.Foreground = stateInfo.Brush;
        StateIndicator.Fill = stateInfo.Brush;
        StateIndicator.Effect = new DropShadowEffect
        {
            Color = stateInfo.Color,
            Opacity = 0.6,
            BlurRadius = 10,
            ShadowDepth = 0
        };

        if (context.NextActionTime.HasValue)
        {
            var nextAction = context.NextActionTime.Value.ToDateTimeUnspecified();
            NextActionText.Text = nextAction.ToString("HH:mm");
        }
        else
        {
            NextActionText.Text = "--";
        }

        var classInfo = GetClassificationDisplayInfo(context.Timing.Classification);
        ClassificationText.Text = classInfo.Text;
        ClassificationText.Foreground = classInfo.Brush;
        ClassificationIndicator.Fill = classInfo.Brush;

        // 下次預警時間：延遲中顯示延遲到期時間（警告重現時間），否則顯示排程預警時間（已過則 --）
        if (context.State == BehaviorState.Delayed || context.State == BehaviorState.AutoDelaying)
        {
            if (context.NextActionTime.HasValue)
                WarningTimeText.Text = context.NextActionTime.Value.ToDateTimeUnspecified().ToString("HH:mm");
            else
                WarningTimeText.Text = "--";
        }
        else if (context.Timing.WarningTime != LocalTime.Midnight)
        {
            var nowTime = new LocalTime(now.Hour, now.Minute);
            if (nowTime < context.Timing.WarningTime)
                WarningTimeText.Text = context.Timing.WarningTime.ToString("HH:mm", null);
            else
                WarningTimeText.Text = "--";
        }
        else
        {
            WarningTimeText.Text = "--";
        }

        if (context.Timing.IsUnlimitedManualDelay)
        {
            QuotaText.Text = "無限制";
        }
        else
        {
            QuotaText.Text = $"{context.CurrentCycle.QuotaRemaining.TotalMinutes:F0} 分鐘";
        }

        DelayCountText.Text = $"手動 {context.CurrentCycle.DelayCount} 次 · 自動 {context.CurrentCycle.AutoDelayCount} 次";

        // Format CycleId properly (remove the "T" separator)
        var cycleId = context.CurrentCycle.CycleId;
        if (DateTime.TryParse(cycleId, out var cycleDate))
            CycleIdText.Text = cycleDate.ToString("yyyy/MM/dd HH:mm");
        else
            CycleIdText.Text = cycleId;

        if (context.Timing.FirstClassTime.HasValue)
        {
            TomorrowClassTimeText.Text = context.Timing.FirstClassTime.Value.ToString("HH:mm", null);
        }
        else
        {
            TomorrowClassTimeText.Text = "--";
        }

        // Refresh 7-day preview when data availability changes
        var hasData = context.Timing.Classification != DayClassification.NoData;
        if (hasData != _lastHasData)
        {
            _lastHasData = hasData;
            LoadSchedulePreview();
        }
    }

    private (string Text, Brush Brush, Color Color) GetStateDisplayInfo(BehaviorState state)
    {
        return state switch
        {
            BehaviorState.Idle => MakeStateInfo("閒置中", _brushStatusIdle),
            BehaviorState.PreWarning => MakeStateInfo("預警階段", _brushStatusWarning),
            BehaviorState.Warning => MakeStateInfo("警告中", _brushStatusWarning),
            BehaviorState.Delayed => MakeStateInfo("已延後", _brushStatusInfo),
            BehaviorState.AutoDelaying => MakeStateInfo("自動延後中", _brushStatusInfo),
            BehaviorState.ForceShutdown => MakeStateInfo("強制關機倒數", _brushStatusError),
            BehaviorState.ShuttingDown => MakeStateInfo("關機中", _brushStatusError),
            BehaviorState.AIMode => MakeStateInfo("AI 模式運行中", _brushStatusIdle),
            BehaviorState.Disabled => MakeStateInfo("已停用", _brushTextTertiary),
            _ => MakeStateInfo(state.ToString(), _brushTextSecondary)
        };
    }

    private (string Text, Brush Brush, Color Color) MakeStateInfo(string text, Brush? brush)
    {
        var b = brush ?? _brushTextSecondary!;
        var color = b is SolidColorBrush scb ? scb.Color : Colors.Transparent;
        return (text, b, color);
    }

    private (string Text, Brush Brush) GetClassificationDisplayInfo(DayClassification classification)
    {
        return classification switch
        {
            DayClassification.EarlyClass => ("早課日", _brushStatusIdle!),
            DayClassification.NonEarlyClass => ("非早課日", _brushStatusSuccess!),
            DayClassification.NoData => ("無日曆數據", _brushStatusWarning!),
            _ => (classification.ToString(), _brushTextTertiary!)
        };
    }

    private void LoadSchedulePreview()
    {
        SchedulePanel.Children.Clear();

        var context = _orchestrator.GetCurrentContext();
        var hasData = context != null && context.Timing.Classification != DayClassification.NoData;
        var events = context?.Events ?? new List<CalendarEvent>();

        var daysOfWeek = new[] { "週日", "週一", "週二", "週三", "週四", "週五", "週六" };
        var today = DateTime.Today;

        for (int i = 0; i < 7; i++)
        {
            var date = today.AddDays(i);
            var dayName = i == 0 ? "今天" : daysOfWeek[(int)date.DayOfWeek];
            var normalBgKey = i == 0 ? "BgTertiary" : "BgSecondary";

            var item = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            item.SetResourceReference(Border.BackgroundProperty, normalBgKey);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dateText = new TextBlock
            {
                Text = $"{date:MM/dd} {dayName}",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            dateText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(dateText, 0);
            grid.Children.Add(dateText);

            var typeText = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var timeText = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (hasData)
            {
                // Classify each day individually using the events
                var hkDate = LocalDate.FromDateTime(date);
                var classification = ScheduleClassifier.ClassifyDay(hkDate, events, true);
                var firstClass = ScheduleClassifier.GetFirstClassTime(hkDate, events);

                typeText.Text = classification == DayClassification.EarlyClass ? "早課日" : "非早課日";
                typeText.SetResourceReference(TextBlock.ForegroundProperty,
                    classification == DayClassification.EarlyClass ? "StatusIdle" : "TextSecondary");

                if (classification == DayClassification.EarlyClass && firstClass.HasValue)
                    timeText.Text = $"首節 {firstClass.Value.ToString("HH:mm", null)}";
                else
                    timeText.Text = context!.Timing.WarningTime.ToString("HH:mm", null);
            }
            else
            {
                typeText.Text = "待配置";
                typeText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                timeText.Text = "--";
            }
            timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");

            Grid.SetColumn(typeText, 1);
            grid.Children.Add(typeText);
            Grid.SetColumn(timeText, 2);
            grid.Children.Add(timeText);

            item.Child = grid;
            SchedulePanel.Children.Add(item);
        }
    }

    private async void DelayButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WarningDialog("確認延遲", "確定要延遲 30 分鐘嗎？", icon: "⏰");
        dialog.SetConfirmMode("確認延遲");
        dialog.ShowDialog();

        if (dialog.DialogResult != true)
            return;

        var success = await _orchestrator.OnDelayRequestedAsync();
        if (!success)
        {
            var feedback = new WarningDialog("無法延遲",
                "目前不在警告階段，無法手動延遲。\n請等待警告出現後再操作。", icon: "ℹ️");
            WarningDialog.ShowSingleton(feedback);
        }
    }

    private async void ShutdownNowButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WarningDialog("確認關機", "確定要立即關機嗎？\n此操作無法復原。", icon: "⚡");
        dialog.SetConfirmMode("立即關機", isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            await _orchestrator.OnShutdownNowRequestedAsync();
        }
    }

    private void AIModeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WarningDialog("AI 通宵模式", "此功能開發中，敬請期待。", icon: "🤖");
        WarningDialog.ShowSingleton(dialog);
    }
}
