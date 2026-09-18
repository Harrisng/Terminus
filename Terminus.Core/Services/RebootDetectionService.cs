using Microsoft.Win32;
using System.Management;

namespace Terminus.Core.Services;

/// <summary>
/// Reboot detection service (T5) - tracks reboots and disables cycles.
/// </summary>
public class RebootDetectionService
{
    private readonly string _registryPath = @"SOFTWARE\Terminus";
    private readonly string _lastShutdownCycleKey = "LastShutdownCycle";
    private readonly string _lastBootTimeKey = "LastBootTime";

    /// <summary>
    /// Records that a shutdown was initiated for a specific cycle.
    /// </summary>
    public void RecordShutdownForCycle(string cycleId)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath);
            key.SetValue(_lastShutdownCycleKey, cycleId);
            key.SetValue(_lastBootTimeKey, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            LoggerService.Error("RebootDetectionService: RecordShutdownForCycle 失敗", ex);
        }
    }

    /// <summary>
    /// Checks if the system rebooted after a shutdown was initiated.
    /// Returns the cycle ID that should be disabled, or null.
    /// </summary>
    public string? DetectRebootAfterShutdown()
    {
        try
        {
            var currentBootTime = GetSystemBootTime();

            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            if (key == null)
                return null;

            var lastShutdownCycle = key.GetValue(_lastShutdownCycleKey) as string;
            var lastBootTimeStr = key.GetValue(_lastBootTimeKey) as string;

            if (string.IsNullOrEmpty(lastShutdownCycle) || string.IsNullOrEmpty(lastBootTimeStr))
                return null;

            if (!DateTime.TryParse(lastBootTimeStr, out var lastBootTime))
                return null;

            // If current boot time is after last recorded boot time, a reboot occurred
            if (currentBootTime > lastBootTime)
            {
                return lastShutdownCycle;
            }

            return null;
        }
        catch (Exception ex)
        {
            LoggerService.Error("RebootDetectionService: DetectRebootAfterShutdown 失敗", ex);
            return null;
        }
    }

    /// <summary>
    /// Clears the shutdown record after handling reboot.
    /// </summary>
    public void ClearShutdownRecord()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath, writable: true);
            if (key != null)
            {
                key.DeleteValue(_lastShutdownCycleKey, throwOnMissingValue: false);
                key.DeleteValue(_lastBootTimeKey, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error("RebootDetectionService: ClearShutdownRecord 失敗", ex);
        }
    }

    /// <summary>
    /// Gets the system boot time using WMI.
    /// </summary>
    private DateTime GetSystemBootTime()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            using var results = searcher.Get();

            foreach (ManagementObject mo in results)
            {
                var lastBootStr = mo["LastBootUpTime"] as string;
                if (!string.IsNullOrEmpty(lastBootStr))
                {
                    return ManagementDateTimeConverter.ToDateTime(lastBootStr);
                }
            }
        }
        catch
        {
            // Fallback: use Environment.TickCount (less reliable)
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return DateTime.UtcNow - uptime;
        }

        return DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if this is the first run (no registry data).
    /// </summary>
    public bool IsFirstRun()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryPath);
            return key == null;
        }
        catch
        {
            return true;
        }
    }
}
