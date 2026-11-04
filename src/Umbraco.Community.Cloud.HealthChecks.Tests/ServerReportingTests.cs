using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.Cloud.HealthChecks.Stats;
using Xunit;

namespace Umbraco.Community.Cloud.HealthChecks.Tests;

public class ServerListTests
{
    private static void Identity(InMemoryResultStore store, string machine, string site, string name) =>
        store.Items[StatsServers.IdentityName(machine)] = new FolderSizeResult { MachineId = machine, SiteKey = site, ServerName = name, CalculatedAt = DateTime.UtcNow };

    [Fact]
    public async Task Lists_current_first_then_known_servers_that_have_reported_an_identity()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var ids = new FakeMachineIds();
        ids.Ids.AddRange(new[] { "m3", "m1", "m2", "m-stale" });
        Identity(store, "m2", "site1", "WEB2/site1");
        Identity(store, "m3", "site2", "FE3/site2");

        var servers = await ServersBuild.Servers(store, catalog, ids, "WEB1/site1").GetServersAsync();

        Assert.Equal(3, servers.Count);
        Assert.True(servers[0].IsCurrent);
        Assert.Equal("m1", servers[0].Key.MachineId);
        Assert.Equal("WEB1/site1", servers[0].DisplayName);
        Assert.DoesNotContain(servers, s => s.Key.MachineId == "m-stale"); // registered but never reported
        Assert.Equal("site2", servers.Single(s => s.Key.MachineId == "m3").Key.SiteKey);
    }

    [Fact]
    public async Task Registration_failure_still_lists_the_current_server()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var ids = new FakeMachineIds { Throw = true };

        var servers = await ServersBuild.Servers(store, Build.TestModeCatalog(root.Path), ids).GetServersAsync();

        Assert.Single(servers);
        Assert.True(servers[0].IsCurrent);
    }

    [Fact]
    public async Task Publishes_this_servers_identity()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");

        await ServersBuild.Servers(store, catalog, null, "WEB1/mysite").PublishCurrentAsync();

        var stored = store.Items[StatsServers.IdentityName("m1")];
        Assert.Equal("site1", stored.SiteKey);
        Assert.Equal("WEB1/mysite", stored.ServerName);
    }

    [Fact]
    public void Display_name_uses_the_umbraco_site_name_like_the_hosting_kit()
    {
        using var root = new TempFolder();
        var azure = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.Azure("plansite", "staging"), null, _ => false);
        var other = new StatsTargetCatalog(root.Path, new CloudHealthChecksOptions(), "m", Build.OffAzure(), null, _ => false);

        // Umbraco:CMS:Hosting:SiteName wins, same machine/siteName format as the hosting kit's MachineNameFactory
        Assert.Equal("RD0001/MySite", StatsServers.BuildDisplayName("MySite", "RD0001", azure));
        // Not set on App Service: fall back to the App Service site (and slot) so servers on one plan stay distinguishable
        Assert.Equal("RD0001/plansite:staging", StatsServers.BuildDisplayName(null, "RD0001", azure));
        Assert.Equal("RD0001/plansite:staging", StatsServers.BuildDisplayName("  ", "RD0001", azure));
        // Not set elsewhere: machine name only
        Assert.Equal("VM1", StatsServers.BuildDisplayName(null, "VM1", other));
    }
}

public class MultiServerHealthCheckTests
{
    private static (UmbracoLogsFolderSizeHealthCheck check, InMemoryResultStore store, TempFolder root) Create(Action<InMemoryResultStore> seed, params (string machine, string site, string name)[] others)
    {
        var root = new TempFolder();
        root.AddFile("umbraco/Logs/a.txt", 1);
        var store = new InMemoryResultStore();
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var disk = new FakeDiskReader();
        var ids = new FakeMachineIds();
        ids.Ids.Add("m1");
        foreach (var (machine, site, name) in others)
        {
            ids.Ids.Add(machine);
            store.Items[StatsServers.IdentityName(machine)] = new FolderSizeResult { MachineId = machine, SiteKey = site, ServerName = name, CalculatedAt = DateTime.UtcNow };
        }

        seed(store);
        var check = new UmbracoLogsFolderSizeHealthCheck(
            store,
            new FakeHostEnvironment { ContentRootPath = root.Path },
            options,
            Build.Service(store, catalog, disk, options.Value),
            new StatsThresholds(options, catalog, disk),
            catalog,
            ServersBuild.Servers(store, catalog, ids, "WEB1/site1"));
        return (check, store, root);
    }

    private static FolderSizeResult Logs(long mb) => new() { SizeInBytes = mb * 1024 * 1024, FileCount = 3, CalculatedAt = DateTime.UtcNow };

    [Fact]
    public async Task Single_server_reads_exactly_as_before_with_no_name_prefix()
    {
        var (check, _, root) = Create(s => s.Items["stats:shared:site1:umbraco-logs"] = Logs(10));
        using var _r = root;

        var status = Assert.Single(await check.GetStatusAsync());

        Assert.StartsWith("Umbraco Logs folder size:", status.Message);
    }

    [Fact]
    public async Task Reports_each_other_site_once_by_name_and_names_this_server()
    {
        var (check, _, root) = Create(
            s =>
            {
                s.Items["stats:shared:site1:umbraco-logs"] = Logs(10);
                s.Items["stats:shared:site2:umbraco-logs"] = Logs(600);
            },
            ("m2", "site1", "WEB2/site1"), // same site as this server: shares its disk, no extra line
            ("m3", "site2", "FE3/site2"),
            ("m4", "site2", "FE4/site2"));
        using var _r = root;

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.Equal(2, statuses.Count);
        Assert.StartsWith("[WEB1/site1] Umbraco Logs folder size:", statuses[0].Message);
        Assert.StartsWith("[site site2 (FE3/site2, FE4/site2)] Umbraco Logs folder size:", statuses[1].Message);
        Assert.Equal(StatusResultType.Error, statuses[1].ResultType); // 600 MB is over the 500 MB error threshold
        Assert.Contains("Exceeds error threshold", statuses[1].Message);
        Assert.Equal(StatusResultType.Success, statuses[0].ResultType);
    }

    [Fact]
    public async Task Another_server_that_has_not_been_measured_yet_is_reported_as_pending()
    {
        var (check, _, root) = Create(s => s.Items["stats:shared:site1:umbraco-logs"] = Logs(10), ("m3", "site2", "FE3/site2"));
        using var _r = root;

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.Equal(StatusResultType.Info, statuses[1].ResultType);
        Assert.Contains("no measurement has been recorded yet", statuses[1].Message);
    }

    [Fact]
    public async Task Per_instance_check_reports_every_server_by_name()
    {
        using var root = new TempFolder();
        root.AddFile("tmp/x.bin", 1);
        var store = new InMemoryResultStore();
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1", Path.Combine(root.Path, "tmp"));
        var disk = new FakeDiskReader();
        var ids = new FakeMachineIds();
        ids.Ids.AddRange(new[] { "m1", "m2" });
        store.Items[StatsServers.IdentityName("m2")] = new FolderSizeResult { MachineId = "m2", SiteKey = "site1", ServerName = "WEB2/site1", CalculatedAt = DateTime.UtcNow };
        store.Items["stats:instance:m1:local-temp"] = new FolderSizeResult { SizeInBytes = 5, CalculatedAt = DateTime.UtcNow, DiskTotalBytes = 1000, DiskFreeBytes = 800 };
        store.Items["stats:instance:m2:local-temp"] = new FolderSizeResult { SizeInBytes = 6, CalculatedAt = DateTime.UtcNow, DiskTotalBytes = 1000, DiskFreeBytes = 50 };
        var check = new LocalTempFolderSizeHealthCheck(
            store, options, Build.Service(store, catalog, disk, options.Value), new StatsThresholds(options, catalog, disk),
            ServersBuild.Servers(store, catalog, ids, "WEB1/site1"), catalog);

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.Equal(2, statuses.Count);
        Assert.StartsWith("[WEB1/site1] Local temp folder size:", statuses[0].Message);
        Assert.Equal(StatusResultType.Success, statuses[0].ResultType);
        Assert.StartsWith("[WEB2/site1] Local temp folder size:", statuses[1].Message);
        Assert.Equal(StatusResultType.Error, statuses[1].ResultType); // that server's temp drive is 95% used
        Assert.Contains("Disk usage 95.0%", statuses[1].Message);
    }

    [Fact]
    public async Task A_failed_measurement_on_another_server_does_not_hide_the_current_servers_status()
    {
        var (check, store, root) = Create(s => s.Items["stats:shared:site1:umbraco-logs"] = Logs(10), ("m3", "site2", "FE3/site2"));
        using var _r = root;
        store.Items["stats:shared:site2:umbraco-logs"] = new FolderSizeResult { ErrorMessage = "took longer than the limit, will retry", CalculatedAt = DateTime.UtcNow };

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.Equal(StatusResultType.Success, statuses[0].ResultType);
        Assert.Equal(StatusResultType.Error, statuses[1].ResultType);
        Assert.Contains("will retry", statuses[1].Message);
    }

    [Fact]
    public async Task Azure_storage_check_reports_other_servers_disks_by_name()
    {
        using var root = new TempFolder();
        Directory.CreateDirectory(Path.Combine(root.Path, "wwwroot"));
        var store = new InMemoryResultStore();
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var disk = new FakeDiskReader { Usage = new DiskUsage(1000, 500) };
        var ids = new FakeMachineIds();
        ids.Ids.AddRange(new[] { "m1", "m3" });
        store.Items[StatsServers.IdentityName("m3")] = new FolderSizeResult { MachineId = "m3", SiteKey = "site2", ServerName = "FE3/site2", CalculatedAt = DateTime.UtcNow };
        store.Items["stats:shared:site2:home-disk"] = new FolderSizeResult { DiskTotalBytes = 1000, DiskFreeBytes = 90, CalculatedAt = DateTime.UtcNow };
        store.Items["stats:instance:m3:local-disk"] = new FolderSizeResult { DiskTotalBytes = 1000, DiskFreeBytes = 900, CalculatedAt = DateTime.UtcNow };
        var check = new AzureStorageHealthCheck(
            options, new FakeHostEnvironment { ContentRootPath = root.Path }, disk, catalog,
            ServersBuild.Servers(store, catalog, ids, "WEB1/site1"),
            Build.Service(store, catalog, disk, options.Value), new StatsThresholds(options, catalog, disk));

        var statuses = (await check.GetStatusAsync()).ToList();

        var home = Assert.Single(statuses, s => s.Message.StartsWith("[site site2 (FE3/site2)]"));
        Assert.Equal(StatusResultType.Error, home.ResultType); // 91% used
        var local = Assert.Single(statuses, s => s.Message.StartsWith("[FE3/site2]"));
        Assert.Equal(StatusResultType.Success, local.ResultType);
    }

    [Fact]
    public async Task Current_server_is_named_on_failures_too_not_only_on_the_size_line()
    {
        var (check, store, root) = Create(s => { }, ("m3", "site2", "FE3/site2"));
        using var _r = root;
        store.Items["stats:shared:site1:umbraco-logs"] = new FolderSizeResult { ErrorMessage = "access denied", CalculatedAt = DateTime.UtcNow };
        store.Items["stats:shared:site2:umbraco-logs"] = Logs(1);

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.StartsWith("[WEB1/site1] Error checking", statuses[0].Message);
        Assert.Equal(StatusResultType.Error, statuses[0].ResultType);
    }

    [Fact]
    public async Task Pending_current_server_is_named_when_there_are_several_servers()
    {
        var (check, _, root) = Create(s => { }, ("m3", "site2", "FE3/site2"));
        using var _r = root;

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.StartsWith("[WEB1/site1] Umbraco Logs folder: no measurement", statuses[0].Message);
    }

    [Fact]
    public async Task Local_temp_without_background_scanning_does_not_report_a_pass_it_cannot_evaluate()
    {
        using var root = new TempFolder();
        root.AddFile("tmp/x.bin", 1);
        var store = new InMemoryResultStore();
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true, BackgroundScan = { Enabled = false } });
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1", Path.Combine(root.Path, "tmp"));
        var disk = new FakeDiskReader();
        var check = new LocalTempFolderSizeHealthCheck(
            store, options, Build.Service(store, catalog, disk, options.Value), new StatsThresholds(options, catalog, disk),
            ServersBuild.Servers(store, catalog), catalog);

        var status = Assert.Single(await check.GetStatusAsync());

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Contains("could not be evaluated", status.Message);
        Assert.DoesNotContain("Within normal limits", status.Message);
    }

    [Fact]
    public async Task Unreadable_disk_on_one_server_does_not_hide_the_others()
    {
        using var root = new TempFolder();
        Directory.CreateDirectory(Path.Combine(root.Path, "wwwroot"));
        var inner = new InMemoryResultStore();
        var store = new ThrowingStore(inner, "stats:shared:site2:home-disk");
        var options = Options.Create(new CloudHealthChecksOptions { LocalTestMode = true });
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var disk = new FakeDiskReader { Usage = new DiskUsage(1000, 500) };
        var ids = new FakeMachineIds();
        ids.Ids.AddRange(new[] { "m1", "m3" });
        inner.Items[StatsServers.IdentityName("m3")] = new FolderSizeResult { MachineId = "m3", SiteKey = "site2", ServerName = "FE3/site2", CalculatedAt = DateTime.UtcNow };
        inner.Items["stats:instance:m3:local-disk"] = new FolderSizeResult { DiskTotalBytes = 1000, DiskFreeBytes = 900, CalculatedAt = DateTime.UtcNow };
        var check = new AzureStorageHealthCheck(
            options, new FakeHostEnvironment { ContentRootPath = root.Path }, disk, catalog,
            ServersBuild.Servers(store, catalog, ids, "WEB1/site1"),
            Build.Service(store, catalog, disk, options.Value), new StatsThresholds(options, catalog, disk));

        var statuses = (await check.GetStatusAsync()).ToList();

        Assert.Contains(statuses, s => s.Message.StartsWith("[site site2 (FE3/site2)] Error reading") && s.ResultType == StatusResultType.Error);
        Assert.Contains(statuses, s => s.Message.StartsWith("[FE3/site2]") && s.ResultType == StatusResultType.Success);
    }

    [Fact]
    public async Task Last_reported_time_comes_from_the_published_identity_not_from_the_call()
    {
        using var root = new TempFolder();
        var store = new InMemoryResultStore();
        var catalog = Build.TestModeCatalog(root.Path, "m1", "site1");
        var servers = ServersBuild.Servers(store, catalog, null, "WEB1/site1");

        Assert.Null((await servers.GetServersAsync())[0].LastReportedAt);

        var published = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        servers.UtcNow = () => published;
        await servers.PublishCurrentAsync();

        Assert.Equal(published, (await servers.GetServersAsync())[0].LastReportedAt);
    }
}

internal sealed class ThrowingStore : Umbraco.Community.Cloud.HealthChecks.Services.IHealthCheckResultStore
{
    private readonly InMemoryResultStore _inner;
    private readonly string _failingName;

    public ThrowingStore(InMemoryResultStore inner, string failingName)
    {
        _inner = inner;
        _failingName = failingName;
    }

    public Task StoreAsync(string healthCheckName, FolderSizeResult result) => _inner.StoreAsync(healthCheckName, result);

    public Task<FolderSizeResult?> GetAsync(string healthCheckName) =>
        healthCheckName == _failingName ? throw new InvalidOperationException("store down") : _inner.GetAsync(healthCheckName);
}
