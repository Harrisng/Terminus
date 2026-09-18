namespace Terminus.Core.Services;

/// <summary>
/// Notification service interface for Windows native notifications (T8).
/// Implementation will use Windows.UI.Notifications in the UI layer.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a pre-warning notification (30 minutes before main warning).
    /// </summary>
    Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo);

    /// <summary>
    /// Shows the main warning notification with action buttons.
    /// </summary>
    Task ShowWarningAsync(WarningNotificationArgs args);

    /// <summary>
    /// Shows a delay confirmation notification.
    /// </summary>
    Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited);

    /// <summary>
    /// Shows an AI mode started notification.
    /// </summary>
    Task ShowAIModeStartedAsync(string taskDescription);

    /// <summary>
    /// Shows an AI mode completed notification with webhook.
    /// </summary>
    Task ShowAIModeCompletedAsync(string taskDescription, bool success);

    /// <summary>
    /// Shows a shutdown imminent warning.
    /// </summary>
    Task ShowShutdownImminentAsync(TimeSpan timeRemaining);

    /// <summary>
    /// Shows a fetch error warning.
    /// </summary>
    Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours);

    /// <summary>
    /// Clears all active notifications.
    /// </summary>
    Task ClearAllNotificationsAsync();
}

public class WarningNotificationArgs
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required TimeSpan QuotaRemaining { get; init; }
    public required bool IsUnlimitedDelay { get; init; }
    public required bool ShowDelayButton { get; init; }
    public required bool ShowShutdownButton { get; init; }
    public required bool ShowAIModeButton { get; init; }
}

/// <summary>
/// Stub implementation for testing without Windows notifications.
/// </summary>
public class StubNotificationService : INotificationService
{
    public Task ShowPreWarningAsync(TimeSpan timeUntilWarning, string firstClassInfo)
    {
        Console.WriteLine($"[PRE-WARNING] Main warning in {timeUntilWarning.TotalMinutes:F0} minutes. {firstClassInfo}");
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(WarningNotificationArgs args)
    {
        Console.WriteLine($"[WARNING] {args.Title}: {args.Message}");
        Console.WriteLine($"  Quota: {args.QuotaRemaining.TotalMinutes:F0} min remaining (Unlimited: {args.IsUnlimitedDelay})");
        return Task.CompletedTask;
    }

    public Task ShowDelayConfirmationAsync(TimeSpan delayedUntil, TimeSpan quotaRemaining, bool isUnlimited)
    {
        Console.WriteLine($"[DELAY] Delayed for {delayedUntil.TotalMinutes:F0} minutes. Quota: {quotaRemaining.TotalMinutes:F0} min");
        return Task.CompletedTask;
    }

    public Task ShowAIModeStartedAsync(string taskDescription)
    {
        Console.WriteLine($"[AI MODE] Started: {taskDescription}");
        return Task.CompletedTask;
    }

    public Task ShowAIModeCompletedAsync(string taskDescription, bool success)
    {
        Console.WriteLine($"[AI MODE] Completed: {taskDescription} (Success: {success})");
        return Task.CompletedTask;
    }

    public Task ShowShutdownImminentAsync(TimeSpan timeRemaining)
    {
        Console.WriteLine($"[SHUTDOWN] Imminent in {timeRemaining.TotalSeconds:F0} seconds");
        return Task.CompletedTask;
    }

    public Task ShowFetchErrorAsync(int consecutiveFailures, double? cacheAgeHours)
    {
        Console.WriteLine($"[FETCH ERROR] {consecutiveFailures} consecutive failures. Cache age: {cacheAgeHours:F1} hours");
        return Task.CompletedTask;
    }

    public Task ClearAllNotificationsAsync()
    {
        Console.WriteLine("[CLEAR] All notifications cleared");
        return Task.CompletedTask;
    }
}
