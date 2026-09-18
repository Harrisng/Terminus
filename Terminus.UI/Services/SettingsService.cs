using Microsoft.Win32;

namespace Terminus.UI.Services;

/// <summary>
/// Settings service for storing and retrieving user preferences.
/// </summary>
public class SettingsService
{
    private readonly string _registryPath = @"SOFTWARE\Terminus";

    public string GetCalendarUrl()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            return key?.GetValue("CalendarUrl") as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetCalendarUrl(string url)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("CalendarUrl", url);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save calendar URL: {ex.Message}", ex);
        }
    }

    public TimeSpan GetSleepTime()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var minutes = key?.GetValue("SleepTimeMinutes") as int? ?? 360; // Default 6 hours
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromHours(6);
        }
    }

    public void SetSleepTime(TimeSpan time)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("SleepTimeMinutes", (int)time.TotalMinutes);
        }
        catch { }
    }

    public TimeSpan GetPreSleepBuffer()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var minutes = key?.GetValue("PreSleepMinutes") as int? ?? 15;
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(15);
        }
    }

    public void SetPreSleepBuffer(TimeSpan time)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("PreSleepMinutes", (int)time.TotalMinutes);
        }
        catch { }
    }

    public TimeSpan GetWashTime()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var minutes = key?.GetValue("WashMinutes") as int? ?? 30;
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(30);
        }
    }

    public void SetWashTime(TimeSpan time)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("WashMinutes", (int)time.TotalMinutes);
        }
        catch { }
    }

    public TimeSpan GetBreakfastTime()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var minutes = key?.GetValue("BreakfastMinutes") as int? ?? 45;
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(45);
        }
    }

    public void SetBreakfastTime(TimeSpan time)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("BreakfastMinutes", (int)time.TotalMinutes);
        }
        catch { }
    }

    public TimeSpan GetCommuteTime()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            var minutes = key?.GetValue("CommuteMinutes") as int? ?? 105; // 1h45m
            return TimeSpan.FromMinutes(minutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(105);
        }
    }

    public void SetCommuteTime(TimeSpan time)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue("CommuteMinutes", (int)time.TotalMinutes);
        }
        catch { }
    }

    public bool GetStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("Terminus") != null;
        }
        catch
        {
            return false;
        }
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
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
        }
        catch { }
    }
}
