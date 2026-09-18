using FluentAssertions;
using Terminus.Core.Models;
using Terminus.Core.Services;
using Xunit;

namespace Terminus.Core.Tests.Services;

/// <summary>
/// Tests for CacheService (T3).
/// Covers cache functionality, corruption handling, and offline fallback.
/// </summary>
public class CacheServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesData()
    {
        var cacheService = new CacheService();
        var key = $"test_{Guid.NewGuid()}";

        var testData = new List<string> { "event1", "event2", "event3" };
        var metadata = new CacheMetadata
        {
            FetchedAt = DateTime.UtcNow,
            SourceUrl = "https://test.example.com",
            FetchSuccess = true,
            EventCount = 3
        };

        await cacheService.SaveAsync(key, testData, metadata);

        var (loadedData, loadedMetadata, status) = await cacheService.LoadAsync<string>(key);

        status.Should().Be(CacheStatus.Cached);
        loadedData.Should().BeEquivalentTo(testData);
        loadedMetadata.Should().NotBeNull();
        loadedMetadata!.SourceUrl.Should().Be(metadata.SourceUrl);
        loadedMetadata.EventCount.Should().Be(3);
    }

    [Fact]
    public async Task Load_NonExistentKey_ReturnsDefault()
    {
        var cacheService = new CacheService();
        var key = $"nonexistent_{Guid.NewGuid()}";

        var (data, metadata, status) = await cacheService.LoadAsync<string>(key);

        status.Should().Be(CacheStatus.Default);
        data.Should().BeNull();
        metadata.Should().BeNull();
    }

    [Fact]
    public async Task GetCacheAgeHours_ReturnsCorrectAge()
    {
        var cacheService = new CacheService();
        var key = $"age_test_{Guid.NewGuid()}";

        var metadata = new CacheMetadata
        {
            FetchedAt = DateTime.UtcNow.AddHours(-2),
            SourceUrl = "https://test.example.com",
            FetchSuccess = true,
            EventCount = 1
        };

        await cacheService.SaveAsync(key, new List<string> { "test" }, metadata);

        var ageHours = await cacheService.GetCacheAgeHoursAsync(key);

        ageHours.Should().NotBeNull();
        ageHours!.Value.Should().BeGreaterOrEqualTo(1.9).And.BeLessThan(2.2);
    }

    [Fact]
    public async Task GetCacheAgeHours_NonExistentCache_ReturnsNull()
    {
        var cacheService = new CacheService();
        var key = $"nonexistent_{Guid.NewGuid()}";

        var ageHours = await cacheService.GetCacheAgeHoursAsync(key);

        ageHours.Should().BeNull();
    }

    [Fact]
    public async Task HandleCorruptCache_RenamesFile()
    {
        var cacheService = new CacheService();
        var key = $"corrupt_test_{Guid.NewGuid()}";

        // Create a valid cache first
        var metadata = new CacheMetadata
        {
            FetchedAt = DateTime.UtcNow,
            SourceUrl = "https://test.example.com",
            FetchSuccess = true,
            EventCount = 1
        };

        await cacheService.SaveAsync(key, new List<string> { "test" }, metadata);

        // Manually corrupt the file by writing invalid JSON
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Terminus", "cache");
        var cacheFile = Path.Combine(cacheDir, $"{key}.json");
        await File.WriteAllTextAsync(cacheFile, "{ invalid json }");

        // Now try to load - should detect corruption
        var (data, loadedMetadata, status) = await cacheService.LoadAsync<string>(key);

        status.Should().Be(CacheStatus.Corrupted);
        data.Should().BeNull();

        // Original file should be renamed
        File.Exists(cacheFile).Should().BeFalse();

        // Corrupted file should exist
        var corruptedFiles = Directory.GetFiles(cacheDir, $"{key}.corrupted_*.json");
        corruptedFiles.Should().NotBeEmpty();

        // Cleanup
        foreach (var file in corruptedFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ClearAll_RemovesAllCacheFiles()
    {
        var cacheService = new CacheService();
        var keys = Enumerable.Range(1, 3).Select(i => $"clear_test_{i}_{Guid.NewGuid()}").ToList();

        // Create multiple cache entries
        foreach (var key in keys)
        {
            var metadata = new CacheMetadata
            {
                FetchedAt = DateTime.UtcNow,
                SourceUrl = "https://test.example.com",
                FetchSuccess = true,
                EventCount = 1
            };

            await cacheService.SaveAsync(key, new List<string> { "test" }, metadata);
        }

        // Clear all
        cacheService.ClearAll();

        // Verify all are gone
        foreach (var key in keys)
        {
            var (_, _, status) = await cacheService.LoadAsync<string>(key);
            status.Should().Be(CacheStatus.Default);
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNotExists()
    {
        // This test verifies the constructor creates the cache directory
        var cacheService = new CacheService();
        var key = $"dir_test_{Guid.NewGuid()}";

        var metadata = new CacheMetadata
        {
            FetchedAt = DateTime.UtcNow,
            SourceUrl = "https://test.example.com",
            FetchSuccess = true,
            EventCount = 1
        };

        // Should not throw even if directory doesn't exist
        await cacheService.SaveAsync(key, new List<string> { "test" }, metadata);

        var (data, _, status) = await cacheService.LoadAsync<string>(key);

        status.Should().Be(CacheStatus.Cached);
        data.Should().NotBeNull();
    }
}
