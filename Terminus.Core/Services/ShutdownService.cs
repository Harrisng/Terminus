using System.Diagnostics;
using NodaTime;

namespace Terminus.Core.Services;

/// <summary>
/// Unified shutdown interface with retry blocking and cancel detection (T7).
/// All shutdown attempts go through this single entry point.
/// IMPORTANT: Has built-in safety guards to prevent accidental shutdown during daytime.
/// </summary>
public class ShutdownService
{
    private readonly object _lock = new();
    private bool _isShutdownInProgress = false;
    private bool _shutdownWasCancelled = false;
    private DateTime? _lastShutdownAttempt = null;
    private int _retryCount = 0;
    private const int MaxRetries = 3;
    private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);
    
    // Safety: don't allow shutdown within this time after app start
    private readonly Instant _startupTime;
    private static readonly Duration StartupGracePeriod = Duration.FromMinutes(2);
    
    // Safety: only allow shutdown during these hours (21:30 ~ 05:00 next day)
    private const int SafeShutdownStartHour = 21;
    private const int SafeShutdownStartMinute = 30;
    private const int SafeShutdownEndHour = 5;
    private const int SafeShutdownEndMinute = 0;

    public event EventHandler<ShutdownEventArgs>? ShutdownInitiated;
    public event EventHandler<ShutdownEventArgs>? ShutdownCancelled;
    public event EventHandler<ShutdownEventArgs>? ShutdownCompleted;
    public event EventHandler<ShutdownEventArgs>? ShutdownFailed;
    public event EventHandler<ShutdownEventArgs>? ShutdownBlocked;

    public ShutdownService(IClock? clock = null)
    {
        _startupTime = (clock ?? SystemClock.Instance).GetCurrentInstant();
    }

    /// <summary>
    /// Initiates system shutdown with retry logic.
    /// Has safety guards to prevent accidental shutdown during daytime.
    /// </summary>
    /// <param name="reason">Reason for shutdown (logged)</param>
    /// <param name="userInitiated">If true, bypass startup grace period (user explicitly requested)</param>
    public virtual async Task<ShutdownResult> InitiateShutdownAsync(string reason, CancellationToken cancellationToken = default, bool userInitiated = false)
    {
        // Safety check 1: Startup grace period (skipped for user-initiated shutdowns)
        var now = SystemClock.Instance.GetCurrentInstant();
        var timeSinceStartup = now - _startupTime;
        if (!userInitiated && timeSinceStartup < StartupGracePeriod)
        {
            var remaining = StartupGracePeriod - timeSinceStartup;
            LoggerService.Info($"ShutdownService: 啟動寬限期內阻止關機, 剩餘 {remaining.TotalSeconds:F0}s, reason={reason}");
            ShutdownBlocked?.Invoke(this, new ShutdownEventArgs
            {
                Reason = reason,
                Message = $"Blocked: startup grace period active, {remaining.TotalSeconds:F0}s remaining"
            });

            return new ShutdownResult
            {
                Success = false,
                Message = $"Shutdown blocked: startup grace period ({remaining.TotalSeconds:F0}s remaining)",
                WasCancelled = false
            };
        }

        // Safety check 2: Time of day restriction
        var hkTime = now.InZone(DateTimeZoneProviders.Tzdb["Asia/Hong_Kong"]);
        if (!IsWithinSafeShutdownWindow(hkTime.TimeOfDay))
        {
            LoggerService.Info($"ShutdownService: 非安全時段阻止關機, 目前={hkTime.TimeOfDay:HH:mm}, reason={reason}");
            ShutdownBlocked?.Invoke(this, new ShutdownEventArgs
            {
                Reason = reason,
                Message = $"Blocked: outside safe shutdown window (current: {hkTime.TimeOfDay:HH:mm})"
            });

            return new ShutdownResult
            {
                Success = false,
                Message = $"Shutdown blocked: outside safe hours ({hkTime.TimeOfDay:HH:mm}, safe: 21:30-05:00)",
                WasCancelled = false
            };
        }

        lock (_lock)
        {
            if (_isShutdownInProgress)
            {
                LoggerService.Info($"ShutdownService: 已有關機進行中, 忽略重複要求, reason={reason}");
                return new ShutdownResult
                {
                    Success = false,
                    Message = "Shutdown already in progress",
                    WasCancelled = false
                };
            }

            _isShutdownInProgress = true;
            _shutdownWasCancelled = false;
            _lastShutdownAttempt = DateTime.UtcNow;
        }

        LoggerService.Info($"ShutdownService: 開始關機流程, reason={reason}, userInitiated={userInitiated}");
        ShutdownInitiated?.Invoke(this, new ShutdownEventArgs { Reason = reason, Attempt = _retryCount + 1 });

        try
        {
            // Execute shutdown command
            var result = await ExecuteShutdownCommandAsync(cancellationToken);

            if (result.Success)
            {
                LoggerService.Info($"ShutdownService: 關機指令已送出, 系統將在 30 秒後關機");
                ShutdownCompleted?.Invoke(this, new ShutdownEventArgs { Reason = reason });
                return result;
            }

            LoggerService.Warn($"ShutdownService: 關機指令失敗: {result.Message}");

            // Failed - retry logic (only for automated, not user-initiated)
            if (userInitiated)
            {
                lock (_lock)
                {
                    _isShutdownInProgress = false;
                }
                return result;
            }

            _retryCount++;
            if (_retryCount < MaxRetries)
            {
                ShutdownFailed?.Invoke(this, new ShutdownEventArgs
                {
                    Reason = reason,
                    Attempt = _retryCount,
                    Message = $"Shutdown failed, will retry in {_retryInterval.TotalMinutes} minutes"
                });

                await Task.Delay(_retryInterval, cancellationToken);

                lock (_lock)
                {
                    _isShutdownInProgress = false;
                }

                return await InitiateShutdownAsync($"{reason} (retry {_retryCount})", cancellationToken);
            }

            // Max retries exceeded - force shutdown
            LoggerService.Warn($"ShutdownService: 重試次數已達上限, 執行強制關機");
            ShutdownFailed?.Invoke(this, new ShutdownEventArgs
            {
                Reason = reason,
                Attempt = _retryCount,
                Message = "Max retries exceeded, forcing shutdown"
            });

            var forceResult = await ForceShutdownAsync(cancellationToken);

            lock (_lock)
            {
                _isShutdownInProgress = false;
                _retryCount = 0;
            }

            return forceResult;
        }
        catch (Exception ex)
        {
            LoggerService.Error($"ShutdownService: 關機流程例外: {ex.Message}", ex);
            lock (_lock)
            {
                _isShutdownInProgress = false;
            }

            ShutdownFailed?.Invoke(this, new ShutdownEventArgs
            {
                Reason = reason,
                Message = ex.Message
            });

            return new ShutdownResult
            {
                Success = false,
                Message = $"Exception during shutdown: {ex.Message}",
                WasCancelled = false
            };
        }
    }

    /// <summary>
    /// Executes the shutdown command.
    /// </summary>
    private async Task<ShutdownResult> ExecuteShutdownCommandAsync(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /t 30 /c \"Terminus: Sleep cycle shutdown\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new ShutdownResult
                {
                    Success = false,
                    Message = "Failed to start shutdown process",
                    WasCancelled = false
                };
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                return new ShutdownResult
                {
                    Success = true,
                    Message = "Shutdown initiated successfully",
                    WasCancelled = false
                };
            }

            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            return new ShutdownResult
            {
                Success = false,
                Message = $"Shutdown command failed: {error}",
                WasCancelled = false
            };
        }
        catch (Exception ex)
        {
            return new ShutdownResult
            {
                Success = false,
                Message = ex.Message,
                WasCancelled = false
            };
        }
    }

    /// <summary>
    /// Forces immediate shutdown without timeout.
    /// </summary>
    private async Task<ShutdownResult> ForceShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /f /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            return new ShutdownResult
            {
                Success = true,
                Message = "Force shutdown executed",
                WasCancelled = false
            };
        }
        catch (Exception ex)
        {
            return new ShutdownResult
            {
                Success = false,
                Message = ex.Message,
                WasCancelled = false
            };
        }
    }

    /// <summary>
    /// Detects if shutdown was cancelled by checking if shutdown command is no longer pending.
    /// </summary>
    private async Task<bool> DetectShutdownCancelAsync(CancellationToken cancellationToken)
    {
        // Wait a bit to let shutdown take effect
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        // Check if shutdown is still pending
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/a",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(cancellationToken);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            // If /a succeeds, there was a pending shutdown (now cancelled)
            // If /a fails, no shutdown was pending (user already cancelled)
            return process.ExitCode != 0; // Non-zero = no shutdown to abort = was cancelled
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Aborts a pending shutdown.
    /// </summary>
    public async Task<bool> AbortShutdownAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/a",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();

                lock (_lock)
                {
                    _isShutdownInProgress = false;
                    _shutdownWasCancelled = true;
                }

                return process.ExitCode == 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool IsShutdownInProgress
    {
        get { lock (_lock) { return _isShutdownInProgress; } }
    }

    public bool WasLastShutdownCancelled
    {
        get { lock (_lock) { return _shutdownWasCancelled; } }
    }

    /// <summary>
    /// Checks if current time is within the safe shutdown window (21:30 ~ 05:00 next day).
    /// This is a safety guard to prevent accidental shutdown during daytime.
    /// </summary>
    private bool IsWithinSafeShutdownWindow(LocalTime current)
    {
        var startMinutes = SafeShutdownStartHour * 60 + SafeShutdownStartMinute;
        var endMinutes = SafeShutdownEndHour * 60 + SafeShutdownEndMinute;
        var currentMinutes = current.Hour * 60 + current.Minute;

        // Window crosses midnight: 21:30 ~ 05:00
        // Valid if: current >= 21:30 OR current < 05:00
        return currentMinutes >= startMinutes || currentMinutes < endMinutes;
    }
}

public class ShutdownResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required bool WasCancelled { get; init; }
}

public class ShutdownEventArgs : EventArgs
{
    public required string Reason { get; init; }
    public int Attempt { get; init; } = 1;
    public string? Message { get; init; }
}
