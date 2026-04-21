# Configuration

## appsettings.json Example

You can configure the thresholds for each health check in your `appsettings.json` file. All settings are optional and will use the default values if not specified.

```json
{
  "CloudHealthChecks": {
    "LocalTestMode": false,
    "BackgroundScan": {
      "Enabled": true,
      "ScanIntervalHours": 6.0,
      "CacheExpirationHours": 12.0
    },
    "AzureStorage": {
      "WarningThresholdPercentage": 75.0,
      "ErrorThresholdPercentage": 90.0
    },
    "NuGetCache": {
      "WarningThresholdMb": 2560,
      "ErrorThresholdMb": 3072
    },
    "UmbracoLogs": {
      "WarningThresholdMb": 100,
      "ErrorThresholdMb": 500,
      "FileAgeWarningThresholdDays": 550,
      "FileAgeErrorThresholdDays": 730
    }
  }
}
```

## Configuration Options

### Global Options

- **LocalTestMode** (default: `false`)  
  Enable local test mode for testing health checks outside of Azure environment.  
  When enabled:
  - **Azure Storage Check** uses application root and wwwroot folders instead of `C:\home` and `C:\local`
  - **NuGet Cache Check** uses `%USERPROFILE%\.nuget\packages` instead of `C:\home\.nuget`
  - Both checks will run even when not in Azure environment
  
  **Usage:** Set to `true` in your local development appsettings.Development.json to test the health checks.

### Background Scan Options

- **Enabled** (default: `true`)  
  Enable background folder size scanning. When enabled, folders are scanned periodically in the background and results are cached using distributed cache.

- **ScanIntervalHours** (default: `6.0`)  
  How often to scan folders in hours. The background job runs on the elected SchedulingPublisher server in load-balanced environments.

- **CacheExpirationHours** (default: `12.0`)  
  How long to cache scan results in hours. Results are stored in distributed cache and shared across all servers.

**How it works:**
- In single-server environments, the job runs on that server
- In load-balanced environments, only the elected SchedulingPublisher server runs the scan
- All servers read from the shared distributed cache for instant results
- If cache expires before the next scan, a live scan is performed and cached for 30 minutes
- Health checks display "calculated X ago" timestamps showing data freshness

### Azure Storage Health Check

- **WarningThresholdPercentage** (default: `75.0`)  
  Warning threshold percentage for storage usage

- **ErrorThresholdPercentage** (default: `90.0`)  
  Error threshold percentage for storage usage

### NuGet Cache Health Check

- **WarningThresholdMb** (default: `2560`)  
  Warning threshold in MB for NuGet cache size (2.5 GB)

- **ErrorThresholdMb** (default: `3072`)  
  Error threshold in MB for NuGet cache size (3 GB)

### Umbraco Logs Health Check

- **WarningThresholdMb** (default: `100`)  
  Warning threshold in MB for logs folder size

- **ErrorThresholdMb** (default: `500`)  
  Error threshold in MB for logs folder size

- **FileAgeWarningThresholdDays** (default: `550`)  
  Warning threshold in days for old log files

- **FileAgeErrorThresholdDays** (default: `730`)  
  Error threshold in days for old log files

## Example Configurations

### Local Development Testing

To test Azure and NuGet health checks on your local machine, add to `appsettings.Development.json`:

```json
{
  "CloudHealthChecks": {
    "LocalTestMode": true
  }
}
```

This will enable the Azure Storage and NuGet Cache checks using local paths for testing.

### Testing Background Scans

To test the background scanning feature locally with faster intervals, add to `appsettings.Development.json`:

```json
{
  "CloudHealthChecks": {
    "LocalTestMode": true,
    "BackgroundScan": {
      "Enabled": true,
      "ScanIntervalHours": 0.083,
      "CacheExpirationHours": 1.0
    }
  }
}
```

This configuration:
- Scans folders every **5 minutes** (0.083 hours) instead of 6 hours
- Caches results for 1 hour instead of 12 hours
- Enables local test mode to scan local folders

**Note**: The first background scan occurs after the `ScanIntervalHours` period (not immediately on startup). After that, scans repeat at the configured interval. You can watch for log messages:
```
Starting folder size background scan for {Count} health checks
Scanning folder for {HealthCheck}: {Path}
Cached folder size for {HealthCheck}: {Size} bytes, {Files} files
```
