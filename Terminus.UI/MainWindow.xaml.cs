using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Terminus.Core.Services;
using Terminus.UI.Pages;
using Terminus.UI.Services;
using Terminus.UI.Themes;

namespace Terminus.UI;

public partial class MainWindow : Window
{
    private readonly BehaviorOrchestrator _orchestrator = null!;
    private readonly CalendarDataService _calendarService = null!;
    private readonly ThemeService _themeService = null!;

    private DashboardPage? _dashboardPage;
    private CalendarPage? _calendarPage;
    private SettingsPage? _settingsPage;

    // ── 無邊框視窗：自繪最大化狀態與可調整大小的邊緣命中測試 ──
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const string MoonGeometry =
        "M13.2 3.4a6.8 6.8 0 1 0 3.4 11.5 5.6 5.6 0 0 1-3.4-11.5z";

    private const string SunGeometry =
        "M10 6.2a3.8 3.8 0 1 0 0 7.6 3.8 3.8 0 0 0 0-7.6M10 2.2v2M10 15.8v2M2.2 10h2M15.8 10h2"
        + "M4.6 4.6 6 6M14 14l1.4 1.4M15.4 4.6 14 6M6 14l-1.4 1.4";

    private bool _isMaximized;
    private Rect _restoreBounds;

    // ── 多顯示器支援：取視窗目前所在螢幕的工作區 ──
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>視窗目前所在顯示器的工作區（DIP）。失敗時退回主屏 WorkArea。</summary>
    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            var hMon = MonitorFromWindow(handle, 2 /* MONITOR_DEFAULTTONEAREST */);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                // rcWork 是裝置像素，轉回 DIP
                double dpiX = 1, dpiY = 1;
                var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
                if (ct != null)
                {
                    dpiX = ct.TransformToDevice.M11;
                    dpiY = ct.TransformToDevice.M22;
                    if (dpiX <= 0) dpiX = 1;
                    if (dpiY <= 0) dpiY = 1;
                }
                return new Rect(
                    mi.rcWork.Left / dpiX,
                    mi.rcWork.Top / dpiY,
                    (mi.rcWork.Right - mi.rcWork.Left) / dpiX,
                    (mi.rcWork.Bottom - mi.rcWork.Top) / dpiY);
            }
        }
        return SystemParameters.WorkArea;
    }

    public MainWindow(BehaviorOrchestrator orchestrator, CalendarDataService calendarService, ThemeService themeService)
    {
        try
        {
            _orchestrator = orchestrator;
            _calendarService = calendarService;
            _themeService = themeService;

            InitializeComponent();

            SizeChanged += (_, _) => UpdateRootClip();
            Loaded += (_, _) => FitToWorkArea();
            UpdateRootClip();

            _themeService.ThemeChanged += OnThemeChanged;
            UpdateThemeButton();

            NavigateToDashboard();
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 初始化失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_InitError") as string ?? "初始化錯誤",
                string.Format(TryFindResource("Window_InitErrorBody") as string ?? "主視窗初始化失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    // ══════════ 無邊框視窗外觀 ══════════

    /// <summary>依視窗尺寸更新圓角裁切，避免環境光溢出圓角。</summary>
    private void UpdateRootClip()
    {
        var w = RootClipHost.ActualWidth;
        var h = RootClipHost.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var radius = 22.0;  // 最大化也保留圓角（工作區放大而非原生全螢幕）
        RootClipHost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
    }

    /// <summary>小螢幕（如 720p/768p 筆電）上預設 1180×780 會溢出工作區，啟動時收斂到工作區內並重新置中。</summary>
    private void FitToWorkArea()
    {
        if (_isMaximized) return;

        var wa = GetCurrentMonitorWorkArea();
        var targetW = Math.Min(Width, wa.Width - 24);
        var targetH = Math.Min(Height, wa.Height - 12);
        targetW = Math.Max(MinWidth, targetW);
        targetH = Math.Max(MinHeight, targetH);

        if (Math.Abs(targetW - Width) < 0.5 && Math.Abs(targetH - Height) < 0.5)
            return;

        Width = targetW;
        Height = targetH;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WindowStyle=None 會失去原生可調整大小的邊緣，這裡以 WM_NCHITTEST 補回。
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST || _isMaximized)
            return IntPtr.Zero;

        var raw = lParam.ToInt32();
        var screenPoint = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
        var p = PointFromScreen(screenPoint);

        const double grip = 6;
        var left = p.X <= grip;
        var right = p.X >= ActualWidth - grip;
        var top = p.Y <= grip;
        var bottom = p.Y >= ActualHeight - grip;

        int result;
        if (top && left) result = HTTOPLEFT;
        else if (top && right) result = HTTOPRIGHT;
        else if (bottom && left) result = HTBOTTOMLEFT;
        else if (bottom && right) result = HTBOTTOMRIGHT;
        else if (left) result = HTLEFT;
        else if (right) result = HTRIGHT;
        else if (top) result = HTTOP;
        else if (bottom) result = HTBOTTOM;
        else return IntPtr.Zero;

        handled = true;
        return new IntPtr(result);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        // 與原生行為一致：最大化狀態下拖動標題列時，先還原視窗尺寸再跟隨滑鼠拖曳，
        // 否則視窗位置被拖走但尺寸仍是全螢幕，_isMaximized 狀態與實際脫節會卡死。
        if (_isMaximized)
        {
            var pos = e.GetPosition(this);  // DIP，相對視窗
            var ratioX = ActualWidth > 0 ? pos.X / ActualWidth : 0.5;
            var restoreW = !_restoreBounds.IsEmpty ? _restoreBounds.Width : 1180;
            var restoreH = !_restoreBounds.IsEmpty ? _restoreBounds.Height : 780;

            // PointToScreen 回傳裝置像素，換回 DIP
            double dpiX = 1, dpiY = 1;
            var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
            if (ct != null)
            {
                dpiX = ct.TransformToDevice.M11;
                dpiY = ct.TransformToDevice.M22;
                if (dpiX <= 0) dpiX = 1;
                if (dpiY <= 0) dpiY = 1;
            }
            var mouseDev = PointToScreen(pos);
            var mouseDip = new Point(mouseDev.X / dpiX, mouseDev.Y / dpiY);

            _isMaximized = false;
            Width = restoreW;
            Height = restoreH;
            Left = mouseDip.X - restoreW * ratioX;
            Top = mouseDip.Y - pos.Y;
            UpdateRootClip();
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 拖曳期間失去滑鼠捕獲時忽略
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    /// <summary>在視窗目前所在顯示器的工作區內「放大」，保留圓角與無邊框外觀。</summary>
    private void ToggleMaximize()
    {
        if (_isMaximized)
        {
            _isMaximized = false;
            if (!_restoreBounds.IsEmpty)
            {
                Left = _restoreBounds.Left;
                Top = _restoreBounds.Top;
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
            }
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = GetCurrentMonitorWorkArea();
            _isMaximized = true;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }

        UpdateRootClip();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
    {
        if (_themeService.CurrentTheme == AppTheme.Dark)
        {
            ThemeIcon.Data = Geometry.Parse(MoonGeometry);
            ThemeText.SetResourceReference(TextBlock.TextProperty, "Nav_DarkMode");
        }
        else
        {
            ThemeIcon.Data = Geometry.Parse(SunGeometry);
            ThemeText.SetResourceReference(TextBlock.TextProperty, "Nav_LightMode");
        }
    }

    public void NavigateToDashboard()
    {
        try
        {
            if (_dashboardPage == null)
            {
                _dashboardPage = new DashboardPage(_orchestrator);
            }
            MainFrame.Navigate(_dashboardPage);
            PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Dashboard");
            SetActiveNav(NavDashboard);
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入儀表板失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_DashboardLoadFailed") as string ?? "載入儀表板失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    public void RefreshDashboard()
    {
        _dashboardPage?.UpdateDisplaySafe();
    }

    private void NavigateToCalendar()
    {
        try
        {
            if (_calendarPage == null)
            {
                _calendarPage = new CalendarPage(_calendarService);
            }
            _calendarPage.RefreshEvents();
            MainFrame.Navigate(_calendarPage);
            PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Calendar");
            SetActiveNav(NavCalendar);
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入月曆失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_CalendarLoadFailed") as string ?? "載入月曆失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    public void NavigateToSettings()
    {
        try
        {
            if (_settingsPage == null)
            {
                var settingsService = App.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                var cacheService = App.Services.GetService(typeof(CacheService)) as CacheService;
                if (settingsService != null && cacheService != null)
                {
                    var languageService = App.Services.GetService(typeof(LanguageService)) as LanguageService;
                    _settingsPage = new SettingsPage(settingsService, cacheService, _themeService, languageService);
                }
            }
            if (_settingsPage != null)
            {
                MainFrame.Navigate(_settingsPage);
                PageTitle.SetResourceReference(TextBlock.TextProperty, "Nav_Settings");
                SetActiveNav(NavSettings);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("MainWindow: 載入設定頁面失敗", ex);
            var dialog = new Windows.WarningDialog(
                TryFindResource("Window_LoadFailed") as string ?? "載入失敗",
                string.Format(TryFindResource("Window_SettingsLoadFailed") as string ?? "載入設定頁面失敗：{0}", ex.Message),
                icon: "❌");
            Windows.WarningDialog.ShowSingleton(dialog);
        }
    }

    private void SetActiveNav(Button activeButton)
    {
        var navButtons = new[] { NavDashboard, NavCalendar, NavSettings };
        foreach (var btn in navButtons)
        {
            btn.Style = (Style)FindResource("NavItemStyle");
        }
        activeButton.Style = (Style)FindResource("NavItemActiveStyle");
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateToDashboard();
    }

    private void NavCalendar_Click(object sender, RoutedEventArgs e)
    {
        NavigateToCalendar();
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _themeService.ToggleTheme();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
