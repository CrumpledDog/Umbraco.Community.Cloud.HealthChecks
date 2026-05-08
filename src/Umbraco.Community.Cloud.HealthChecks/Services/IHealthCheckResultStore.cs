namespace Umbraco.Community.Cloud.HealthChecks.Services;

/// <summary>
/// Abstraction for storing and retrieving health check results.
/// Implementations can use distributed cache or database key-value storage.
/// </summary>
public interface IHealthCheckResultStore
{
    /// <summary>
    /// Stores a health check result.
    /// </summary>
    /// <param name="healthCheckName">The health check name (typically the type name).</param>
    /// <param name="result">The health check result to store.</param>
    Task StoreAsync(string healthCheckName, FolderSizeResult result);

    /// <summary>
    /// Gets a health check result.
    /// </summary>
    /// <param name="healthCheckName">The health check name.</param>
    /// <returns>The health check result, or null if not found.</returns>
    Task<FolderSizeResult?> GetAsync(string healthCheckName);
}
