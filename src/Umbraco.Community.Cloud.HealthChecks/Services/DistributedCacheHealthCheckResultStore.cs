using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbraco.Community.Cloud.HealthChecks.Services;

/// <summary>
/// Stores health check results in distributed cache (e.g., Redis, in-memory).
/// Suitable for deployments with a configured distributed cache provider.
/// </summary>
public class DistributedCacheHealthCheckResultStore : IHealthCheckResultStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheHealthCheckResultStore> _logger;
    private readonly CloudHealthChecksOptions _options;

    public DistributedCacheHealthCheckResultStore(
        IDistributedCache cache,
        ILogger<DistributedCacheHealthCheckResultStore> logger,
        IOptions<CloudHealthChecksOptions> options)
    {
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task StoreAsync(string healthCheckName, FolderSizeResult result)
    {
        var cacheKey = GetCacheKey(healthCheckName);
        var json = JsonSerializer.Serialize(result);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_options.BackgroundScan.CacheExpirationHours)
        };

        await _cache.SetAsync(cacheKey, bytes, cacheOptions);
        _logger.LogDebug("Stored health check result for {HealthCheckName} in distributed cache", healthCheckName);
    }

    /// <inheritdoc />
    public async Task<FolderSizeResult?> GetAsync(string healthCheckName)
    {
        var cacheKey = GetCacheKey(healthCheckName);
        var bytes = await _cache.GetAsync(cacheKey);

        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<FolderSizeResult>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize health check result for {HealthCheckName}", healthCheckName);
            return null;
        }
    }

    private static string GetCacheKey(string healthCheckName)
    {
        return $"{Constants.ApiName}:foldersize:{healthCheckName}";
    }
}
