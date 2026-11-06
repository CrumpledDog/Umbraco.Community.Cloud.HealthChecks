using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPoco;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Umbraco.Extensions;

namespace Umbraco.Community.Cloud.HealthChecks.Stats;

/// <summary>A server known to this site's cluster.</summary>
/// <param name="Key">Machine id and site key. The site key is empty until that server has reported once.</param>
/// <param name="DisplayName">Friendly name: machine name, plus <c>/{SiteName}</c> when <c>Umbraco:CMS:Hosting:SiteName</c> is set.</param>
/// <param name="IsCurrent">True for the server answering this call.</param>
/// <param name="LastReportedAt">When the server's refresh job last ran (UTC), or null if it has not reported.</param>
public sealed record StatsServer(ServerKey Key, string DisplayName, bool IsCurrent, DateTime? LastReportedAt);

/// <summary>Lists the servers whose measurements can be read.</summary>
public interface IStatsServers
{
    /// <summary>
    /// Every server Umbraco knows about (its <c>umbracoLastSynced</c> registration table, the same source the hosting
    /// kit uses; the machine id there is <c>IMachineInfoFactory.GetMachineIdentifier()</c>), paired with the identity each
    /// server published. The current server is always included and listed first. Servers that never reported, such as
    /// decommissioned instances, are left out because their site key, and so their shared results, are unknown.
    /// </summary>
    Task<IReadOnlyList<StatsServer>> GetServersAsync(CancellationToken cancellationToken = default);
}

/// <summary>Machine ids from Umbraco's server registration.</summary>
internal interface IKnownMachineIds
{
    Task<IReadOnlyList<string>> GetAsync();
}

internal sealed class UmbracoKnownMachineIds : IKnownMachineIds
{
    private readonly IScopeProvider _scopeProvider;

    public UmbracoKnownMachineIds(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public async Task<IReadOnlyList<string>> GetAsync()
    {
        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql().Select("*").From<LastSyncedDto>().OrderBy<LastSyncedDto>(x => x.MachineId);
        var rows = await scope.Database.FetchAsync<LastSyncedDto>(sql);
        scope.Complete();
        return rows.Select(r => r.MachineId).ToList();
    }
}

/// <inheritdoc />
public sealed class StatsServers : IStatsServers
{
    private readonly IHealthCheckResultStore _store;
    private readonly IStatsTargetCatalog _catalog;
    private readonly IKnownMachineIds _machineIds;
    private readonly ILogger<StatsServers> _logger;
    private readonly string _displayName;

    /// <summary>Creates the service.</summary>
    public StatsServers(
        IHealthCheckResultStore store,
        IStatsTargetCatalog catalog,
        IScopeProvider scopeProvider,
        IOptions<HostingSettings> hostingSettings,
        ILogger<StatsServers> logger)
        : this(store, catalog, new UmbracoKnownMachineIds(scopeProvider), BuildDisplayName(hostingSettings.Value.SiteName, Environment.MachineName, catalog), logger)
    {
    }

    internal StatsServers(IHealthCheckResultStore store, IStatsTargetCatalog catalog, IKnownMachineIds machineIds, string displayName, ILogger<StatsServers> logger)
    {
        _store = store;
        _catalog = catalog;
        _machineIds = machineIds;
        _displayName = displayName;
        _logger = logger;
    }

    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    // Same format as the hosting kit's MachineNameFactory (and Umbraco's machine identifier suffix): machine name, plus /SiteName
    // when Umbraco:CMS:Hosting:SiteName is set. When it is not set on App Service several sites on one plan share the machine
    // name, so fall back to the App Service site name (and slot) to keep servers distinguishable.
    internal static string BuildDisplayName(string? siteName, string machineName, IStatsTargetCatalog? catalog = null)
    {
        if (!string.IsNullOrWhiteSpace(siteName))
        {
            return $"{machineName}/{siteName}";
        }

        return catalog is { Platform: not HostingPlatform.Other }
            ? $"{machineName}/{catalog.CurrentServer.SiteKey}"
            : machineName;
    }

    internal static string IdentityName(string machineId) => $"stats-server:{machineId}";

    // When this server's refresh job last published its identity (null until its first run, or when background scanning is off)
    private async Task<DateTime?> ReadOwnReportTimeAsync(ServerKey current)
    {
        try
        {
            return (await _store.GetAsync(IdentityName(current.MachineId)))?.CalculatedAt;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read this server's published identity");
            return null;
        }
    }

    /// <summary>Publishes this server's identity (site key and name) so other servers can find its shared results.</summary>
    internal Task PublishCurrentAsync() =>
        _store.StoreAsync(IdentityName(_catalog.CurrentServer.MachineId), new FolderSizeResult
        {
            CalculatedAt = UtcNow(),
            MachineId = _catalog.CurrentServer.MachineId,
            SiteKey = _catalog.CurrentServer.SiteKey,
            ServerName = _displayName
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatsServer>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        var current = _catalog.CurrentServer;
        var servers = new List<StatsServer>
        {
            new(current, _displayName, true, await ReadOwnReportTimeAsync(current))
        };

        IReadOnlyList<string> ids;
        try
        {
            ids = await _machineIds.GetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the server registration; only the current server is listed");
            return servers;
        }

        foreach (var id in ids.Where(i => !string.Equals(i, current.MachineId, StringComparison.Ordinal)).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FolderSizeResult? identity;
            try
            {
                identity = await _store.GetAsync(IdentityName(id)).WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable identity must not hide the other servers
                _logger.LogWarning(ex, "Could not read the identity of server {MachineId}", id);
                continue;
            }

            if (identity?.SiteKey == null)
            {
                continue;
            }

            servers.Add(new StatsServer(new ServerKey(id, identity.SiteKey), identity.ServerName ?? id, false, identity.CalculatedAt));
        }

        return servers;
    }
}
