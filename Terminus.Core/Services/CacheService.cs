using System.Text.Json;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using Terminus.Core.Models;

namespace Terminus.Core.Services;

/// <summary>
/// Service for caching calendar data with metadata and corruption handling.
/// Stores cache files in AppData\Local\Terminus\cache\
/// </summary>
public class CacheService
{
    private readonly string _cacheDirectory;
    private static readonly JsonSerializerOptions JsonOptions;

    static CacheService()
    {
        JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        JsonOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    }

    public CacheService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "Terminus", "cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Saves data to cache with metadata.
    /// </summary>
    public async Task SaveAsync<T>(string key, List<T> data, CacheMetadata metadata)
    {
        var cacheData = new CachedCalendarData<T>
        {
            Data = data,
            Metadata = metadata
        };

        var filePath = GetCacheFilePath(key);
        var json = JsonSerializer.Serialize(cacheData, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Loads data from cache.
    /// Returns the data, metadata, and cache status.
    /// Handles corruption by renaming corrupted files for diagnosis.
    /// </summary>
    public async Task<(List<T>? Data, CacheMetadata? Metadata, CacheStatus Status)> LoadAsync<T>(string key)
    {
        var filePath = GetCacheFilePath(key);

        if (!File.Exists(filePath))
        {
            return (null, null, CacheStatus.Default);
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var cacheData = JsonSerializer.Deserialize<CachedCalendarData<T>>(json, JsonOptions);

            if (cacheData == null)
            {
                HandleCorruptCache(key);
                return (null, null, CacheStatus.Corrupted);
            }

            return (cacheData.Data, cacheData.Metadata, CacheStatus.Cached);
        }
        catch (JsonException)
        {
            // Cache file is corrupted
            HandleCorruptCache(key);
            return (null, null, CacheStatus.Corrupted);
        }
        catch (Exception)
        {
            // Other errors (permissions, etc.)
            HandleCorruptCache(key);
            return (null, null, CacheStatus.Corrupted);
        }
    }

    /// <summary>
    /// Handles corrupted cache by renaming the file for diagnosis.
    /// Does NOT delete the file - preserves it for troubleshooting.
    /// </summary>
    public void HandleCorruptCache(string key)
    {
        var filePath = GetCacheFilePath(key);
        if (!File.Exists(filePath))
            return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var corruptedPath = Path.Combine(
            _cacheDirectory,
            $"{key}.corrupted_{timestamp}.json"
        );

        try
        {
            File.Move(filePath, corruptedPath);
        }
        catch
        {
            // If rename fails, at least try to delete to prevent repeated corruption errors
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Give up - leave it as is
            }
        }
    }

    /// <summary>
    /// Gets the age of cached data in hours.
    /// Returns null if cache doesn't exist or can't be read.
    /// </summary>
    public async Task<double?> GetCacheAgeHoursAsync(string key)
    {
        var (_, metadata, status) = await LoadAsync<object>(key);

        if (status != CacheStatus.Cached || metadata == null)
            return null;

        return (DateTime.UtcNow - metadata.FetchedAt.ToUniversalTime()).TotalHours;
    }

    /// <summary>
    /// Clears all cache files (used for testing or manual reset).
    /// </summary>
    public void ClearAll()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore deletion errors
                }
            }
        }
    }

    private string GetCacheFilePath(string key)
    {
        // Sanitize key to be filesystem-safe
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, $"{safeKey}.json");
    }
}
