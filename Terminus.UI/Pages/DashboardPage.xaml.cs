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
            var title = TryFindResource("Window_InitError") as string ?? "初始化錯誤";
            var bodyFmt = TryFindResource("Dashboard_InitError") as string ?? "儀表板頁面初始化失敗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    /// <summary>每秒只更新日期時間文字，不重新計算狀態/Brush。</summary>
    private void UpdateClock(object? sender, EventArgs e)
    {
        var fmt = TryFindResource("Dashboard_DateFormat") as string ?? "yyyy年MM月dd日 dddd HH:mm";
        CurrentDateText.Text = DateTime.Now.ToString(fmt);
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
            StateText.SetResourceReference(TextBlock.TextProperty, "Dashboard_NotStarted");
            StateText.Foreground = _brushStatusWarning!;
            StateIndicator.Fill = _brushStatusWarning!;
            NextActionText.SetResourceReference(TextBlock.TextProperty, "Dashboard_PleaseSetup");
            QuotaText.Text = "--";
            CycleIdText.Text = "--";
            var delayFmt0 = TryFindResource("Dashboard_DelayCountFormat") as string ?? "手動 {0} 次 · 自動 {1} 次";
            DelayCountText.Text = string.Format(delayFmt0, 0, 0);
            WarningTimeText.Text = "--";
            TomorrowClassTimeText.Text = "--";
            ClassificationText.SetResourceReference(TextBlock.TextProperty, "Dashboard_PleaseConfigCalendar");
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
            QuotaText.SetResourceReference(TextBlock.TextProperty, "Dashboard_Unlimited");
        }
        else
        {
            var quotaFmt = TryFindResource("Format_QuotaRemaining") as string ?? "{0:F0} 分鐘";
            QuotaText.Text = string.Format(quotaFmt, context.CurrentCycle.QuotaRemaining.TotalMinutes);
        }

        var delayFmt = TryFindResource("Dashboard_DelayCountFormat") as string ?? "手動 {0} 次 · 自動 {1} 次";
        DelayCountText.Text = string.Format(delayFmt,
            context.CurrentCycle.DelayCount, context.CurrentCycle.AutoDelayCount);

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
            BehaviorState.Idle => MakeStateInfo(TryFindResource("State_Idle") as string ?? "閒置中", _brushStatusIdle),
            BehaviorState.PreWarning => MakeStateInfo(TryFindResource("State_PreWarning") as string ?? "預警階段", _brushStatusWarning),
            BehaviorState.Warning => MakeStateInfo(TryFindResource("State_Warning") as string ?? "警告中", _brushStatusWarning),
            BehaviorState.Delayed => MakeStateInfo(TryFindResource("State_Delayed") as string ?? "已延後", _brushStatusInfo),
            BehaviorState.AutoDelaying => MakeStateInfo(TryFindResource("State_AutoDelaying") as string ?? "自動延後中", _brushStatusInfo),
            BehaviorState.ForceShutdown => MakeStateInfo(TryFindResource("State_ForceShutdown") as string ?? "強制關機倒數", _brushStatusError),
            BehaviorState.ShuttingDown => MakeStateInfo(TryFindResource("State_ShuttingDown") as string ?? "關機中", _brushStatusError),
            BehaviorState.AIMode => MakeStateInfo(TryFindResource("State_AIMode") as string ?? "AI 模式運行中", _brushStatusIdle),
            BehaviorState.Disabled => MakeStateInfo(TryFindResource("State_Disabled") as string ?? "已停用", _brushTextTertiary),
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
            DayClassification.EarlyClass => (TryFindResource("Classification_EarlyClass") as string ?? "早課日", _brushStatusIdle!),
            DayClassification.NonEarlyClass => (TryFindResource("Classification_NonEarlyClass") as string ?? "非早課日", _brushStatusSuccess!),
            DayClassification.NoData => (TryFindResource("Classification_NoData") as string ?? "無日曆數據", _brushStatusWarning!),
            _ => (classification.ToString(), _brushTextTertiary!)
        };
    }

    private void LoadSchedulePreview()
    {
        SchedulePanel.Children.Clear();

        var context = _orchestrator.GetCurrentContext();
        var hasData = context != null && context.Timing.Classification != DayClassification.NoData;
        var events = context?.Events ?? new List<CalendarEvent>();

        var dayKeys = new[]
        {
            "Day_Sunday", "Day_Monday", "Day_Tuesday", "Day_Wednesday",
            "Day_Thursday", "Day_Friday", "Day_Saturday"
        };
        var today = DateTime.Today;
        var todayLabel = TryFindResource("Day_Today") as string ?? "今天";

        for (int i = 0; i < 7; i++)
        {
            var date = today.AddDays(i);
            var dayName = i == 0 ? todayLabel : (TryFindResource(dayKeys[(int)date.DayOfWeek]) as string ?? date.DayOfWeek.ToString());
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

                typeText.Text = classification == DayClassification.EarlyClass
                    ? (TryFindResource("Classification_EarlyClass") as string ?? "早課日")
                    : (TryFindResource("Classification_NonEarlyClass") as string ?? "非早課日");
                typeText.SetResourceReference(TextBlock.ForegroundProperty,
                    classification == DayClassification.EarlyClass ? "StatusIdle" : "TextSecondary");

                if (classification == DayClassification.EarlyClass && firstClass.HasValue)
                {
                    var firstFmt = TryFindResource("Dashboard_FirstClassPrefix") as string ?? "首節 {0}";
                    timeText.Text = string.Format(firstFmt, firstClass.Value.ToString("HH:mm", null));
                }
                else
                    timeText.Text = context!.Timing.WarningTime.ToString("HH:mm", null);
            }
            else
            {
                typeText.Text = TryFindResource("Dashboard_PendingConfig") as string ?? "待配置";
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
        var title = TryFindResource("Warning_ConfirmDelay") as string ?? "確認延遲";
        var body = TryFindResource("Warning_ConfirmDelayMsg") as string ?? "確定要延遲 30 分鐘嗎？";
        var dialog = new WarningDialog(title, body, icon: "⏰");
        dialog.SetConfirmMode(title);
        dialog.ShowDialog();

        if (dialog.DialogResult != true)
            return;

        var success = await _orchestrator.OnDelayRequestedAsync();
        if (!success)
        {
            var failTitle = TryFindResource("Warning_UnableToDelay") as string ?? "無法延遲";
            var failBody = TryFindResource("Warning_NotInWarningState") as string ?? "目前不在警告階段，無法手動延遲。\n請等待警告出現後再操作。";
            var feedback = new WarningDialog(failTitle, failBody, icon: "ℹ️");
            WarningDialog.ShowSingleton(feedback);
        }
    }

    private async void ShutdownNowButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TryFindResource("Warning_ConfirmShutdown") as string ?? "確認關機";
        var body = TryFindResource("Warning_ConfirmShutdownMsg") as string ?? "確定要立即關機嗎？\n此操作無法復原。";
        var dialog = new WarningDialog(title, body, icon: "⚡");
        dialog.SetConfirmMode(title, isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            await _orchestrator.OnShutdownNowRequestedAsync();
        }
    }

    private async void AIModeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AIModeDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();

        if (dialog.DialogResult != true)
            return;

        var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
        if (orchestrator == null)
        {
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var body = TryFindResource("AIMode_NotAvailable") as string ?? "AI 模式目前無法啟動。";
            var err = new WarningDialog(title, body, icon: "❌");
            WarningDialog.ShowSingleton(err);
            return;
        }

        try
        {
            await orchestrator.OnAIModeRequestedAsync(
                dialog.CommandLine,
                dialog.WorkingDirectory,
                dialog.Description);
        }
        catch (Exception ex)
        {
            LoggerService.Error("DashboardPage: 啟動 AI 模式失敗", ex);
            var title = TryFindResource("AIMode_StartFailed") as string ?? "AI 模式啟動失敗";
            var fmt = TryFindResource("AIMode_StartFailedBody") as string ?? "無法啟動 AI 模式：{0}";
            var body = string.Format(fmt, ex.Message);
            var err = new WarningDialog(title, body, icon: "❌");
            WarningDialog.ShowSingleton(err);
        }
    }
}
