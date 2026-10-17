using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Terminus.Core.Services;
using Terminus.UI.Services;
using Terminus.UI.Themes;
using Terminus.UI.Windows;

namespace Terminus.UI.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService = null!;
    private readonly CacheService _cacheService = null!;
    private readonly ThemeService _themeService = null!;
    private readonly LanguageService? _languageService;
    private bool _isInitialized = false;

    // ── 時間軸預覽狀態 ──
    /// <summary>目前預覽的就寢時間（拉動 bar 會改變這個值）。</summary>
    private DateTime _previewBedtime;
    /// <summary>目前預覽的抵達時間（= bedtime + 5 個時長總和）。</summary>
    private DateTime _previewArrival;
    /// <summary>時間軸可見範圍起點（就寢前 3 小時）。</summary>
    private DateTime _rangeStart;
    /// <summary>時間軸可見範圍終點（抵達後 3 小時）。</summary>
    private DateTime _rangeEnd;

    private enum DragMode { None, Left, Right }  // Left=改就寢(抵達固定), Right=改抵達(就寢固定)
    private DragMode _dragMode = DragMode.None;
    private double _dragStartX;
    private DateTime _dragStartBedtime;
    private DateTime _dragStartArrival;
    /// <summary>拖動中游標 X 座標（用於畫指示線）。NaN 表示未拖動。</summary>
    private double _cursorX = double.NaN;
    /// <summary>拖動中是否正在更新 TextBox（避免 TextChanged 遞迴）。</summary>
    private bool _isUpdatingFromDrag = false;
    /// <summary>最小睡眠分鐘數（防止拖過頭）。</summary>
    private const int MinSleepMinutes = 30;

    public SettingsPage(SettingsService settingsService, CacheService cacheService, ThemeService themeService, LanguageService? languageService = null)
    {
        try
        {
            _settingsService = settingsService;
            _cacheService = cacheService;
            _themeService = themeService;
            _languageService = languageService;

            InitializeComponent();

            LoadSettings();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 初始化失敗", ex);
            var title = TryFindResource("Window_InitError") as string ?? "初始化錯誤";
            var bodyFmt = TryFindResource("Settings_InitError") as string ?? "設定頁面初始化失敗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void LoadSettings()
    {
        CalendarUrlTextBox.Text = _settingsService.GetCalendarUrl();
        var sleepTotal = (int)_settingsService.GetSleepTime().TotalMinutes;
        SleepHoursTextBox.Text = (sleepTotal / 60).ToString();
        SleepMinutesTextBox.Text = (sleepTotal % 60).ToString();
        PreSleepTextBox.Text = _settingsService.GetPreSleepBuffer().TotalMinutes.ToString();
        WashTextBox.Text = _settingsService.GetWashTime().TotalMinutes.ToString();
        BreakfastTextBox.Text = _settingsService.GetBreakfastTime().TotalMinutes.ToString();
        CommuteTextBox.Text = _settingsService.GetCommuteTime().TotalMinutes.ToString();
        CutoffHourTextBox.Text = _settingsService.GetEarlyClassCutoffHour().ToString();
        DelayQuotaTextBox.Text = _settingsService.GetEarlyClassDelayQuotaMinutes().ToString();
        StartWithWindowsCheckBox.IsChecked = _settingsService.GetStartWithWindows();
        DarkModeCheckBox.IsChecked = _themeService.CurrentTheme == AppTheme.Dark;

        // Initialize language combo (suppress SelectionChanged during init)
        if (_languageService != null)
        {
            var current = _languageService.CurrentLanguage;
            ComboBoxItem? item = current switch
            {
                LanguageCodes.SimplifiedChinese => LangZhCNItem,
                LanguageCodes.English => LangEnUSItem,
                _ => LangZhTWItem
            };
            if (item != null)
                LanguageComboBox.SelectedItem = item;
        }

        // 初始化時間軸預覽
        UpdateSleepTimelinePreview();
    }

    /// <summary>輸入框文字變更時即時更新時間軸預覽。</summary>
    private void SleepInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (_isUpdatingFromDrag) return;  // 拖動中由程式更新，避免遞迴
        UpdateSleepTimelinePreview();
    }

    /// <summary>
    /// 根據 5 個分鐘輸入值更新睡眠時間軸預覽：
    /// - 摘要文字（就寢 → 起床 → 抵達）
    /// - 時間軸 Canvas 上的 30 分鐘刻度 + 5 區段 + 兩個 handle
    /// - 圖例的時長標籤
    /// </summary>
    private void UpdateSleepTimelinePreview()
    {
        try
        {
            // 防呆：XAML 還沒初始化完成時跳過
            if (SleepTimelineSummary == null || TimelineCanvas == null) return;

            // 解析睡眠時長（從「時」「分」兩個輸入框組合）
            int sleepH = int.TryParse(SleepHoursTextBox.Text, out var sh) && sh >= 0 ? sh : 0;
            int sleepM = int.TryParse(SleepMinutesTextBox.Text, out var smn) && smn >= 0 ? smn : 0;
            int sleepMin = sleepH * 60 + sleepM;
            if (sleepMin <= 0) sleepMin = 390;  // 防呆預設 6.5 小時
            int preMin = int.TryParse(PreSleepTextBox.Text, out var pm) && pm >= 0 ? pm : 15;
            int washMin = int.TryParse(WashTextBox.Text, out var wm) && wm > 0 ? wm : 30;
            int breakfastMin = int.TryParse(BreakfastTextBox.Text, out var bm) && bm > 0 ? bm : 45;
            int commuteMin = int.TryParse(CommuteTextBox.Text, out var cm) && cm > 0 ? cm : 105;

            int total = preMin + sleepMin + washMin + breakfastMin + commuteMin;
            if (total <= 0)
            {
                SleepTimelineSummary.Text = string.Empty;
                return;
            }

            // 若沒有預覽值（首次載入），根據首節課時間往回推算就寢
            if (_previewBedtime == default)
            {
                DateTime targetTime;
                var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
                var context = orchestrator?.GetCurrentContext();
                if (context?.Timing?.FirstClassTime != null)
                {
                    var fcTime = context.Timing.FirstClassTime.Value;
                    targetTime = DateTime.Today.Add(new TimeSpan(fcTime.Hour, fcTime.Minute, fcTime.Second));
                    if (targetTime < DateTime.Now) targetTime = targetTime.AddDays(1);
                }
                else
                {
                    targetTime = DateTime.Today.AddDays(1).AddHours(9);
                }
                _previewArrival = targetTime;
                _previewBedtime = targetTime.AddMinutes(-total);
            }
            else if (_isUpdatingFromDrag)
            {
                // 拖動中：bedtime/arrival 已由拖動邏輯設定，不重算
            }
            else
            {
                // 輸入值變動時，以現有 bedtime 為基準重算 arrival
                _previewArrival = _previewBedtime.AddMinutes(total);
            }

            // 自動範圍：就寢前 3 小時到抵達後 3 小時（拖動中不重算，維持穩定的 pixel-to-minute 映射）
            if (!_isUpdatingFromDrag)
            {
                _rangeStart = _previewBedtime.AddHours(-3);
                _rangeEnd = _previewArrival.AddHours(3);
            }

            // 更新摘要文字
            var totalHours = total / 60;
            var totalMins = total % 60;
            var totalStr = totalHours > 0
                ? $"{totalHours} 小時 {totalMins} 分鐘"
                : $"{totalMins} 分鐘";
            var summaryFmt = TryFindResource("SleepTimeline_SummaryFormat") as string
                ?? "預計 {0} 就寢 → {1} 起床 → {2} 抵達（總計 {3}）";
            SleepTimelineSummary.Text = string.Format(summaryFmt,
                _previewBedtime.ToString("HH:mm"),
                _previewArrival.AddMinutes(-(washMin + breakfastMin + commuteMin)).ToString("HH:mm"),
                _previewArrival.ToString("HH:mm"),
                totalStr);

            // 重畫 Canvas
            RedrawTimelineCanvas(sleepMin, preMin, washMin, breakfastMin, commuteMin);

            // 更新圖例時長標籤
            LegendBufferDuration.Text = FormatDuration(preMin);
            LegendSleepDuration.Text = FormatDuration(sleepMin);
            LegendWashDuration.Text = FormatDuration(washMin);
            LegendBreakfastDuration.Text = FormatDuration(breakfastMin);
            LegendCommuteDuration.Text = FormatDuration(commuteMin);
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsPage: UpdateSleepTimelinePreview 失敗: {ex.Message}");
        }
    }

    /// <summary>重畫時間軸 Canvas：精緻現代風格 — 細 bar、pill handle、克制刻度。</summary>
    private void RedrawTimelineCanvas(int sleepMin, int preMin, int washMin, int breakfastMin, int commuteMin)
    {
        TimelineCanvas.Children.Clear();

        var width = TimelineCanvas.ActualWidth > 0 ? TimelineCanvas.ActualWidth : 800;
        var totalRangeMin = (_rangeEnd - _rangeStart).TotalMinutes;
        if (totalRangeMin <= 0) return;
        var pxPerMin = width / totalRangeMin;

        // 取得叢集顏色
        var bBuffer = FindResource("AccentSecondary") as Brush;
        var bSleep = FindResource("AccentPrimary") as Brush;
        var bWash = FindResource("StatusInfo") as Brush;
        var bBreakfast = FindResource("StatusWarning") as Brush;
        var bCommute = FindResource("StatusSuccess") as Brush;
        var bTickMinor = FindResource("BorderPrimary") as Brush;
        var bTickMajor = FindResource("TextTertiary") as Brush;
        var bDivider = FindResource("BgPrimary") as Brush;       // 段間分隔線（背景色）
        var bHandle = FindResource("AccentPrimary") as Brush;    // handle 用紫色
        var bHandleStroke = FindResource("BgPrimary") as Brush;  // handle 邊框
        var bAxisLabel = FindResource("TextTertiary") as Brush;   // 刻度標籤（更淡）
        var bBoundaryLabel = FindResource("TextSecondary") as Brush;
        var bBoundaryTime = FindResource("TextPrimary") as Brush;
        var bDragIndicator = FindResource("AccentPrimary") as Brush;

        // ── 版面配置（canvas 高度 108）──
        // y= 0~2  : 頂部（拖動時 HH:mm 標籤）
        // y= 4~24 : bar（高度 20，pill 形）
        // y=28~44 : 邊界小標籤 + HH:mm
        // y=48~54 : 底部 tick marks（15 分鐘間隔，major=6px / minor=3px 由上往下）
        // y=56~72 : 底部 HH:mm 標籤（每 30 分鐘顯示）
        // y=76~108: 拖動時「已移動」標籤
        const double barTop = 4;
        const double barHeight = 20;
        const double boundaryLabelTop = 28;
        const double boundaryTimeTop = 42;
        const double tickTopY = 48;            // tick 從此 y 開始往下畫
        const double tickMajorHeight = 6;       // 整點刻度長度
        const double tickMinorHeight = 3;      // 15/30/45 分鐘刻度長度
        const double timeLabelTop = 56;        // 底部 HH:mm 標籤 y
        const double dragDeltaTop = 88;

        var barLeft = (_previewBedtime - _rangeStart).TotalMinutes * pxPerMin;
        var barRight = (_previewArrival - _rangeStart).TotalMinutes * pxPerMin;

        // ── 1. bar 陰影（柔和下方陰影，浮起感） ──
        if (bTickMinor != null)
        {
            var shadow = new System.Windows.Shapes.Rectangle
            {
                Width = barRight - barLeft,
                Height = barHeight,
                Fill = bTickMinor,
                RadiusX = 10,
                RadiusY = 10,
                Opacity = 0.2
            };
            Canvas.SetLeft(shadow, barLeft + 1);
            Canvas.SetTop(shadow, barTop + 2);
            TimelineCanvas.Children.Add(shadow);
        }

        // ── 3. 5 個彩色區段（用 Border 才能控制每個角的圓角） ──
        var phases = new (Brush? brush, int mins, CornerRadius radius)[]
        {
            (bBuffer, preMin, new CornerRadius(10, 0, 0, 10)),
            (bSleep, sleepMin, new CornerRadius(0)),
            (bWash, washMin, new CornerRadius(0)),
            (bBreakfast, breakfastMin, new CornerRadius(0)),
            (bCommute, commuteMin, new CornerRadius(0, 10, 10, 0)),
        };

        var cumulativeX = barLeft;
        var phaseBoundaries = new List<(double x, Brush? brush)>();  // 記錄段間分隔位置
        for (var i = 0; i < phases.Length; i++)
        {
            var (brush, mins, radius) = phases[i];
            if (brush == null || mins <= 0) continue;
            var segWidth = mins * pxPerMin;
            var border = new Border
            {
                Width = segWidth,
                Height = barHeight,
                Background = brush,
                CornerRadius = radius,
                BorderThickness = new Thickness(0)
            };
            // 柔和的色塊（稍微降低飽和度）
            border.Opacity = 0.92;
            Canvas.SetLeft(border, cumulativeX);
            Canvas.SetTop(border, barTop);
            TimelineCanvas.Children.Add(border);

            // 記錄段間分隔（除了最後一段）
            if (i < phases.Length - 1)
                phaseBoundaries.Add((cumulativeX + segWidth, brush));

            cumulativeX += segWidth;
        }

        // ── 4. 段間分隔線（1px 背景色垂直線，讓相鄰色塊有清晰邊界） ──
        if (bDivider != null)
        {
            foreach (var (bX, _) in phaseBoundaries)
            {
                var divider = new System.Windows.Shapes.Rectangle
                {
                    Width = 1,
                    Height = barHeight,
                    Fill = bDivider,
                    Opacity = 0.5
                };
                Canvas.SetLeft(divider, bX - 0.5);
                Canvas.SetTop(divider, barTop);
                TimelineCanvas.Children.Add(divider);
            }
        }

        // ── 5. 兩個 pill handle（垂直膠囊，置中於 bar） ──
        const double handleWidth = 6;
        const double handleHeight = 26;
        var handleVerticalOffset = barTop + barHeight / 2 - handleHeight / 2;

        var leftHandle = CreatePillHandle(handleWidth, handleHeight, bHandle, bHandleStroke);
        Canvas.SetLeft(leftHandle, barLeft - handleWidth / 2);
        Canvas.SetTop(leftHandle, handleVerticalOffset);
        TimelineCanvas.Children.Add(leftHandle);

        var rightHandle = CreatePillHandle(handleWidth, handleHeight, bHandle, bHandleStroke);
        Canvas.SetLeft(rightHandle, barRight - handleWidth / 2);
        Canvas.SetTop(rightHandle, handleVerticalOffset);
        TimelineCanvas.Children.Add(rightHandle);

        // ── 6. 邊界標籤（兩行：小標籤 + HH:mm，用對應段顏色） ──
        var wakeTime = _previewArrival.AddMinutes(-(washMin + breakfastMin + commuteMin));
        var wakeX = barLeft + (wakeTime - _previewBedtime).TotalMinutes * pxPerMin;

        // 邊界標籤已移到下方時間軸以紫色高亮顯示，上方不再重複顯示

        // ── 7. 底部時間刻度（15 分鐘間隔 tick + 每 30 分鐘 HH:mm 標籤 + 邊界時間高亮） ──
        // 先記下邊界時間（用於在底部軸上高亮顯示）
        var bedtimeStr = _previewBedtime.ToString("HH:mm");
        var wakeTimeStr = wakeTime.ToString("HH:mm");
        var arrivalTimeStr = _previewArrival.ToString("HH:mm");
        // 邊界時間位置（x, time, label）— 這些會在底部軸上高亮顯示
        var boundaryTimes = new (double x, string time)[]
        {
            (barLeft, bedtimeStr),
            (wakeX, wakeTimeStr),
            (barRight, arrivalTimeStr)
        };

        var labelIndex = 0;
        for (var t = _rangeStart; t <= _rangeEnd; t = t.AddMinutes(15))
        {
            var x = (t - _rangeStart).TotalMinutes * pxPerMin;
            var minute = t.Minute;
            var isHour = minute == 0;
            var isHalf = minute == 30;
            // 檢查是否為邊界時間（就寢/起床/抵達）— 這些用高亮色
            var tStr = t.ToString("HH:mm");
            var isBoundary = false;
            foreach (var bt in boundaryTimes)
            {
                if (bt.time == tStr) { isBoundary = true; break; }
            }

            // 整點最長，半點中等，15/45 最短；邊界時間用最長
            var tickHeight = isBoundary ? tickMajorHeight + 2 : (isHour ? tickMajorHeight : (isHalf ? 4 : tickMinorHeight));
            var tickColor = isBoundary ? bDragIndicator : (isHour ? bTickMajor : bTickMinor);
            if (tickColor != null)
            {
                var line = new System.Windows.Shapes.Line
                {
                    X1 = x, Y1 = tickTopY,
                    X2 = x, Y2 = tickTopY + tickHeight,
                    Stroke = tickColor,
                    StrokeThickness = isBoundary ? 1.5 : (isHour ? 1.2 : 0.5),
                    Opacity = isBoundary ? 1.0 : (isHour ? 0.85 : (isHalf ? 0.6 : 0.4))
                };
                TimelineCanvas.Children.Add(line);
            }

            // 每 30 分鐘顯示 HH:mm 標籤，邊界時間也顯示（紫色高亮）
            if ((isHour || isHalf || isBoundary) && bAxisLabel != null)
            {
                // 整點放上排（y=timeLabelTop），半點/邊界放下排（y=timeLabelTop+12）
                var staggerY = isHour ? timeLabelTop : timeLabelTop + 12;
                labelIndex++;
                var label = new TextBlock
                {
                    Text = t.ToString("HH:mm"),
                    FontSize = isBoundary ? 11 : (isHour ? 11 : 10),
                    FontWeight = isBoundary ? FontWeights.Bold : (isHour ? FontWeights.SemiBold : FontWeights.Normal),
                    Foreground = isBoundary ? bDragIndicator : bAxisLabel,
                    Opacity = isBoundary ? 1.0 : (isHour ? 0.95 : 0.75)
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var lw = label.DesiredSize.Width;
                Canvas.SetLeft(label, Math.Max(2, Math.Min(x - lw / 2, width - lw - 2)));
                Canvas.SetTop(label, staggerY);
                TimelineCanvas.Children.Add(label);
            }
        }

        // ── 8. 拖動中：游標指示線 + 時間 + 移動量 ──
        if ((_dragMode == DragMode.Left || _dragMode == DragMode.Right) && !double.IsNaN(_cursorX) && bDragIndicator != null)
        {
            // 垂直虛線（從頂部貫穿到 底部時間刻度頂端）
            var cursorLine = new System.Windows.Shapes.Line
            {
                X1 = _cursorX, Y1 = 0,
                X2 = _cursorX, Y2 = tickTopY,
                Stroke = bDragIndicator,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Opacity = 0.8
            };
            TimelineCanvas.Children.Add(cursorLine);

            // 計算游標時間（snap 15 分鐘）
            var cursorMinutesFromStart = _cursorX / pxPerMin;
            var cursorTime = _rangeStart.AddMinutes(cursorMinutesFromStart);
            var snappedCursor = cursorTime.AddMinutes(-(cursorTime.Minute % 15));
            if (cursorTime.Minute % 15 >= 8) snappedCursor = snappedCursor.AddMinutes(15);

            // 移動量
            var deltaMin = (_previewBedtime - _dragStartBedtime).TotalMinutes;
            var sign = deltaMin >= 0 ? "+" : "−";
            var absDelta = Math.Abs(deltaMin);
            var deltaStr = absDelta >= 60
                ? $"{sign}{(int)(absDelta / 60)} 小時 {(int)(absDelta % 60)} 分"
                : $"{sign}{(int)absDelta} 分";
            if (absDelta == 0) deltaStr = "±0";

            // 頂部時間 pill
            var currentLabel = new Border
            {
                Background = bDragIndicator,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = $"{snappedCursor:HH:mm}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = bHandleStroke
                }
            };
            currentLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var cw = currentLabel.DesiredSize.Width;
            Canvas.SetLeft(currentLabel, Math.Max(2, Math.Min(_cursorX - cw / 2, width - cw - 2)));
            Canvas.SetTop(currentLabel, 0);
            TimelineCanvas.Children.Add(currentLabel);

            // 底部移動量 pill
            var deltaBrush = deltaMin > 0
                ? FindResource("StatusSuccess") as Brush
                : deltaMin < 0
                    ? FindResource("StatusWarning") as Brush
                    : bAxisLabel;
            if (deltaBrush != null)
            {
                var deltaLabel = new Border
                {
                    Background = deltaBrush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = new TextBlock
                    {
                        Text = $"已移動 {deltaStr}",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = bHandleStroke
                    }
                };
                deltaLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var dw = deltaLabel.DesiredSize.Width;
                Canvas.SetLeft(deltaLabel, Math.Max(2, Math.Min(_cursorX - dw / 2, width - dw - 2)));
                Canvas.SetTop(deltaLabel, dragDeltaTop);
                TimelineCanvas.Children.Add(deltaLabel);
            }
        }
    }

    /// <summary>建立 pill handle（垂直膠囊形狀）。</summary>
    private static Border CreatePillHandle(double width, double height, Brush? fill, Brush? stroke)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(3)
        };
    }

    /// <summary>緊湊版邊界標籤：兩行（小標籤 + HH:mm），用對應段顏色。</summary>
    private void AddBoundaryLabelCompact(double x, double labelY, double timeY,
        string label, string time, Brush? labelColor, Brush? timeColor, Brush? accentColor,
        double canvasWidth, bool alignRight = false, bool center = false)
    {
        if (labelColor == null || timeColor == null) return;

        // 小標籤（就寢/起床/抵達）
        var labelElement = new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.Medium,
            Foreground = labelColor,
            Opacity = 0.85
        };
        labelElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var lw = labelElement.DesiredSize.Width;

        // HH:mm 時間
        var timeElement = new TextBlock
        {
            Text = time,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = timeColor
        };
        timeElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = timeElement.DesiredSize.Width;

        // 對齊
        double lx, tx;
        if (alignRight)
        {
            lx = x - lw - 2;
            tx = x - tw - 2;
        }
        else if (center)
        {
            lx = x - lw / 2;
            tx = x - tw / 2;
        }
        else
        {
            lx = x + 2;
            tx = x + 2;
        }
        lx = Math.Max(2, Math.Min(lx, canvasWidth - lw - 2));
        tx = Math.Max(2, Math.Min(tx, canvasWidth - tw - 2));

        Canvas.SetLeft(labelElement, lx);
        Canvas.SetTop(labelElement, labelY);
        TimelineCanvas.Children.Add(labelElement);

        Canvas.SetLeft(timeElement, tx);
        Canvas.SetTop(timeElement, timeY);
        TimelineCanvas.Children.Add(timeElement);

        // 上方加一個小色點（用 accentColor，強化對應關係）
        if (accentColor != null)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = accentColor,
                Opacity = 0.9
            };
            Canvas.SetLeft(dot, x - 2);
            Canvas.SetTop(dot, labelY - 6);
            TimelineCanvas.Children.Add(dot);
        }
    }

    /// <summary>在 Canvas 上加一個時間標籤，alignRight 時向左對齊避免超出右邊界。</summary>
    private void AddBoundaryLabel(double x, double y, string text, Brush? foreground, bool alignRight = false)
    {
        if (foreground == null) return;
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground
        };
        // 先量測
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = label.DesiredSize.Width;
        Canvas.SetLeft(label, alignRight ? x - w - 4 : x + 2);
        Canvas.SetTop(label, y);
        TimelineCanvas.Children.Add(label);
    }

    // ═══════════════════════════════════════════════════════
    //  時間軸拉動事件
    // ═══════════════════════════════════════════════════════

    /// <summary>Canvas 大小改變時重畫（首次載入或視窗縮放後 ActualWidth 才有值）。</summary>
    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitialized && e.NewSize.Width > 0)
        {
            UpdateSleepTimelinePreview();
        }
    }

    /// <summary>滑鼠按下：若點在彩色 bar 內或 handle 上，開始拖動。</summary>
    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TimelineCanvas.ActualWidth <= 0) return;
        var pos = e.GetPosition(TimelineCanvas);
        var totalRangeMin = (_rangeEnd - _rangeStart).TotalMinutes;
        var pxPerMin = TimelineCanvas.ActualWidth / totalRangeMin;
        var barLeft = (_previewBedtime - _rangeStart).TotalMinutes * pxPerMin;
        var barRight = (_previewArrival - _rangeStart).TotalMinutes * pxPerMin;

        // 點擊位置必須在 bar 範圍（含 handle 外擴）：y=0~30
        if (pos.Y < 0 || pos.Y > 30) return;
        if (pos.X < barLeft - 12 || pos.X > barRight + 12) return;

        // 判斷點擊哪個 handle：左 handle 在 barLeft 附近，右 handle 在 barRight 附近
        var distToLeft = Math.Abs(pos.X - barLeft);
        var distToRight = Math.Abs(pos.X - barRight);

        if (distToLeft < 15 && distToLeft <= distToRight)
        {
            // 拉左 handle：改就寢，抵達固定
            _dragMode = DragMode.Left;
            _dragStartX = pos.X;
            _dragStartBedtime = _previewBedtime;
            _dragStartArrival = _previewArrival;
            TimelineCanvas.CaptureMouse();
            TimelineCanvas.Cursor = Cursors.SizeWE;  // 左右箭頭
        }
        else if (distToRight < 15)
        {
            // 拉右 handle：改抵達，就寢固定
            _dragMode = DragMode.Right;
            _dragStartX = pos.X;
            _dragStartBedtime = _previewBedtime;
            _dragStartArrival = _previewArrival;
            TimelineCanvas.CaptureMouse();
            TimelineCanvas.Cursor = Cursors.SizeWE;
        }
        // 點擊中間不觸發拖動（避免前後一起動）
    }

    /// <summary>滑鼠移動：拖動 handle 時改 bedtime 或 arrival（snap 15 分鐘，另一端固定）。</summary>
    private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(TimelineCanvas);

        // 拖動中
        if (_dragMode == DragMode.Left || _dragMode == DragMode.Right)
        {
            _cursorX = pos.X;
            var totalRangeMin = (_rangeEnd - _rangeStart).TotalMinutes;
            var pxPerMin = TimelineCanvas.ActualWidth / totalRangeMin;
            var deltaMin = (pos.X - _dragStartX) / pxPerMin;
            // Snap to 15 min
            var snappedDelta = Math.Round(deltaMin / 15.0) * 15;

            // 讀取其他 4 個輸入值（睡眠以外的固定時長）
            int preMin = int.TryParse(PreSleepTextBox.Text, out var pm) && pm >= 0 ? pm : 15;
            int washMin = int.TryParse(WashTextBox.Text, out var wm) && wm > 0 ? wm : 30;
            int breakfastMin = int.TryParse(BreakfastTextBox.Text, out var bm) && bm > 0 ? bm : 45;
            int commuteMin = int.TryParse(CommuteTextBox.Text, out var cm) && cm > 0 ? cm : 105;
            var fixedDur = preMin + washMin + breakfastMin + commuteMin;

            if (_dragMode == DragMode.Left)
            {
                // 拉左 handle：改 bedtime，arrival 固定
                var newBedtime = _dragStartBedtime.AddMinutes(snappedDelta);
                // 計算新睡眠時長
                var newSleepMin = (int)(_dragStartArrival - newBedtime).TotalMinutes - fixedDur;
                // 限制：睡眠不能 < MinSleepMinutes，bar 不能跑出 canvas
                if (newSleepMin < MinSleepMinutes)
                {
                    newSleepMin = MinSleepMinutes;
                    newBedtime = _dragStartArrival.AddMinutes(-(newSleepMin + fixedDur));
                }
                if (newBedtime < _rangeStart) newBedtime = _rangeStart;
                // 重算 sleepMin after clamp
                newSleepMin = (int)(_dragStartArrival - newBedtime).TotalMinutes - fixedDur;
                if (newSleepMin < MinSleepMinutes)
                {
                    newSleepMin = MinSleepMinutes;
                    newBedtime = _dragStartArrival.AddMinutes(-(newSleepMin + fixedDur));
                }

                _previewBedtime = newBedtime;
                _previewArrival = _dragStartArrival;  // 固定
                // 更新睡眠輸入框（時/分）+ 重畫（範圍維持不變）
                _isUpdatingFromDrag = true;
                SleepHoursTextBox.Text = (newSleepMin / 60).ToString();
                SleepMinutesTextBox.Text = (newSleepMin % 60).ToString();
                UpdateSleepTimelinePreview();
                _isUpdatingFromDrag = false;
            }
            else  // DragMode.Right
            {
                // 拉右 handle：改 arrival，bedtime 固定
                var newArrival = _dragStartArrival.AddMinutes(snappedDelta);
                var newSleepMin = (int)(newArrival - _dragStartBedtime).TotalMinutes - fixedDur;
                if (newSleepMin < MinSleepMinutes)
                {
                    newSleepMin = MinSleepMinutes;
                    newArrival = _dragStartBedtime.AddMinutes(newSleepMin + fixedDur);
                }
                if (newArrival > _rangeEnd) newArrival = _rangeEnd;
                newSleepMin = (int)(newArrival - _dragStartBedtime).TotalMinutes - fixedDur;
                if (newSleepMin < MinSleepMinutes)
                {
                    newSleepMin = MinSleepMinutes;
                    newArrival = _dragStartBedtime.AddMinutes(newSleepMin + fixedDur);
                }

                _previewBedtime = _dragStartBedtime;  // 固定
                _previewArrival = newArrival;
                _isUpdatingFromDrag = true;
                SleepHoursTextBox.Text = (newSleepMin / 60).ToString();
                SleepMinutesTextBox.Text = (newSleepMin % 60).ToString();
                UpdateSleepTimelinePreview();
                _isUpdatingFromDrag = false;
            }
        }
        else
        {
            // Hover：在 handle 附近顯示左右箭頭
            if (TimelineCanvas.ActualWidth > 0)
            {
                var totalRangeMin = (_rangeEnd - _rangeStart).TotalMinutes;
                var pxPerMin = TimelineCanvas.ActualWidth / totalRangeMin;
                var barLeft = (_previewBedtime - _rangeStart).TotalMinutes * pxPerMin;
                var barRight = (_previewArrival - _rangeStart).TotalMinutes * pxPerMin;
                var distLeft = Math.Abs(pos.X - barLeft);
                var distRight = Math.Abs(pos.X - barRight);
                if (pos.Y >= 0 && pos.Y <= 30 &&
                    ((distLeft < 15) || (distRight < 15) ||
                     (pos.X >= barLeft - 10 && pos.X <= barRight + 10)))
                {
                    TimelineCanvas.Cursor = (distLeft < 15 || distRight < 15)
                        ? Cursors.SizeWE : Cursors.SizeAll;
                }
                else
                {
                    TimelineCanvas.Cursor = Cursors.Arrow;
                }
            }
        }
    }

    /// <summary>滑鼠放開：結束拖動。</summary>
    private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            _cursorX = double.NaN;
            TimelineCanvas.ReleaseMouseCapture();
            TimelineCanvas.Cursor = Cursors.Arrow;
            UpdateSleepTimelinePreview();
        }
    }

    /// <summary>滑鼠離開 Canvas：結束拖動並還原游標。</summary>
    private void TimelineCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            _cursorX = double.NaN;
            TimelineCanvas.ReleaseMouseCapture();
            TimelineCanvas.Cursor = Cursors.Arrow;
            UpdateSleepTimelinePreview();
        }
        else
        {
            TimelineCanvas.Cursor = Cursors.Arrow;
        }
    }

    /// <summary>將分鐘數格式化為「X 小時 Y 分」或「Y 分」字串。</summary>
    private static string FormatDuration(int minutes)
    {
        if (minutes < 60) return $"{minutes} 分";
        var h = minutes / 60;
        var m = minutes % 60;
        return m > 0 ? $"{h} 小時 {m} 分" : $"{h} 小時";
    }

    /// <summary>語言下拉變更：即時切換並持久化。</summary>
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || _languageService == null) return;
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
        var code = item.Tag as string;
        if (string.IsNullOrEmpty(code)) return;
        if (code == _languageService.CurrentLanguage) return;
        _languageService.SetLanguage(code);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = CalendarUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_EmptyUrl");
                return;
            }

            // 睡眠時長從「時」「分」兩個輸入框組合
            if (!int.TryParse(SleepHoursTextBox.Text, out var sleepH) || sleepH < 0 ||
                !int.TryParse(SleepMinutesTextBox.Text, out var sleepM) || sleepM < 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidSleep");
                return;
            }
            var sleepMinutes = sleepH * 60 + sleepM;
            if (sleepMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidSleep");
                return;
            }

            if (!int.TryParse(PreSleepTextBox.Text, out var preSleepMinutes) || preSleepMinutes < 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidPreSleep");
                return;
            }

            if (!int.TryParse(WashTextBox.Text, out var washMinutes) || washMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidWash");
                return;
            }

            if (!int.TryParse(BreakfastTextBox.Text, out var breakfastMinutes) || breakfastMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidBreakfast");
                return;
            }

            if (!int.TryParse(CommuteTextBox.Text, out var commuteMinutes) || commuteMinutes <= 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidCommute");
                return;
            }

            if (!int.TryParse(CutoffHourTextBox.Text, out var cutoffHour) || cutoffHour < 0 || cutoffHour > 23)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidCutoff");
                return;
            }

            if (!int.TryParse(DelayQuotaTextBox.Text, out var delayQuota) || delayQuota < 0)
            {
                ShowWarning("Settings_ValidationError", "Settings_Validation_InvalidQuota");
                return;
            }

            _settingsService.SetCalendarUrl(url);
            _settingsService.SetSleepTime(TimeSpan.FromMinutes(sleepMinutes));
            _settingsService.SetPreSleepBuffer(TimeSpan.FromMinutes(preSleepMinutes));
            _settingsService.SetWashTime(TimeSpan.FromMinutes(washMinutes));
            _settingsService.SetBreakfastTime(TimeSpan.FromMinutes(breakfastMinutes));
            _settingsService.SetCommuteTime(TimeSpan.FromMinutes(commuteMinutes));
            _settingsService.SetEarlyClassCutoffHour(cutoffHour);
            _settingsService.SetEarlyClassDelayQuotaMinutes(delayQuota);
            _settingsService.SetStartWithWindows(StartWithWindowsCheckBox.IsChecked ?? false);

            // Immediately restart orchestrator with new URL
            var orchestrator = App.Services.GetService(typeof(BehaviorOrchestrator)) as BehaviorOrchestrator;
            if (orchestrator != null)
            {
                App.ApplyOrchestratorSettings(orchestrator, _settingsService);
                await orchestrator.RestartAsync(url);
            }

            var successTitle = TryFindResource("Common_Success") as string ?? "成功";
            var successBody = TryFindResource("Settings_SettingsSavedSuccess") as string ?? "設定已儲存，日曆數據已即時載入。";
            var successDialog = new WarningDialog(successTitle, successBody, icon: "✅");
            WarningDialog.ShowSingleton(successDialog);

            // Navigate to dashboard to show updated data
            if (App.MainWindow != null)
            {
                App.MainWindow.NavigateToDashboard();
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 儲存設定失敗", ex);
            var errTitle = TryFindResource("Common_Error") as string ?? "錯誤";
            var errFmt = TryFindResource("Settings_SaveFailed") as string ?? "儲存設定失敗：{0}";
            var dialog = new WarningDialog(errTitle, string.Format(errFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TryFindResource("Common_Confirm") as string ?? "確認";
        var body = TryFindResource("Settings_ClearCacheConfirm") as string ?? "確定要清除所有快取的日曆數據嗎？";
        var dialog = new WarningDialog(title, body, icon: "🗑️");
        var btnText = TryFindResource("Settings_ConfirmClearCache") as string ?? "確認清除";
        dialog.SetConfirmMode(btnText, isDanger: true);
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            _cacheService.ClearAll();
            var successTitle = TryFindResource("Common_Success") as string ?? "成功";
            var successBody = TryFindResource("Settings_CacheClearedSuccess") as string ?? "快取已清除成功。";
            var successDialog = new WarningDialog(successTitle, successBody, icon: "✅");
            WarningDialog.ShowSingleton(successDialog);
        }
    }

    private void DarkModeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _themeService == null) return;
        _themeService.SetTheme(AppTheme.Dark);
    }

    private void DarkModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _themeService == null) return;
        _themeService.SetTheme(AppTheme.Light);
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logsWindow = new LogsWindow
            {
                Owner = Window.GetWindow(this)
            };
            logsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 無法開啟日誌視窗", ex);
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var bodyFmt = TryFindResource("Settings_LogsOpenFail") as string ?? "無法開啟日誌視窗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    private void ViewDelayHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var historyWindow = new DelayHistoryWindow
            {
                Owner = Window.GetWindow(this)
            };
            historyWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggerService.Error("SettingsPage: 無法開啟延遲歷史視窗", ex);
            var title = TryFindResource("Common_Error") as string ?? "錯誤";
            var bodyFmt = TryFindResource("Settings_HistoryOpenFail") as string ?? "無法開啟延遲歷史視窗：{0}";
            var dialog = new WarningDialog(title, string.Format(bodyFmt, ex.Message), icon: "❌");
            WarningDialog.ShowSingleton(dialog);
        }
    }

    /// <summary>顯示警告對話框（取代 MessageBox）。傳入資源 key。</summary>
    private void ShowWarning(string titleKey, string messageKey)
    {
        var title = TryFindResource(titleKey) as string ?? titleKey;
        var message = TryFindResource(messageKey) as string ?? messageKey;
        var dialog = new WarningDialog(title, message, icon: "⚠️");
        WarningDialog.ShowSingleton(dialog);
    }
}
