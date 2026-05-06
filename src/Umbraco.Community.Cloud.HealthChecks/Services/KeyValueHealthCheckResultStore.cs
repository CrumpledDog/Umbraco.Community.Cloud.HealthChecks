using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Cloud.HealthChecks.Services;

/// <summary>
/// Stores health check results in Umbraco's KeyValue database table.
/// Suitable for deployments without a distributed cache provider.
/// Data persists in the database and survives application restarts.
/// </summary>
public class KeyValueHealthCheckResultStore : IHealthCheckResultStore
{
    private readonly IKeyValueService _keyValueService;
    private readonly ILogger<KeyValueHealthCheckResultStore> _logger;

    public KeyValueHealthCheckResultStore(
        IKeyValueService keyValueService,
        ILogger<KeyValueHealthCheckResultStore> logger)
    {
        _keyValueService = keyValueService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StoreAsync(string healthCheckName, FolderSizeResult result)
    {
        var key = GetCacheKey(healthCheckName);
        var json = JsonSerializer.Serialize(result);

        _keyValueService.SetValue(key, json);
        _logger.LogDebug("Stored health check result for {HealthCheckName} in KeyValue store", healthCheckName);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<FolderSizeResult?> GetAsync(string healthCheckName)
    {
        var key = GetCacheKey(healthCheckName);
        var json = _keyValueService.GetValue(key);

        if (string.IsNullOrEmpty(json))
        {
            return Task.FromResult<FolderSizeResult?>(null);
        }

        try
        {
            var result = JsonSerializer.Deserialize<FolderSizeResult>(json);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize health check result for {HealthCheckName}", healthCheckName);
            return Task.FromResult<FolderSizeResult?>(null);
        }
    }

    private static string GetCacheKey(string healthCheckName)
    {
        return $"{Constants.ApiName}:foldersize:{healthCheckName}";
    }
}
