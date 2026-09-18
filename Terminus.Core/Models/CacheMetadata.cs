namespace Terminus.Core.Models;

/// <summary>
/// Cache status for calendar data.
/// </summary>
public enum CacheStatus
{
    /// <summary>
    /// Data is fresh from the network
    /// </summary>
    Fresh,

    /// <summary>
    /// Using cached data due to network failure
    /// </summary>
    Cached,

    /// <summary>
    /// No cache available, using default (lenient/conservative mode)
    /// </summary>
    Default,

    /// <summary>
    /// Cache file was corrupted and has been renamed
    /// </summary>
    Corrupted
}

/// <summary>
/// Metadata for cached calendar data.
/// </summary>
public class CacheMetadata
{
    /// <summary>
    /// When this data was fetched
    /// </summary>
    public required DateTime FetchedAt { get; init; }

    /// <summary>
    /// Source URL that was fetched
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Whether the fetch was successful
    /// </summary>
    public bool FetchSuccess { get; init; }

    /// <summary>
    /// Error message if fetch failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of events cached
    /// </summary>
    public int EventCount { get; init; }
}

/// <summary>
/// Cached calendar data with metadata.
/// </summary>
public class CachedCalendarData<T>
{
    /// <summary>
    /// The cached data
    /// </summary>
    public required List<T> Data { get; init; }

    /// <summary>
    /// Cache metadata
    /// </summary>
    public required CacheMetadata Metadata { get; init; }
}
