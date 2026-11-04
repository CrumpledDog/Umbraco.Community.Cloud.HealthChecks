using System.Text.Json;
using Umbraco.Community.Cloud.HealthChecks.Stats;
using Xunit;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

public class ServiceTests
{
    private static (StatsService svc, InMemoryResultStore store, StatsTargetCatalog catalog, TempFolder root) Create(CloudHealthChecksOptions? options = null)
    {
        var root = new TempFolder();
        var store = new InMemoryResultStore();
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1", Path.Combine(root.Path, "tmp"));
        var svc = Build.Service(store, catalog, new FakeDiskReader { Usage = new DiskUsage(1000, 250) }, options);
        return (svc, store, catalog, root);
    }

    [Fact]
    public async Task Measure_shared_folder_stores_under_site_key_with_single_pass_numbers()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
        root.AddFile("umbraco/Logs/a.txt", 100, old);
        root.AddFile("umbraco/Logs/sub/b.txt", 50, DateTime.Now);

        var result = await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);

        Assert.True(result.Success);
        Assert.Equal(150, result.SizeInBytes);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(old, result.OldestFileLastWriteTime);
        Assert.Equal("m1", result.MachineId);
        Assert.Equal("site1", result.SiteKey);
        Assert.Equal(StatsTargetKeys.UmbracoLogs, result.TargetKey);
        Assert.Equal(75, result.DiskUsedPercent);
        Assert.Contains("stats:shared:site1:umbraco-logs", store.Items.Keys);
        Assert.DoesNotContain(store.Items.Keys, k => k.StartsWith("stats:instance"));
    }

    [Fact]
    public async Task Measure_per_instance_folder_stores_under_machine_id()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        root.AddFile("tmp/x.bin", 10);

        var result = await svc.MeasureAsync(StatsTargetKeys.LocalTemp);

        Assert.True(result.Success);
        Assert.Contains("stats:instance:m1:local-temp", store.Items.Keys);
    }

    [Fact]
    public async Task Get_latest_reads_another_servers_result()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        store.Items["stats:instance:other-machine:local-temp"] = new FolderSizeResult { SizeInBytes = 42, CalculatedAt = DateTime.UtcNow };
        store.Items["stats:shared:other-site:umbraco-logs"] = new FolderSizeResult { SizeInBytes = 7, CalculatedAt = DateTime.UtcNow };

        var temp = await svc.GetLatestAsync(StatsTargetKeys.LocalTemp, new ServerKey("other-machine", "ignored"));
        var logs = await svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs, new ServerKey("ignored", "other-site"));
        var none = await svc.GetLatestAsync(StatsTargetKeys.LocalTemp, new ServerKey("unknown", "x"));

        Assert.Equal(42, temp!.SizeInBytes);
        Assert.Equal(7, logs!.SizeInBytes);
        Assert.Null(none);
    }

    [Fact]
    public async Task Disk_target_on_current_server_is_read_live_and_other_servers_come_from_the_store()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        Directory.CreateDirectory(Path.Combine(root.Path, "wwwroot"));
        store.Items["stats:instance:other:local-disk"] = new FolderSizeResult { DiskTotalBytes = 100, DiskFreeBytes = 10, CalculatedAt = DateTime.UtcNow };

        var live = await svc.GetLatestAsync(StatsTargetKeys.LocalDisk);
        var other = await svc.GetLatestAsync(StatsTargetKeys.LocalDisk, new ServerKey("other", "s"));

        Assert.Equal(75, live!.DiskUsedPercent);
        Assert.Equal(90, other!.DiskUsedPercent);
        Assert.DoesNotContain("stats:instance:m1:local-disk", store.Items.Keys);
    }

    [Fact]
    public async Task Measure_disk_target_stores_disk_numbers_without_scanning()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;

        var result = await svc.MeasureAsync(StatsTargetKeys.HomeDisk);

        Assert.True(result.Success);
        Assert.Equal(1000, result.DiskTotalBytes);
        Assert.Equal(0, result.FileCount);
        Assert.Contains("stats:shared:site1:home-disk", store.Items.Keys);
    }

    [Fact]
    public async Task Record_publishes_a_post_cleanup_result_without_rescanning()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        svc.Scanner = (_, _, _) => throw new InvalidOperationException("must not scan");

        var input = new FolderSizeResult { SizeInBytes = 5, FileCount = 1, CalculatedAt = DateTime.UtcNow };
        await svc.RecordAsync(StatsTargetKeys.NuGetCache, input);

        var stored = store.Items["stats:shared:site1:nuget-cache"];
        Assert.Equal(5, stored.SizeInBytes);
        Assert.Equal("m1", stored.MachineId);
        Assert.Equal(StatsTargetKeys.NuGetCache, stored.TargetKey);
        Assert.Equal(75, stored.DiskUsedPercent);
        Assert.Null(input.MachineId); // caller's object is not modified
        Assert.Equal(5, (await svc.GetLatestAsync(StatsTargetKeys.NuGetCache))!.SizeInBytes);
    }

    [Fact]
    public async Task Legacy_results_from_1_3_are_still_read_and_still_written()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;

        // 1.3.x JSON had no new fields; stored under the health check type name
        var legacyJson = """{"SizeInBytes":2048,"FileCount":3,"OldestFileLastWriteTime":null,"CalculatedAt":"2026-01-01T00:00:00Z","ErrorMessage":null}""";
        store.Items["UmbracoLogsFolderSizeHealthCheck"] = JsonSerializer.Deserialize<FolderSizeResult>(legacyJson)!;

        var read = await svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs);
        Assert.Equal(2048, read!.SizeInBytes);
        Assert.True(read.Success);
        Assert.Null(read.DiskUsedPercent);

        // Writing a new result also refreshes the legacy key for instances still on 1.3.x
        root.AddFile("umbraco/Logs/a.txt", 10);
        await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);
        Assert.Equal(10, store.Items["UmbracoLogsFolderSizeHealthCheck"].SizeInBytes);

        // New-format JSON round trips and ignores the computed percentage
        var roundTrip = JsonSerializer.Deserialize<FolderSizeResult>(JsonSerializer.Serialize(store.Items["stats:shared:site1:umbraco-logs"]))!;
        Assert.Equal(75, roundTrip.DiskUsedPercent);
        Assert.DoesNotContain("DiskUsedPercent", JsonSerializer.Serialize(roundTrip));
    }

    [Fact]
    public async Task Scan_over_the_time_limit_stores_an_error_not_a_partial_number()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        Directory.CreateDirectory(Path.Combine(root.Path, "umbraco", "Logs"));
        svc.Scanner = (_, _, token) =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            return new FolderSizeResult();
        };

        var result = await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs, TimeSpan.FromMilliseconds(50));

        Assert.False(result.Success);
        Assert.Contains("took longer than the limit", result.ErrorMessage);
        Assert.Contains("will retry", result.ErrorMessage);
        Assert.Equal(0, result.SizeInBytes);
        Assert.False(store.Items["stats:shared:site1:umbraco-logs"].Success);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_being_recorded_as_a_timeout()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        Directory.CreateDirectory(Path.Combine(root.Path, "umbraco", "Logs"));
        using var cts = new CancellationTokenSource();
        svc.Scanner = (_, _, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return new FolderSizeResult();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.MeasureAsync(StatsTargetKeys.UmbracoLogs, TimeSpan.FromMinutes(5), cts.Token));
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Missing_folder_and_unknown_target_are_reported_not_stored()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;

        var missing = await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);

        Assert.False(missing.Success);
        Assert.Empty(store.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.MeasureAsync("nope"));
    }

    [Fact]
    public async Task Configured_time_limit_is_used_when_none_is_passed()
    {
        var (svc, _, _, root) = Create(new CloudHealthChecksOptions { LocalTestMode = true, BackgroundScan = { ScanTimeLimitMinutes = 0.001 } });
        using var _r = root;
        Directory.CreateDirectory(Path.Combine(root.Path, "umbraco", "Logs"));
        svc.Scanner = (_, _, token) =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            return new FolderSizeResult();
        };

        var result = await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);

        Assert.Contains("took longer than the limit", result.ErrorMessage);
    }
}

public class ClaimTests
{
    private static (StatsService svc, InMemoryResultStore store, StatsTarget target, TempFolder root) Create()
    {
        var root = new TempFolder();
        var store = new InMemoryResultStore();
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var svc = Build.Service(store, catalog);
        return (svc, store, catalog.Find(StatsTargetKeys.UmbracoLogs)!, root);
    }

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    [Fact]
    public async Task First_server_claims_and_writes_the_marker_before_any_scan()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;

        Assert.True(await svc.TryClaimSharedAsync(target, Interval));

        Assert.Contains("stats-claim:site1:umbraco-logs", store.Items.Keys);
    }

    [Fact]
    public async Task Fresh_marker_from_another_server_skips()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        svc.UtcNow = () => now;
        store.Items["stats-claim:site1:umbraco-logs"] = new FolderSizeResult { CalculatedAt = now.AddHours(-2.9), MachineId = "other" };

        Assert.False(await svc.TryClaimSharedAsync(target, Interval));
    }

    [Fact]
    public async Task Marker_older_than_half_the_interval_is_claimable_again()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        svc.UtcNow = () => now;
        store.Items["stats-claim:site1:umbraco-logs"] = new FolderSizeResult { CalculatedAt = now.AddHours(-3.1) };

        Assert.True(await svc.TryClaimSharedAsync(target, Interval));
        Assert.Equal(now, store.Items["stats-claim:site1:umbraco-logs"].CalculatedAt);
    }

    [Fact]
    public async Task Second_claim_straight_after_the_first_is_refused()
    {
        var (svc, _, target, root) = Create();
        using var _r = root;

        Assert.True(await svc.TryClaimSharedAsync(target, Interval));
        Assert.False(await svc.TryClaimSharedAsync(target, Interval));
    }

    [Fact]
    public async Task A_recent_recorded_result_counts_so_a_cleanup_is_not_followed_by_a_rescan()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        svc.UtcNow = () => now;
        store.Items["stats:shared:site1:umbraco-logs"] = new FolderSizeResult { CalculatedAt = now.AddMinutes(-10) };

        Assert.False(await svc.TryClaimSharedAsync(target, Interval));
    }

    [Fact]
    public async Task A_recent_error_result_does_not_block_a_retry()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        svc.UtcNow = () => now;
        store.Items["stats:shared:site1:umbraco-logs"] = new FolderSizeResult { CalculatedAt = now.AddMinutes(-10), ErrorMessage = "took longer" };

        Assert.True(await svc.TryClaimSharedAsync(target, Interval));
    }

    [Fact]
    public async Task Different_sites_do_not_share_a_marker()
    {
        var (svc, store, target, root) = Create();
        using var _r = root;
        store.Items["stats-claim:site2:umbraco-logs"] = new FolderSizeResult { CalculatedAt = DateTime.UtcNow };

        Assert.True(await svc.TryClaimSharedAsync(target, Interval));
    }
}

public class ReviewFixTests
{
    private static (StatsService svc, InMemoryResultStore store, FakeDiskReader disk, TempFolder root) Create()
    {
        var root = new TempFolder();
        var store = new InMemoryResultStore();
        var disk = new FakeDiskReader { Usage = new DiskUsage(1000, 500) };
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        return (Build.Service(store, catalog, disk), store, disk, root);
    }

    [Fact]
    public void Scanner_fails_when_the_deadline_expires_after_the_last_file()
    {
        using var root = new TempFolder();
        Directory.CreateDirectory(Path.Combine(root.Path, "empty-trailing-dir"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderScanner.Scan(root.Path, DateTime.UtcNow, cts.Token));
    }

    [Fact]
    public async Task Legacy_result_from_another_site_is_ignored_but_old_unlabelled_and_same_site_results_are_used()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;

        store.Items["UmbracoLogsFolderSizeHealthCheck"] = new FolderSizeResult { SizeInBytes = 9, SiteKey = "other-site", CalculatedAt = DateTime.UtcNow };
        Assert.Null(await svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs));

        store.Items["UmbracoLogsFolderSizeHealthCheck"] = new FolderSizeResult { SizeInBytes = 8, SiteKey = "site1", CalculatedAt = DateTime.UtcNow };
        Assert.Equal(8, (await svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs))!.SizeInBytes);

        store.Items["UmbracoLogsFolderSizeHealthCheck"] = new FolderSizeResult { SizeInBytes = 7, CalculatedAt = DateTime.UtcNow };
        Assert.Equal(7, (await svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs))!.SizeInBytes);
    }

    [Fact]
    public async Task Failed_quota_read_replaces_the_previous_stored_reading_with_an_error()
    {
        var (svc, store, disk, root) = Create();
        using var _r = root;

        await svc.MeasureAsync(StatsTargetKeys.HomeDisk);
        Assert.True(store.Items["stats:shared:site1:home-disk"].Success);

        disk.Usage = null;
        var failed = await svc.MeasureAsync(StatsTargetKeys.HomeDisk);

        Assert.False(failed.Success);
        Assert.False(store.Items["stats:shared:site1:home-disk"].Success);
    }

    [Fact]
    public async Task Record_during_a_running_scan_is_not_overwritten_by_the_older_scan_result()
    {
        var (svc, store, _, root) = Create();
        using var _r = root;
        root.AddFile("umbraco/Logs/a.txt", 100);
        var scanStarted = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        svc.Scanner = (_, now, _) =>
        {
            scanStarted.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new FolderSizeResult { SizeInBytes = 100, FileCount = 1, CalculatedAt = now };
        };

        var scan = svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(10)));

        // clean-up deletes the files and publishes the smaller result while the scan is still counting
        await svc.RecordAsync(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { SizeInBytes = 10, FileCount = 0, CalculatedAt = DateTime.UtcNow });
        release.Set();
        var result = await scan;

        Assert.Equal(10, store.Items["stats:shared:site1:umbraco-logs"].SizeInBytes);
        Assert.Equal(10, result.SizeInBytes);
    }
}

public class CrossServerPublicationTests
{
    [Fact]
    public async Task A_result_recorded_by_another_server_while_this_server_scans_is_not_overwritten()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 100);
        var store = new InMemoryResultStore();

        // Two servers of the same site (shared logs disk), one shared result store
        var scanning = Build.Service(store, Build.TestModeCatalog(root.Path, "server-a", "site1"));
        var cleaning = Build.Service(store, Build.TestModeCatalog(root.Path, "server-b", "site1"));

        var scanStart = DateTime.UtcNow.AddMinutes(-5);
        scanning.UtcNow = () => scanStart;
        var scanStarted = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        scanning.Scanner = (_, now, _) =>
        {
            scanStarted.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new FolderSizeResult { SizeInBytes = 100, FileCount = 1, CalculatedAt = now };
        };

        var scan = scanning.MeasureAsync(StatsTargetKeys.UmbracoLogs);
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(10)));

        await cleaning.RecordAsync(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { SizeInBytes = 10, CalculatedAt = DateTime.UtcNow });
        release.Set();
        var result = await scan;

        Assert.Equal(10, store.Items["stats:shared:site1:umbraco-logs"].SizeInBytes);
        Assert.Equal(10, result.SizeInBytes);
        Assert.Equal("server-b", store.Items["stats:shared:site1:umbraco-logs"].MachineId);
    }

    [Fact]
    public async Task An_older_stored_result_does_not_stop_a_new_scan_publishing()
    {
        using var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 100);
        var store = new InMemoryResultStore();
        var svc = Build.Service(store, Build.TestModeCatalog(root.Path, "server-a", "site1"));
        store.Items["stats:shared:site1:umbraco-logs"] = new FolderSizeResult { SizeInBytes = 1, CalculatedAt = DateTime.UtcNow.AddHours(-6) };

        var result = await svc.MeasureAsync(StatsTargetKeys.UmbracoLogs);

        Assert.Equal(100, result.SizeInBytes);
        Assert.Equal(100, store.Items["stats:shared:site1:umbraco-logs"].SizeInBytes);
    }
}

public class SecondReviewFixTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void Remote_nuget_result_is_judged_against_its_own_quota_not_this_hosts()
    {
        var root = Path.GetTempPath();
        var catalog = new StatsTargetCatalog(root, new CloudHealthChecksOptions(), "m", Build.Azure(), HostingPlatform.AzureWebApp, _ => true);
        // This host has a 100 GB quota; the measured site has 6 GB (error threshold 3 GB)
        var thresholds = new StatsThresholds(Microsoft.Extensions.Options.Options.Create(new CloudHealthChecksOptions()), catalog, new FakeDiskReader { Usage = new DiskUsage(100 * Gb, 0) });

        var remote = new FolderSizeResult { SizeInBytes = 4 * Gb, DiskTotalBytes = 6 * Gb, DiskFreeBytes = Gb };
        var noQuota = new FolderSizeResult { SizeInBytes = 4 * Gb };

        Assert.Equal(ThresholdLevel.Error, thresholds.Evaluate(StatsTargetKeys.NuGetCache, remote).Level);
        Assert.Equal(3072, thresholds.Resolve(StatsTargetKeys.NuGetCache, 6 * Gb).SizeErrorMb);
        // Without quota metadata the local quota (100 GB -> 50 GB error) applies
        Assert.Equal(ThresholdLevel.Ok, thresholds.Evaluate(StatsTargetKeys.NuGetCache, noQuota).Level);
    }

    [Fact]
    public async Task Cancelled_token_stops_measure_get_and_record_before_any_work()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var disk = new FakeDiskReader { Usage = new DiskUsage(1000, 500) };
        var svc = Build.Service(store, Build.TestModeCatalog(root.Path, "m1", "site1"), disk);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.MeasureAsync(StatsTargetKeys.HomeDisk, null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.GetLatestAsync(StatsTargetKeys.UmbracoLogs, null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.RecordAsync(StatsTargetKeys.UmbracoLogs, new FolderSizeResult(), cts.Token));
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Record_fills_the_disk_pair_when_either_value_is_missing()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var svc = Build.Service(store, Build.TestModeCatalog(root.Path, "m1", "site1"), new FakeDiskReader { Usage = new DiskUsage(1000, 250) });

        await svc.RecordAsync(StatsTargetKeys.UmbracoLogs, new FolderSizeResult { DiskTotalBytes = 1000 });

        Assert.Equal(250, store.Items["stats:shared:site1:umbraco-logs"].DiskFreeBytes);
        Assert.Equal(75, store.Items["stats:shared:site1:umbraco-logs"].DiskUsedPercent);
    }

    [Fact]
    public void Scan_observes_cancellation_while_walking_only_directories()
    {
        using var root = new TempFolder();
        for (var i = 0; i < 20; i++)
        {
            Directory.CreateDirectory(Path.Combine(root.Path, "d" + i, "inner"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderScanner.Scan(root.Path, DateTime.UtcNow, cts.Token));
    }

    [Fact]
    public void Scan_counts_files_only_and_reports_the_oldest()
    {
        using var root = new TempFolder();
        var old = new DateTime(2019, 5, 5, 0, 0, 0, DateTimeKind.Local);
        root.AddFile("a/b/old.txt", 30, old);
        root.AddFile("new.txt", 20);
        Directory.CreateDirectory(Path.Combine(root.Path, "empty"));

        var r = FolderScanner.Scan(root.Path, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(50, r.SizeInBytes);
        Assert.Equal(2, r.FileCount);
        Assert.Equal(old, r.OldestFileLastWriteTime);
    }
}
