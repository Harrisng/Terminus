using Microsoft.Win32;
using Terminus.Core.Services;

namespace Terminus.UI.Services;

/// <summary>
/// Settings service for storing and retrieving user preferences.
/// All values stored in HKEY_CURRENT_USER\SOFTWARE\Terminus.
/// </summary>
public class SettingsService
{
    private const string RegistryPath = @"SOFTWARE\Terminus";

    // ── 通用 helper：抽出 12 個幾乎完全相同的 OpenSubKey/GetValue / CreateSubKey/SetValue 樣板 ──

    /// <summary>讀取字串值。找不到或失敗時回傳 <paramref name="defaultValue"/>。</summary>
    private string ReadString(string name, string defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(name) as string ?? defaultValue;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsService: 讀取 {name} 失敗: {ex.Message}");
            return defaultValue;
        }
    }

    /// <summary>讀取整數值。找不到或失敗時回傳 <paramref name="defaultValue"/>。</summary>
    private int ReadInt(string name, int defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(name) as int? ?? defaultValue;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsService: 讀取 {name} 失敗: {ex.Message}");
            return defaultValue;
        }
    }

    /// <summary>寫入整數值（含夾取/下限檢查）。</summary>
    private void WriteInt(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(name, value);
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsService: 寫入 {name}={value} 失敗: {ex.Message}");
        }
    }

    // ── 用戶設定 ──

    public string GetCalendarUrl() => ReadString("CalendarUrl", string.Empty);

    public void SetCalendarUrl(string url)
    {
        // SetCalendarUrl 歷史上會擲回，維持此行為讓 UI 層能攔截並提示用戶
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue("CalendarUrl", url);
        }
        catch (Exception ex)
        {
            LoggerService.Error($"SettingsService: 寫入 CalendarUrl 失敗: {ex.Message}");
            throw new InvalidOperationException($"Failed to save calendar URL: {ex.Message}", ex);
        }
    }

    public TimeSpan GetSleepTime() => TimeSpan.FromMinutes(ReadInt("SleepTimeMinutes", 360));

    public void SetSleepTime(TimeSpan time) => WriteInt("SleepTimeMinutes", (int)time.TotalMinutes);

    public TimeSpan GetPreSleepBuffer() => TimeSpan.FromMinutes(ReadInt("PreSleepMinutes", 15));

    public void SetPreSleepBuffer(TimeSpan time) => WriteInt("PreSleepMinutes", (int)time.TotalMinutes);

    public TimeSpan GetWashTime() => TimeSpan.FromMinutes(ReadInt("WashMinutes", 30));

    public void SetWashTime(TimeSpan time) => WriteInt("WashMinutes", (int)time.TotalMinutes);

    public TimeSpan GetBreakfastTime() => TimeSpan.FromMinutes(ReadInt("BreakfastMinutes", 45));

    public void SetBreakfastTime(TimeSpan time) => WriteInt("BreakfastMinutes", (int)time.TotalMinutes);

    public TimeSpan GetCommuteTime() => TimeSpan.FromMinutes(ReadInt("CommuteMinutes", 105));

    public void SetCommuteTime(TimeSpan time) => WriteInt("CommuteMinutes", (int)time.TotalMinutes);

    /// <summary>
    /// Hour (0-23) that separates early-class vs non-early-class days.
    /// Default is 12 (noon). Any class starting before this hour is "early".
    /// </summary>
    public int GetEarlyClassCutoffHour() => ReadInt("EarlyClassCutoffHour", 12);

    public void SetEarlyClassCutoffHour(int hour) => WriteInt("EarlyClassCutoffHour", Math.Clamp(hour, 0, 23));

    /// <summary>
    /// Delay quota in minutes for early-class days.
    /// Default is 90 minutes.
    /// </summary>
    public int GetEarlyClassDelayQuotaMinutes() => ReadInt("EarlyClassDelayQuotaMinutes", 90);

    public void SetEarlyClassDelayQuotaMinutes(int minutes) => WriteInt("EarlyClassDelayQuotaMinutes", Math.Max(0, minutes));

    /// <summary>警告無回應寬限期（分鐘）。超過後自動延遲一次或直接關機。</summary>
    public int GetWarningGraceMinutes() => ReadInt("WarningGraceMinutes", 5);

    public void SetWarningGraceMinutes(int minutes) => WriteInt("WarningGraceMinutes", Math.Clamp(minutes, 1, 30));

    /// <summary>硬關機強制提醒寬限期（分鐘）。到達硬關機時間後給用戶多久儲存資料再關機。</summary>
    public int GetForceShutdownGraceMinutes() => ReadInt("ForceShutdownGraceMinutes", 5);

    public void SetForceShutdownGraceMinutes(int minutes) => WriteInt("ForceShutdownGraceMinutes", Math.Clamp(minutes, 1, 30));

    public bool GetStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("Terminus") != null;
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsService: 讀取 StartWithWindows 失敗: {ex.Message}");
            return false;
        }
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null)
            {
                LoggerService.Warn("SettingsService: 無法開啟 Run 機碼");
                return;
            }

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                key.SetValue("Terminus", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("Terminus", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Warn($"SettingsService: 寫入 StartWithWindows={enabled} 失敗: {ex.Message}");
        }
    }
}
