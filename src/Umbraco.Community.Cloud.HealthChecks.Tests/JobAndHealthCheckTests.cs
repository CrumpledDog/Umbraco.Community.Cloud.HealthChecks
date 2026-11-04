using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.Cloud.HealthChecks.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Sync;
using Umbraco.Community.Cloud.HealthChecks.Stats;
using Xunit;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

public class JobTests
{
    private static (StatsRefreshJob job, StatsService svc, InMemoryResultStore store, TempFolder root) Create(CloudHealthChecksOptions? options = null)
    {
        var root = new TempFolder();
        var store = new InMemoryResultStore();
        var o = options ?? new CloudHealthChecksOptions { LocalTestMode = true };
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1", Path.Combine(root.Path, "tmp"));
        var svc = Build.Service(store, catalog, new FakeDiskReader { Usage = new DiskUsage(1000, 500) }, o);
        // Test mode points the NuGet target at the real user profile: never walk that in a unit test
        svc.Scanner = (p, n, t) => p.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase) ? FolderScanner.Scan(p, n, t) : new FolderSizeResult { CalculatedAt = n };
        var job = new StatsRefreshJob(svc, catalog, Options.Create(o), NullLogger<StatsRefreshJob>.Instance);
        return (job, svc, store, root);
    }

    [Fact]
    public void Runs_on_every_server_role()
    {
        var (job, _, _, root) = Create();
        using var _r = root;

        Assert.Contains(ServerRole.Single, job.ServerRoles);
        Assert.Contains(ServerRole.SchedulingPublisher, job.ServerRoles);
        Assert.Contains(ServerRole.Subscriber, job.ServerRoles);
        Assert.Equal(TimeSpan.FromHours(6), job.Period);
        Assert.True(job.Delay >= TimeSpan.FromMinutes(2) && job.Delay < TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task First_run_measures_shared_per_instance_and_disk_targets()
    {
        var (job, _, store, root) = Create();
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 10);
        root.AddFile("tmp/t.bin", 20);

        await job.RunJobAsync();

        Assert.Contains("stats:shared:site1:umbraco-logs", store.Items.Keys);
        Assert.Contains("stats:instance:m1:local-temp", store.Items.Keys);
        Assert.Contains("stats:shared:site1:home-disk", store.Items.Keys);
        Assert.Contains("stats-claim:site1:umbraco-logs", store.Items.Keys);
        Assert.Equal(10, store.Items["stats:shared:site1:umbraco-logs"].SizeInBytes);
    }

    [Fact]
    public async Task Shared_folder_is_skipped_when_another_server_claimed_it_but_per_instance_and_disk_still_run()
    {
        var (job, _, store, root) = Create();
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 10);
        root.AddFile("tmp/t.bin", 20);
        store.Items["stats-claim:site1:umbraco-logs"] = new FolderSizeResult { CalculatedAt = DateTime.UtcNow, MachineId = "other" };

        await job.RunJobAsync();

        Assert.DoesNotContain("stats:shared:site1:umbraco-logs", store.Items.Keys);
        Assert.Contains("stats:instance:m1:local-temp", store.Items.Keys);
        Assert.Contains("stats:shared:site1:home-disk", store.Items.Keys);
    }

    [Fact]
    public async Task Second_run_straight_after_does_not_rescan_the_shared_folder()
    {
        var (job, svc, _, root) = Create();
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 10);
        root.AddFile("tmp/t.bin", 20);
        await job.RunJobAsync();

        var scans = 0;
        svc.Scanner = (p, _, _) =>
        {
            if (p.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase)) scans++;
            return new FolderSizeResult();
        };
        await job.RunJobAsync();

        // only the per-instance folder (temp) is scanned again; the shared one is claimed/fresh
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task Disabled_background_scan_does_nothing()
    {
        var (job, _, store, root) = Create(new CloudHealthChecksOptions { LocalTestMode = true, BackgroundScan = { Enabled = false } });
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 10);

        await job.RunJobAsync();

        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Never_throws_even_when_a_scan_times_out_or_the_store_fails()
    {
        var (job, svc, store, root) = Create();
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 10);
        svc.Scanner = (_, _, token) =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            return new FolderSizeResult();
        };
        var o = new CloudHealthChecksOptions { LocalTestMode = true, BackgroundScan = { ScanTimeLimitMinutes = 0.001 } };
        var slow = Build.Service(store, Build.TestModeCatalog(root.Path, "m1", "site1"), new FakeDiskReader(), o);
        slow.Scanner = svc.Scanner;
        var timedOutJob = new StatsRefreshJob(slow, Build.TestModeCatalog(root.Path, "m1", "site1"), Options.Create(o), NullLogger<StatsRefreshJob>.Instance);

        await timedOutJob.RunJobAsync();

        Assert.False(store.Items["stats:shared:site1:umbraco-logs"].Success);
    }
}

public class HealthCheckCompatibilityTests
{
    [Fact]
    public async Task Logs_health_check_reports_a_1_3_cached_result_on_any_instance()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 1);
        var store = new InMemoryResultStore();
        store.Items["UmbracoLogsFolderSizeHealthCheck"] = new FolderSizeResult { SizeInBytes = 600L * 1024 * 1024, FileCount = 4, CalculatedAt = DateTime.UtcNow };
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path);
        var disk = new FakeDiskReader();
        var check = new UmbracoLogsFolderSizeHealthCheck(
            store,
            new FakeHostEnvironment { ContentRootPath = root.Path },
            options,
            Build.Service(store, catalog, disk, options.Value),
            new StatsThresholds(options, catalog, disk),
            catalog);

        var statuses = (await check.GetStatusAsync()).ToList();

        var status = Assert.Single(statuses);
        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.Contains("(4 files)", status.Message);
        Assert.Contains("from cache", status.Description);
    }

    [Fact]
    public async Task Logs_health_check_prefers_this_sites_new_result_and_is_ok_when_small()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 1);
        var store = new InMemoryResultStore();
        store.Items["UmbracoLogsFolderSizeHealthCheck"] = new FolderSizeResult { SizeInBytes = 600L * 1024 * 1024, CalculatedAt = DateTime.UtcNow };
        store.Items["stats:shared:site1:umbraco-logs"] = new FolderSizeResult { SizeInBytes = 1024, FileCount = 1, CalculatedAt = DateTime.UtcNow };
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path);
        var disk = new FakeDiskReader();
        var check = new UmbracoLogsFolderSizeHealthCheck(
            store,
            new FakeHostEnvironment { ContentRootPath = root.Path },
            options,
            Build.Service(store, catalog, disk, options.Value),
            new StatsThresholds(options, catalog, disk),
            catalog);

        var status = Assert.Single(await check.GetStatusAsync());

        Assert.Equal(StatusResultType.Success, status.ResultType);
    }

    private static UmbracoLogsFolderSizeHealthCheck LogsCheck(TempFolder root, InMemoryResultStore store, CloudHealthChecksOptions o)
    {
        var options = Options.Create(o);
        var catalog = Build.TestModeCatalog(root.Path);
        var disk = new FakeDiskReader();
        return new UmbracoLogsFolderSizeHealthCheck(
            store,
            new FakeHostEnvironment { ContentRootPath = root.Path },
            options,
            Build.Service(store, catalog, disk, o),
            new StatsThresholds(options, catalog, disk),
            catalog);
    }

    [Fact]
    public async Task Cold_cache_never_scans_in_the_request_it_reports_pending_until_the_job_has_run()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 5);
        var store = new InMemoryResultStore();

        var status = Assert.Single(await LogsCheck(root, store, new CloudHealthChecksOptions { LocalTestMode = true }).GetStatusAsync());

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Contains("no measurement", status.Message);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task With_background_scanning_disabled_the_check_still_live_scans()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 5);
        var store = new InMemoryResultStore();

        var status = Assert.Single(await LogsCheck(root, store, new CloudHealthChecksOptions { LocalTestMode = true, BackgroundScan = { Enabled = false } }).GetStatusAsync());

        Assert.Equal(StatusResultType.Success, status.ResultType);
    }

    [Fact]
    public void Original_public_constructors_are_kept_and_di_still_picks_the_new_ones()
    {
        var options = Options.Create(new CloudHealthChecksOptions());
        var store = new InMemoryResultStore();
        var host = new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() };

        Assert.NotNull(new UmbracoLogsFolderSizeHealthCheck(store, host, options));
        Assert.NotNull(new NuGetCacheSizeHealthCheck(store, options));
        Assert.NotNull(new AzureStorageHealthCheck(options, host));

        using var root = new TempFolder();
        var catalog = Build.TestModeCatalog(root.Path);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IHealthCheckResultStore>(store);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(host);
        services.AddSingleton(options);
        services.AddSingleton<IDiskUsageReader>(new FakeDiskReader());
        services.AddSingleton<IStatsTargetCatalog>(catalog);
        services.AddSingleton<IStatsThresholds, StatsThresholds>();
        services.AddSingleton<IStatsService>(Build.Service(store, catalog));
        services.AddSingleton<UmbracoLogsFolderSizeHealthCheck>();
        services.AddSingleton<NuGetCacheSizeHealthCheck>();
        services.AddSingleton<AzureStorageHealthCheck>();
        using var provider = services.BuildServiceProvider();

        Assert.Equal("Hosting", provider.GetRequiredService<UmbracoLogsFolderSizeHealthCheck>().Group);
        Assert.Equal("Hosting", provider.GetRequiredService<NuGetCacheSizeHealthCheck>().Group);
        Assert.Equal("Hosting", provider.GetRequiredService<AzureStorageHealthCheck>().Group);
    }
}

[HealthCheck("5B0B2E8C-3A55-4E57-9E6B-0C6B6E9D1A11", "NuGet like", Group = "Test")]
internal sealed class NuGetLikeCheck : FolderSizeHealthCheckBase
{
    private readonly string _path;

    public NuGetLikeCheck(string path, IHealthCheckResultStore store, IOptions<CloudHealthChecksOptions> options, IStatsService stats, IStatsThresholds thresholds)
        : base(store, options, stats, thresholds)
    {
        _path = path;
    }

    protected override string? StatsTargetKey => StatsTargetKeys.NuGetCache;

    public override string FolderPath => _path;

    protected override long WarningThresholdMb => 0;

    protected override long ErrorThresholdMb => 0;

    protected override string FolderDisplayName => "NuGet cache";
}

public class QuotaAwareHealthCheckTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public async Task Health_check_uses_the_cached_results_quota_for_status_and_the_displayed_thresholds()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path);
        var disk = new FakeDiskReader { Usage = new DiskUsage(100 * Gb, 0) }; // this host: 100 GB
        var stats = Build.Service(store, catalog, disk, options.Value);
        // cached result from a site with a 6 GB quota: 4 GB cache is over its 3 GB error threshold
        store.Items["stats:shared:site1:nuget-cache"] = new FolderSizeResult
        {
            SizeInBytes = 4 * Gb,
            FileCount = 1,
            CalculatedAt = DateTime.UtcNow,
            DiskTotalBytes = 6 * Gb,
            DiskFreeBytes = Gb
        };
        var check = new NuGetLikeCheck(root.Path, store, options, stats, new StatsThresholds(options, catalog, disk));

        var status = Assert.Single(await check.GetStatusAsync());

        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.Contains("3072 MB", status.Message);
    }
}
