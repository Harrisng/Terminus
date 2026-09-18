using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Terminus.Core.Services;
using Terminus.Core.Models;
using NodaTime;

namespace Terminus.UI.Pages;

public partial class CalendarPage : Page
{
    private readonly CalendarDataService _calendarService;
    private DateTime _currentMonth;
    private List<CalendarEvent>? _events;
    private DateTime? _selectedDate;
    private bool _isWeekView;
    private DateTime _weekStartDate;

    private const int HourStart = 7;
    private const int HourEnd = 19;
    private const double HourHeight = 48;

    public CalendarPage(CalendarDataService calendarService)
    {
        try
        {
            InitializeComponent();
            _calendarService = calendarService;
            _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            _isWeekView = false;

            InitializeDayHeaders();
            InitializeWeekHeaders();
            SetViewStyle();
            RenderCalendar();
            LoadEventsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"月曆頁面初始化失敗：{ex.Message}\n\n{ex.StackTrace}",
                "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InitializeDayHeaders()
    {
        DayHeadersGrid.Children.Clear();
        var dayNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        for (int i = 0; i < 7; i++)
        {
            var border = new Border
            {
                Padding = new Thickness(0, 8, 0, 8),
                CornerRadius = new CornerRadius(4)
            };
            border.SetResourceReference(Border.BackgroundProperty, "BgTertiary");

            var textBlock = new TextBlock
            {
                Text = dayNames[i],
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (i == 0 || i == 6)
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "StatusError");
            else
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

            border.Child = textBlock;
            Grid.SetColumn(border, i);
            DayHeadersGrid.Children.Add(border);
        }
    }

    private void InitializeWeekHeaders()
    {
        WeekHeadersGrid.Children.Clear();

        // Empty corner cell
        var corner = new Border
        {
            Background = (Brush)FindResource("BgHeader"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        corner.SetResourceReference(Border.BorderBrushProperty, "BorderPrimary");
        Grid.SetColumn(corner, 0);
        WeekHeadersGrid.Children.Add(corner);

        var dayNames = new[] { "週日", "週一", "週二", "週三", "週四", "週五", "週六" };
        var today = DateTime.Today;

        for (int i = 0; i < 7; i++)
        {
            var date = _weekStartDate.AddDays(i);
            var isToday = date.Date == today.Date;

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            border.SetResourceReference(Border.BorderBrushProperty, "BorderPrimary");
            border.SetResourceReference(Border.BackgroundProperty, isToday ? "AccentSoft" : "BgHeader");

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var dayText = new TextBlock
            {
                Text = dayNames[i],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dayText.SetResourceReference(TextBlock.ForegroundProperty, isToday ? "TextAccent" : "TextSecondary");

            var dateText = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 15,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            dateText.SetResourceReference(TextBlock.ForegroundProperty, isToday ? "TextAccent" : "TextPrimary");

            panel.Children.Add(dayText);
            panel.Children.Add(dateText);
            border.Child = panel;
            Grid.SetColumn(border, i + 1);
            WeekHeadersGrid.Children.Add(border);
        }
    }

    private async void LoadEventsAsync()
    {
        await RefreshEventsAsync();
    }

    public async Task RefreshEventsAsync()
    {
        try
        {
            Terminus.Core.Services.LoggerService.Info("CalendarPage: RefreshEventsAsync 開始");

            var settingsService = App.Services.GetService(typeof(Terminus.UI.Services.SettingsService)) as Terminus.UI.Services.SettingsService;
            var url = settingsService?.GetCalendarUrl() ?? "";

            if (string.IsNullOrEmpty(url))
            {
                Terminus.Core.Services.LoggerService.Warn("CalendarPage: 日曆 URL 為空，無法載入事件");
                return;
            }

            Terminus.Core.Services.LoggerService.Info($"CalendarPage: 正在載入日曆, URL={url.Substring(0, Math.Min(60, url.Length))}...");

            var (events, status, cacheAge) = await _calendarService.GetScheduleAsync(url);

            Terminus.Core.Services.LoggerService.Info($"CalendarPage: 載入完成, 事件數={events.Count}, 狀態={status}, 快取年齡={cacheAge}h");

            if (events.Count > 0)
            {
                var firstEvent = events.First();
                var lastEvent = events.Last();
                Terminus.Core.Services.LoggerService.Info($"CalendarPage: 事件範圍 {firstEvent.Start.ToDateTimeUnspecified():yyyy-MM-dd} ~ {lastEvent.Start.ToDateTimeUnspecified():yyyy-MM-dd}");
                Terminus.Core.Services.LoggerService.Info($"CalendarPage: 前5個事件: {string.Join(", ", events.Take(5).Select(e => $"{e.Start.ToDateTimeUnspecified():MM-dd HH:mm} {e.Summary}"))}");
            }
            else
            {
                Terminus.Core.Services.LoggerService.Warn("CalendarPage: 載入到 0 個事件");
            }

            _events = events;
            RenderCalendar();
            if (_isWeekView) RenderWeekView();
        }
        catch (Exception ex)
        {
            Terminus.Core.Services.LoggerService.Error("CalendarPage: RefreshEventsAsync 失敗", ex);
        }
    }

    public void RefreshEvents()
    {
        _ = RefreshEventsAsync();
    }

    // --- View Toggle ---

    private void SetViewStyle()
    {
        if (_isWeekView)
        {
            PageTitleText.Text = "週曆";
            PageSubtitleText.Text = "本週課程時間表";
            MonthViewBorder.Visibility = Visibility.Collapsed;
            WeekViewBorder.Visibility = Visibility.Visible;
            MonthViewButton.ClearValue(BackgroundProperty);
            WeekViewButton.Background = (Brush)FindResource("AccentSoft");
            WeekViewButton.Foreground = (Brush)FindResource("TextAccent");
            MonthViewButton.Foreground = (Brush)FindResource("TextSecondary");
            MonthTitleText.Text = $"{_weekStartDate:M/d} - {_weekStartDate.AddDays(6):M/d}";
        }
        else
        {
            PageTitleText.Text = "月曆";
            PageSubtitleText.Text = "點擊日期查看課程詳情";
            MonthViewBorder.Visibility = Visibility.Visible;
            WeekViewBorder.Visibility = Visibility.Collapsed;
            MonthViewButton.Background = (Brush)FindResource("AccentSoft");
            MonthViewButton.Foreground = (Brush)FindResource("TextAccent");
            WeekViewButton.ClearValue(BackgroundProperty);
            WeekViewButton.Foreground = (Brush)FindResource("TextSecondary");
        }
    }

    private void MonthViewButton_Click(object sender, RoutedEventArgs e)
    {
        _isWeekView = false;
        SetViewStyle();
        RenderCalendar();
    }

    private void WeekViewButton_Click(object sender, RoutedEventArgs e)
    {
        _isWeekView = true;
        SetViewStyle();
        InitializeWeekHeaders();
        RenderWeekView();
    }

    // --- Month View ---

    private void RenderCalendar()
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (int i = 0; i < 7; i++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var year = _currentMonth.Year;
        var month = _currentMonth.Month;
        MonthTitleText.Text = $"{year}年{month}月";

        var firstDay = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDayOfWeek = (int)firstDay.DayOfWeek;

        var totalCells = startDayOfWeek + daysInMonth;
        var totalWeeks = (int)Math.Ceiling(totalCells / 7.0);
        if (totalCells % 7 != 0) totalWeeks++;

        for (int row = 0; row < totalWeeks; row++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var today = DateTime.Today;
        var prevMonth = firstDay.AddMonths(-1);
        var prevMonthDaysInMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

        for (int i = 0; i < totalWeeks * 7; i++)
        {
            var row = i / 7;
            var col = i % 7;

            DateTime date;
            bool isCurrentMonth;

            if (i < startDayOfWeek)
            {
                date = prevMonth.AddDays(prevMonthDaysInMonth - startDayOfWeek + i);
                isCurrentMonth = false;
            }
            else if (i < totalCells)
            {
                date = new DateTime(year, month, i - startDayOfWeek + 1);
                isCurrentMonth = true;
            }
            else
            {
                date = firstDay.AddMonths(1).AddDays(i - totalCells);
                isCurrentMonth = false;
            }

            var cell = CreateDayCell(date, isCurrentMonth, date == today);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            CalendarGrid.Children.Add(cell);
        }
    }

    private Border CreateDayCell(DateTime date, bool isCurrentMonth, bool isToday)
    {
        var cell = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(1),
            Padding = new Thickness(6),
            Cursor = Cursors.Hand,
            MinHeight = 70,
            Tag = date
        };

        string bgKey;
        if (isToday)
            bgKey = "AccentSoft";
        else if (isCurrentMonth)
            bgKey = "BgSecondary";
        else
            bgKey = "BgPrimary";

        cell.SetResourceReference(Border.BackgroundProperty, bgKey);

        if (isToday)
        {
            cell.BorderThickness = new Thickness(1);
            cell.SetResourceReference(Border.BorderBrushProperty, "AccentPrimary");
        }
        else
        {
            cell.BorderThickness = new Thickness(0);
        }

        if (_selectedDate.HasValue && _selectedDate.Value.Date == date.Date)
        {
            cell.SetResourceReference(Border.BackgroundProperty, "AccentActive");
        }

        cell.MouseLeftButtonUp += (s, e) => OnDayCellClicked(date);

        var panel = new StackPanel();

        var dayNumber = new TextBlock
        {
            Text = date.Day.ToString(),
            FontSize = isCurrentMonth ? 14 : 12,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 0, 0, 0)
        };

        if (isToday)
            dayNumber.SetResourceReference(TextBlock.ForegroundProperty, "TextAccent");
        else if (isCurrentMonth)
            dayNumber.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        else
            dayNumber.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");

        panel.Children.Add(dayNumber);

        if (_events != null)
        {
            var dayEvents = _events
                .Where(e => e.Start.ToDateTimeUnspecified().Date == date.Date)
                .Take(3)
                .ToList();

            // Log for debugging
            if (dayEvents.Count > 0)
            {
                Terminus.Core.Services.LoggerService.Info($"CalendarPage: 日期 {date:yyyy-MM-dd} 有 {dayEvents.Count} 個事件");
            }

            foreach (var evt in dayEvents)
            {
                var eventPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                dot.SetResourceReference(Ellipse.FillProperty, "AccentPrimary");

                var evtText = new TextBlock
                {
                    Text = $"{evt.Start.ToDateTimeUnspecified():HH:mm} {evt.Summary}",
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 100
                };
                evtText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

                eventPanel.Children.Add(dot);
                eventPanel.Children.Add(evtText);
                panel.Children.Add(eventPanel);
            }

            var totalDayEvents = _events.Count(e => e.Start.ToDateTimeUnspecified().Date == date.Date);
            if (totalDayEvents > 3)
            {
                var moreText = new TextBlock
                {
                    Text = $"+{totalDayEvents - 3} 更多",
                    FontSize = 9,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                moreText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                panel.Children.Add(moreText);
            }
        }

        cell.Child = panel;
        return cell;
    }

    private void OnDayCellClicked(DateTime date)
    {
        try
        {
            _selectedDate = date;
            RenderCalendar();
            ShowDayDetail(date);
        }
        catch (Exception ex)
        {
            Terminus.Core.Services.LoggerService.Error("CalendarPage: OnDayCellClicked 失敗", ex);
        }
    }

    // --- Week View ---

    private void RenderWeekView()
    {
        WeekTimeGrid.Children.Clear();
        WeekTimeGrid.RowDefinitions.Clear();
        WeekTimeGrid.ColumnDefinitions.Clear();

        // Time column + 7 day columns
        WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        for (int i = 0; i < 7; i++)
            WeekTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var totalHours = HourEnd - HourStart;
        for (int i = 0; i <= totalHours; i++)
        {
            WeekTimeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HourHeight) });

            // Time label
            var timeLabel = new TextBlock
            {
                Text = $"{HourStart + i:00}:00",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0)
            };
            timeLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
            Grid.SetRow(timeLabel, i);
            Grid.SetColumn(timeLabel, 0);
            WeekTimeGrid.Children.Add(timeLabel);

            // Hour row background + gridlines for each day column
            for (int d = 0; d < 7; d++)
            {
                var date = _weekStartDate.AddDays(d);
                var isToday = date.Date == DateTime.Today;

                var cellBorder = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1)
                };
                cellBorder.SetResourceReference(Border.BorderBrushProperty, "BorderPrimary");
                cellBorder.SetResourceReference(Border.BackgroundProperty, isToday ? "AccentSoft" : "BgPrimary");
                Grid.SetRow(cellBorder, i);
                Grid.SetColumn(cellBorder, d + 1);
                WeekTimeGrid.Children.Add(cellBorder);
            }
        }

        // Render event blocks
        if (_events != null)
        {
            var weekEnd = _weekStartDate.AddDays(7);

            var weekEvents = _events
                .Where(e => {
                    var evtDate = e.Start.ToDateTimeUnspecified();
                    return evtDate >= _weekStartDate && evtDate < weekEnd;
                })
                .OrderBy(e => e.Start.ToDateTimeUnspecified())
                .ToList();

            foreach (var evt in weekEvents)
            {
                RenderWeekEventBlock(evt);
            }
        }

        // Current time indicator
        RenderCurrentTimeLine();
    }

    private void RenderWeekEventBlock(CalendarEvent evt)
    {
        var evtStart = evt.Start.ToDateTimeUnspecified();
        var evtEnd = evt.End.ToDateTimeUnspecified();

        // Clamp to visible hours
        var visibleStart = Math.Max(evtStart.Hour + evtStart.Minute / 60.0, HourStart);
        var visibleEnd = Math.Min(evtEnd.Hour + evtEnd.Minute / 60.0, HourEnd);

        if (visibleEnd <= visibleStart) return;

        var rowStart = visibleStart - HourStart;
        var rowSpan = Math.Max(1, visibleEnd - visibleStart);

        var dayOfWeek = (int)evtStart.DayOfWeek;

        var block = new Border
        {
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2),
            Padding = new Thickness(6, 4, 6, 4),
            Cursor = Cursors.Hand
        };
        block.SetResourceReference(Border.BackgroundProperty, "AccentPrimary");
        block.SetResourceReference(Border.BorderBrushProperty, "AccentPrimary");
        block.BorderThickness = new Thickness(0, 0, 0, 0);
        block.Opacity = 0.85;

        var panel = new StackPanel();

        var timeText = new TextBlock
        {
            Text = $"{evtStart:HH:mm} - {evtEnd:HH:mm}",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextWhite");

        var titleText = new TextBlock
        {
            Text = evt.Summary,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0)
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextWhite");

        var locationText = new TextBlock
        {
            Text = evt.Location,
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0)
        };
        locationText.SetResourceReference(TextBlock.ForegroundProperty, "TextWhite");

        panel.Children.Add(timeText);
        panel.Children.Add(titleText);
        if (!string.IsNullOrEmpty(evt.Location))
            panel.Children.Add(locationText);

        block.Child = panel;

        block.MouseLeftButtonUp += (s, e) =>
        {
            _selectedDate = evtStart;
            ShowDayDetail(evtStart);
        };

        Grid.SetRow(block, (int)rowStart);
        Grid.SetRowSpan(block, (int)Math.Ceiling(rowSpan));
        Grid.SetColumn(block, dayOfWeek + 1);
        WeekTimeGrid.Children.Add(block);
    }

    private void RenderCurrentTimeLine()
    {
        var now = DateTime.Now;
        if (now.Hour < HourStart || now.Hour >= HourEnd) return;

        // Check if today is in the current week view
        if (now < _weekStartDate || now >= _weekStartDate.AddDays(7)) return;

        var hourOffset = now.Hour - HourStart + now.Minute / 60.0;
        var yOffset = hourOffset * HourHeight;

        var todayLine = new Line
        {
            X1 = 50,
            X2 = 1000,
            Y1 = yOffset,
            Y2 = yOffset,
            StrokeThickness = 1.5
        };
        todayLine.SetResourceReference(Line.StrokeProperty, "StatusError");

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(46, yOffset - 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        dot.SetResourceReference(Ellipse.FillProperty, "StatusError");

        Grid.SetRowSpan(todayLine, HourEnd - HourStart + 1);
        Grid.SetColumnSpan(todayLine, 8);
        WeekTimeGrid.Children.Add(todayLine);
    }

    // --- Detail Panel ---

    private void ShowDayDetail(DateTime date)
    {
        DetailPanel.Visibility = Visibility.Visible;
        DetailDateText.Text = date.ToString("yyyy年MM月dd日 dddd");

        EventListPanel.Children.Clear();

        if (_events == null)
        {
            var noData = new TextBlock
            {
                Text = "未載入課程數據，請先在設定中配置日曆 URL。",
                FontSize = 13
            };
            noData.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
            EventListPanel.Children.Add(noData);
            return;
        }

        var dayEvents = _events
            .Where(e => e.Start.ToDateTimeUnspecified().Date == date.Date)
            .OrderBy(e => e.Start.ToDateTimeUnspecified())
            .ToList();

        if (dayEvents.Count == 0)
        {
            var noEvents = new TextBlock
            {
                Text = "此日無課程",
                FontSize = 13
            };
            noEvents.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
            EventListPanel.Children.Add(noEvents);
            return;
        }

        foreach (var evt in dayEvents)
        {
            var item = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6)
            };
            item.SetResourceReference(Border.BackgroundProperty, "BgTertiary");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timeText = new TextBlock
            {
                Text = $"{evt.Start.ToDateTimeUnspecified():HH:mm} - {evt.End.ToDateTimeUnspecified():HH:mm}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextAccent");
            Grid.SetColumn(timeText, 0);

            var titleText = new TextBlock
            {
                Text = evt.Summary,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(titleText, 1);

            var locText = new TextBlock
            {
                Text = evt.Location,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            locText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
            Grid.SetColumn(locText, 2);

            grid.Children.Add(timeText);
            grid.Children.Add(titleText);
            grid.Children.Add(locText);
            item.Child = grid;
            EventListPanel.Children.Add(item);
        }
    }

    private void CloseDetailButton_Click(object sender, RoutedEventArgs e)
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        _selectedDate = null;
        RenderCalendar();
    }

    // --- Navigation ---

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWeekView)
        {
            _weekStartDate = _weekStartDate.AddDays(-7);
            InitializeWeekHeaders();
            RenderWeekView();
        }
        else
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            RenderCalendar();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWeekView)
        {
            _weekStartDate = _weekStartDate.AddDays(7);
            InitializeWeekHeaders();
            RenderWeekView();
        }
        else
        {
            _currentMonth = _currentMonth.AddMonths(1);
            RenderCalendar();
        }
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        if (_isWeekView)
        {
            InitializeWeekHeaders();
            RenderWeekView();
        }
        else
        {
            RenderCalendar();
        }
    }
}
