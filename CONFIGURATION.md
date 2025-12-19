# Configuration

## appsettings.json Example

You can configure the thresholds for each health check in your `appsettings.json` file. All settings are optional and will use the default values if not specified.

```json
{
  "CloudHealthChecks": {
    "LocalTestMode": false,
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
