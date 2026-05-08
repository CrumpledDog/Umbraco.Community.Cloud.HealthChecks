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
      "CacheExpirationHours": 720.0,
      "StorageMode": "KeyValue"
    },
    "AzureStorage": {
      "WarningThresholdPercentage": 75.0,
      "ErrorThresholdPercentage": 90.0
    },
    "NuGetCache": {
      "WarningThresholdMb": null,
      "ErrorThresholdMb": null
    },
    "UmbracoLogs": {
      "WarningThresholdMb": 250,
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

- **CacheExpirationHours** (default: `720.0`)  
  How long to cache scan results in hours (30 days). Acts as a safety net if background scanning is disabled. Since the background job updates the cache regularly, expiration is set to a very long duration to prevent expensive live scans.

- **StorageMode** (default: `"KeyValue"`)  
  Storage mode for health check results. Options:
  - `"KeyValue"` - Uses Umbraco's KeyValue database table for persistent storage. Suitable for deployments without a distributed cache provider. Data persists across application restarts.
  - `"DistributedCache"` - Uses IDistributedCache (in-memory or Redis) for storing health check results. Suitable for deployments with a configured distributed cache provider.

**How it works:**
- In single-server environments, the job runs on that server
- In load-balanced environments, only the elected SchedulingPublisher server runs the scan
- All servers read from the configured storage (KeyValue database or distributed cache) for instant results
- If cache expires before the next scan, a live scan is performed and cached for 30 minutes
- Health checks display "calculated X ago" timestamps showing data freshness

### Azure Storage Health Check

- **WarningThresholdPercentage** (default: `75.0`)  
  Warning threshold percentage for storage usage

- **ErrorThresholdPercentage** (default: `90.0`)  
  Error threshold percentage for storage usage

### NuGet Cache Health Check

- **WarningThresholdMb** (default: `null`)  
  Warning threshold in MB for NuGet cache size.  
  When `null` (default), automatically calculates as **75% of error threshold** (rounded to nearest GB).  
  Example: If error threshold is 10 GB, warning threshold becomes 8 GB.

- **ErrorThresholdMb** (default: `null`)  
  Error threshold in MB for NuGet cache size.  
  When `null` (default), automatically calculates as **50% of total home directory size** (rounded to nearest GB).  
  Example: If `C:\home` has 20 GB total capacity, error threshold becomes 10 GB.  
  
  **Intelligent Defaults:** These dynamic thresholds adapt to your Azure environment's actual storage capacity. In a load-balanced environment with 250 GB storage, you'd get ~125 GB error threshold instead of the old fixed 3 GB limit. You can still override with explicit values if needed.

### Umbraco Logs Health Check

- **WarningThresholdMb** (default: `250`)  
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
      "CacheExpirationHours": 24.0
    }
  }
}
```

This configuration:
- Scans folders every **5 minutes** (0.083 hours) instead of 6 hours
- Caches results for 24 hours instead of 30 days
- Enables local test mode to scan local folders

**Note**: The first background scan occurs after the `ScanIntervalHours` period (not immediately on startup). After that, scans repeat at the configured interval. You can watch for log messages:
```
Starting folder size background scan for {Count} health checks
Scanning folder for {HealthCheck}: {Path}
Cached folder size for {HealthCheck}: {Size} bytes, {Files} files
```

### Overriding NuGet Cache Intelligent Defaults

By default, NuGet cache thresholds are automatically calculated based on your `C:\home` directory size. To override with explicit values:

```json
{
  "CloudHealthChecks": {
    "NuGetCache": {
      "WarningThresholdMb": 5120,
      "ErrorThresholdMb": 8192
    }
  }
}
```

This sets:
- Warning threshold to 5 GB (5120 MB)
- Error threshold to 8 GB (8192 MB)

**When to override:**
- You want more conservative thresholds than the automatic 50%/75% calculation
- You have specific storage capacity constraints
- You need consistent thresholds across different environments

**Tip:** Set just the error threshold to use automatic calculation for warning (75% of error):

```json
{
  "CloudHealthChecks": {
    "NuGetCache": {
      "ErrorThresholdMb": 10240
    }
  }
}
```

This sets error to 10 GB and automatically calculates warning as ~8 GB.

### Storage Modes

The package supports two storage modes for health check results. By default, **KeyValue mode** is used, which stores results in Umbraco's database KeyValue table. This works out-of-the-box without requiring a distributed cache provider.

For deployments without a distributed cache provider (e.g., single-server environments without Redis), use KeyValue storage mode (the default):

```json
{
  "CloudHealthChecks": {
    "BackgroundScan": {
      "StorageMode": "KeyValue"
    }
  }
}
```

**Benefits of KeyValue mode (default):**
- No distributed cache provider required
- Results persist across application restarts
- Suitable for single-server or development environments
- Works out-of-the-box without additional configuration

**When to use DistributedCache mode:**
- Load-balanced environments with Redis or similar cache
- When you want automatic cache expiration
- For high-performance scenarios with frequent reads

**Example for DistributedCache mode:**
```json
{
  "CloudHealthChecks": {
    "BackgroundScan": {
      "StorageMode": "DistributedCache"
    }
  }
}
```
