using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// Orchestrates calendar data fetching with cache fallback and offline handling.
/// Implements T3 requirements: cache with timestamp, offline fallback, corruption handling.
/// </summary>
public class CalendarDataService
{
    private readonly ICalService _iCalService;
    private readonly CacheService _cacheService;
    private readonly TimeSpan _autoRefreshInterval = TimeSpan.FromHours(6);

    private DateTime _lastScheduleFetch = DateTime.MinValue;
    private DateTime _lastHolidayFetch = DateTime.MinValue;
    private int _consecutiveScheduleFailures = 0;
    private int _consecutiveHolidayFailures = 0;

    private const string ScheduleCacheKey = "schedule";
    private const string HolidayCacheKey = "holidays";
    private const int WarningThresholdFailures = 3;

    public CalendarDataService(ICalService iCalService, CacheService cacheService)
    {
        _iCalService = iCalService;
        _cacheService = cacheService;
    }

    /// <summary>
    /// Gets schedule data with automatic caching and fallback.
    /// Returns (events, cache status, age in hours if cached).
    /// </summary>
    public async Task<(List<CalendarEvent> Events, CacheStatus Status, double? CacheAgeHours)> GetScheduleAsync(
        string calendarUrl,
        bool forceRefresh = false)
    {
        var shouldRefresh = forceRefresh ||
                           (DateTime.UtcNow - _lastScheduleFetch) >= _autoRefreshInterval;

        LoggerService.Info($"CalendarDataService: GetScheduleAsync, shouldRefresh={shouldRefresh}, URL={calendarUrl?.Substring(0, Math.Min(50, calendarUrl?.Length ?? 0)) ?? "null"}...");

        if (shouldRefresh)
        {
            try
            {
                // Try to fetch fresh data
                var events = await _iCalService.FetchCalendarAsync(calendarUrl);

                LoggerService.Info($"CalendarDataService: 抓取成功, 事件數={events.Count}");

                // Save to cache
                var metadata = new CacheMetadata
                {
                    FetchedAt = DateTime.UtcNow,
                    SourceUrl = calendarUrl,
                    FetchSuccess = true,
                    EventCount = events.Count
                };

                await _cacheService.SaveAsync(ScheduleCacheKey, events, metadata);

                _lastScheduleFetch = DateTime.UtcNow;
                _consecutiveScheduleFailures = 0;

                return (events, CacheStatus.Fresh, null);
            }
            catch (Exception ex)
            {
                _consecutiveScheduleFailures++;
                LoggerService.Error("CalendarDataService: 抓取失敗", ex);
                // Fetch failed - try to use cache
                return await GetScheduleFromCacheOrDefault(calendarUrl, ex.Message);
            }
        }
        else
        {
            // Not time to refresh yet - use cache if available
            var (cachedEvents, metadata, status) = await _cacheService.LoadAsync<CalendarEvent>(ScheduleCacheKey);

            LoggerService.Info($"CalendarDataService: 使用快取, 狀態={status}, 事件數={cachedEvents?.Count ?? 0}");

            if (status == CacheStatus.Cached && cachedEvents != null)
            {
                var ageHours = await _cacheService.GetCacheAgeHoursAsync(ScheduleCacheKey);
                return (cachedEvents, CacheStatus.Cached, ageHours);
            }

            // No cache - return empty default
            LoggerService.Warn("CalendarDataService: 無快取可用, 返回空列表");
            return (new List<CalendarEvent>(), CacheStatus.Default, null);
        }
    }

    /// <summary>
    /// Gets holiday data with automatic caching and fallback.
    /// </summary>
    public async Task<(List<PublicHoliday> Holidays, CacheStatus Status, double? CacheAgeHours)> GetHolidaysAsync(
        string language = "tc",
        bool forceRefresh = false)
    {
        var shouldRefresh = forceRefresh ||
                           (DateTime.UtcNow - _lastHolidayFetch) >= _autoRefreshInterval;

        if (shouldRefresh)
        {
            try
            {
                var holidays = await _iCalService.FetchHolidaysAsync(language);

                var metadata = new CacheMetadata
                {
                    FetchedAt = DateTime.UtcNow,
                    SourceUrl = $"https://www.1823.gov.hk/common/ical/{language}.ics",
                    FetchSuccess = true,
                    EventCount = holidays.Count
                };

                await _cacheService.SaveAsync(HolidayCacheKey, holidays, metadata);

                _lastHolidayFetch = DateTime.UtcNow;
                _consecutiveHolidayFailures = 0;

                return (holidays, CacheStatus.Fresh, null);
            }
            catch (Exception)
            {
                _consecutiveHolidayFailures++;

                // Try cache
                var (cachedHolidays, _, status) = await _cacheService.LoadAsync<PublicHoliday>(HolidayCacheKey);

                if (status == CacheStatus.Cached && cachedHolidays != null)
                {
                    var ageHours = await _cacheService.GetCacheAgeHoursAsync(HolidayCacheKey);
                    return (cachedHolidays, CacheStatus.Cached, ageHours);
                }

                return (new List<PublicHoliday>(), CacheStatus.Default, null);
            }
        }
        else
        {
            var (cachedHolidays, _, status) = await _cacheService.LoadAsync<PublicHoliday>(HolidayCacheKey);

            if (status == CacheStatus.Cached && cachedHolidays != null)
            {
                var ageHours = await _cacheService.GetCacheAgeHoursAsync(HolidayCacheKey);
                return (cachedHolidays, CacheStatus.Cached, ageHours);
            }

            return (new List<PublicHoliday>(), CacheStatus.Default, null);
        }
    }

    /// <summary>
    /// Checks if there are consecutive failures that should trigger a warning.
    /// </summary>
    public bool ShouldShowFetchWarning()
    {
        return _consecutiveScheduleFailures >= WarningThresholdFailures ||
               _consecutiveHolidayFailures >= WarningThresholdFailures;
    }

    /// <summary>
    /// Gets consecutive failure counts for diagnostics.
    /// </summary>
    public (int ScheduleFailures, int HolidayFailures) GetConsecutiveFailures()
    {
        return (_consecutiveScheduleFailures, _consecutiveHolidayFailures);
    }

    private async Task<(List<CalendarEvent> Events, CacheStatus Status, double? CacheAgeHours)> GetScheduleFromCacheOrDefault(
        string calendarUrl,
        string errorMessage)
    {
        var (cachedEvents, metadata, status) = await _cacheService.LoadAsync<CalendarEvent>(ScheduleCacheKey);

        if (status == CacheStatus.Cached && cachedEvents != null)
        {
            // Have cache - use it and show age
            var ageHours = await _cacheService.GetCacheAgeHoursAsync(ScheduleCacheKey);
            return (cachedEvents, CacheStatus.Cached, ageHours);
        }

        if (status == CacheStatus.Corrupted)
        {
            // Cache was corrupted and has been renamed
            return (new List<CalendarEvent>(), CacheStatus.Corrupted, null);
        }

        // No cache available - return empty list (will be treated as non-early-class day)
        return (new List<CalendarEvent>(), CacheStatus.Default, null);
    }
}
