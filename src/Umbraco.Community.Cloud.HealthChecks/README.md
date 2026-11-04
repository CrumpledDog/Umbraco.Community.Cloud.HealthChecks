# Umbraco Cloud Health Checks

![Cloud Health Checks](https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/src/Umbraco.Community.Cloud.HealthChecks/cloud-healthchecks.png)

A package that provides health checks for Umbraco Cloud environments, helping you monitor critical system resources and storage usage.

## Features

### Background Scanning

To prevent timeouts and performance issues, folder size health checks use a **background scanning** strategy:

- **Scheduled Scans**: Folders are scanned every 6 hours by default (configurable)
- **Distributed Cache**: Results are cached and shared across all servers in load-balanced environments
- **No Timeouts**: Health checks return instantly using cached data
- **No scans in requests**: until the first background measurement exists the check reports "no measurement yet" instead of walking the folder (with background scanning disabled it falls back to a live scan)
- **Timestamp Display**: Shows "calculated X ago" so you know data freshness

This approach is especially valuable for large folders (NuGet cache, logs) where scanning can take several seconds or minutes.

### Included Health Checks

- **Azure Storage Usage** - Monitors storage usage for `C:\home` and `C:\local` directories in Umbraco Cloud
  - Warning threshold at 75% usage
  - Error threshold at 90% usage
  - Windows-only support
  - Uses Windows API for instant results (no scanning required)

- **NuGet Cache Size** - Monitors the size of the NuGet cache directory
  - **Intelligent defaults**: Automatically calculates thresholds based on total home directory size (50% for error, 75% of error for warning)
  - Uses background scanning with distributed cache
  - Displays calculation timestamp
  
- **Umbraco Logs Folder Size** - Tracks the size of Umbraco log files
  - Uses background scanning with distributed cache
  - Displays calculation timestamp

### Hosting platform detection

The package works on any infrastructure:

- **Umbraco Cloud** (`Umbraco:Cloud:IsRunningOnCloud`, or an App Service with `Umbraco:Cloud:Identity:EnvironmentId` / `umbraco-cloud.json`) and **Azure Web Apps** (both `WEBSITE_SITE_NAME` and `WEBSITE_INSTANCE_ID` set): all checks apply. The health check group is named **Umbraco Cloud** or **Azure Web App** accordingly.
- **Anywhere else**: only the Umbraco Logs check applies (group **Hosting**).

`CloudHealthChecks:LocalTestMode` still makes every check run locally. The detected platform is available as `IStatsTargetCatalog.Platform`.

## Installation

Install via NuGet Package Manager or the .NET CLI:

```bash
dotnet add package Umbraco.Community.Cloud.HealthChecks
```

Or via NuGet Package Manager:

```bash
Install-Package Umbraco.Community.Cloud.HealthChecks
```

## Usage

After installation, the health checks will automatically appear in the Umbraco backoffice under:

**Settings → Health Check**

The health checks are grouped under **"Umbraco Cloud"** for easy identification.

![Health Check Results](https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/example.png)

## Configuration

All health check settings can be customized via `appsettings.json`. The package uses sensible defaults, but you can override them to suit your environment.

See the [Configuration Guide](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/blob/develop/v1/CONFIGURATION.md) for detailed configuration options and examples.

Quick example:

```json
{
  "CloudHealthChecks": {
    "BackgroundScan": {
      "Enabled": true,
      "ScanIntervalHours": 6.0,
      "CacheExpirationHours": 720.0
    },
    "UmbracoLogs": {
      "WarningThresholdMb": 200,
      "ErrorThresholdMb": 1000
    }
  }
}
```

### Background Scan Configuration

- **Enabled** (default: `true`) - Enable/disable background scanning
- **ScanIntervalHours** (default: `6.0`) - How often to scan folders (in hours)
- **CacheExpirationHours** (default: `720.0`) - How long to cache results (in hours / 30 days)

- **ScanTimeLimitMinutes** (default: `15.0`) - Time limit for scanning one folder. On overrun an error result ("took longer than the limit, will retry") is stored instead of a partial number

**Note**: Since 1.4.0 the refresh job runs on **every** server role (Single, SchedulingPublisher and Subscriber), so a split front-end web app's own disks are measured too. Folders on a disk shared by all instances of a web app (logs, NuGet cache) are measured once per site per interval using a best-effort claim marker in the result store (overlap is harmless); per-instance folders (local temp) and disk usage are measured by each server. Health checks always show the result for the server or site they run on.

## Stats API (for other packages)

Since 1.4.0 the package exposes its measurements so other packages (for example a clean-up tool) don't need to scan folders themselves. Everything is in the `Umbraco.Community.Cloud.HealthChecks.Stats` namespace and registered in DI.

**Scope model.** A monitored target is either `Shared` (one disk shared by all instances of a web app, e.g. `C:\home`: logs, NuGet cache) or `PerInstance` (local to each worker, e.g. `D:\local` temp). Shared results are keyed by `ServerKey.SiteKey` (`WEBSITE_SITE_NAME` plus `WEBSITE_SLOT_NAME`; the machine name off Azure); per-instance results by `ServerKey.MachineId` (`IMachineInfoFactory.GetMachineIdentifier()`). Azure App Service is detected when both `WEBSITE_SITE_NAME` and `WEBSITE_INSTANCE_ID` are set.

```csharp
public interface IStatsTargetCatalog
{
    IReadOnlyList<StatsTarget> Targets { get; }   // key, display name, resolved path, scope, kind, applies to this host
    ServerKey CurrentServer { get; }              // MachineId + SiteKey
    StatsTarget? Find(string key);
}

public interface IStatsService
{
    // Latest stored result for a server (default: this one). Never scans.
    Task<FolderSizeResult?> GetLatestAsync(string targetKey, ServerKey? server = null, CancellationToken ct = default);

    // Scan now (single pass, honours the time limit), store and return. Background callers only.
    Task<FolderSizeResult> MeasureAsync(string targetKey, TimeSpan? timeLimit = null, CancellationToken ct = default);

    // Publish a result (e.g. size left after a clean-up) without rescanning.
    Task RecordAsync(string targetKey, FolderSizeResult result, CancellationToken ct = default);
}

public interface IStatsThresholds
{
    TargetThresholds Resolve(string targetKey);   // configured values or the adaptive NuGet defaults
    ThresholdEvaluation Evaluate(string targetKey, FolderSizeResult? result, DateTime? nowUtc = null); // Level None/Ok/Warning/Error + reasons
}

public interface IDiskUsageReader
{
    DiskUsage? Read(string path);                 // GetDiskFreeSpaceEx on the path (quota-aware); null off Windows
}
```

Target keys are in `StatsTargetKeys`: `umbraco-logs`, `nuget-cache`, `local-temp` (folders) and `home-disk`, `local-disk`, `temp-disk` (disks). `FolderSizeResult` gained `TargetKey`, `MachineId`, `SiteKey`, `DiskTotalBytes`, `DiskFreeBytes` and `DiskUsedPercent`; older stored results still deserialize.

Notes:

- `MeasureAsync` walks the folder, so call it from background code only, never from a request.
- Disk targets on the current server are read live by `GetLatestAsync` (instant); for other servers the numbers last stored by that server's job are returned.
- `LocalTestMode` makes the Azure targets resolve to local paths so the API can be exercised off Azure.
- Results from 1.3.x are still read, and the 1.3.x cache keys are still written for the shared logs and NuGet targets, so a mixed-version fleet keeps working during a rollout.

## Requirements

- Umbraco CMS v17+

## Proactive Monitoring

To get the most value from these health checks, set up automated monitoring using Umbraco's built-in notification system or third-party integrations.

### Built-in Email Notifications

Umbraco includes a built-in email notification system for health checks. Configure it in **Settings → Health Check** to receive email alerts when issues are detected. See the [official documentation](https://docs.umbraco.com/umbraco-cms/reference/configuration/healthchecks#notification) for setup instructions. This allows you to:
- Get early warnings when storage reaches 75% capacity
- Receive critical alerts at 90% usage before your site runs out of space
- Monitor log file growth and aging
- Take proactive action to prevent downtime

### Slack Notifications

For real-time team alerts, combine this package with the [Health Check Slack Notification Method](https://marketplace.umbraco.com/package/our.umbraco.healthcheckslacknotificationmethod) package. This sends automated Slack messages to your team channel when storage thresholds are exceeded.

Both notification methods are especially valuable in Umbraco Cloud environments where storage space is limited and can impact site availability.

## Support

For issues, questions, or feature requests, please visit the [GitHub repository](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks).

## Credits

Developed by **Crumpled Dog** and the **Umbraco Community**.

## License

This project is licensed under the terms specified in the LICENSE file.
