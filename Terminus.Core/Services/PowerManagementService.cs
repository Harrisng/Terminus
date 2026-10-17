using System.Runtime.InteropServices;

namespace Terminus.Core.Services;

/// <summary>
/// 電源管理服務：控制系統睡眠行為。
/// AI 通宵模式期間呼叫 PreventSleep 防止電腦休眠，結束後呼叫 AllowSleep 還原。
/// </summary>
public class PowerManagementService
{
    // ── SetThreadExecutionState flags ──
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    // ── PowerCreateRequest / PowerSetRequest / PowerClearRequest ──
    private const uint PowerRequestSystemRequired = 0;

    private IntPtr _powerRequest = IntPtr.Zero;
    private bool _sleepPrevented;

    /// <summary>目前是否正處於防睡眠狀態。</summary>
    public bool IsSleepPrevented => _sleepPrevented;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref POWER_REQUEST_CONTEXT Context);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool PowerSetRequest(IntPtr PowerRequestHandle, uint RequestType);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool PowerClearRequest(IntPtr PowerRequestHandle, uint RequestType);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct POWER_REQUEST_CONTEXT
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    /// <summary>
    /// 防止系統進入睡眠。同時使用 SetThreadExecutionState 與 PowerSetRequest 雙重保險。
    /// 重複呼叫安全。
    /// </summary>
    public void PreventSleep()
    {
        if (_sleepPrevented)
            return;

        try
        {
            // 第一層：SetThreadExecutionState（簡單可靠）
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

            // 第二層：PowerSetRequest（Modern Standby 系統需要）
            if (_powerRequest == IntPtr.Zero)
            {
                var context = new POWER_REQUEST_CONTEXT
                {
                    Version = 0,
                    Flags = 0,
                    SimpleReasonString = "Terminus AI 通宵模式進行中，防止系統睡眠"
                };
                _powerRequest = PowerCreateRequest(ref context);
            }

            if (_powerRequest != IntPtr.Zero)
            {
                PowerSetRequest(_powerRequest, PowerRequestSystemRequired);
            }

            _sleepPrevented = true;
            LoggerService.Info("PowerManagementService: 已啟用防睡眠");
        }
        catch (Exception ex)
        {
            LoggerService.Error("PowerManagementService: 啟用防睡眠失敗", ex);
        }
    }

    /// <summary>
    /// 還原睡眠行為，允許系統正常休眠。重複呼叫安全。
    /// </summary>
    public void AllowSleep()
    {
        if (!_sleepPrevented && _powerRequest == IntPtr.Zero)
            return;

        try
        {
            // 還原 SetThreadExecutionState
            SetThreadExecutionState(ES_CONTINUOUS);

            // 清除 PowerRequest
            if (_powerRequest != IntPtr.Zero)
            {
                PowerClearRequest(_powerRequest, PowerRequestSystemRequired);
                CloseHandle(_powerRequest);
                _powerRequest = IntPtr.Zero;
            }

            _sleepPrevented = false;
            LoggerService.Info("PowerManagementService: 已還原睡眠設定");
        }
        catch (Exception ex)
        {
            LoggerService.Error("PowerManagementService: 還原睡眠設定失敗", ex);
        }
    }
}
