# Cleanup Scripts

This folder contains PowerShell scripts to help maintain Umbraco Cloud environments.

<a name="logs"></a>
## Clear-UmbracoLogs.ps1

Removes old Umbraco log files to free up disk space.

**Usage:**

Download and run with default 30 days retention:
```powershell
$url = 'https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-UmbracoLogs.ps1'
irm $url | iex
```

To specify custom days to keep (e.g., 300 days):
```powershell
$url = 'https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-UmbracoLogs.ps1'
$DaysToKeep = 300
irm $url | iex
```

**Parameters:**
- `DaysToKeep` (optional, default: 30) - Number of days of logs to retain

<a name="nuget"></a>
## Clear-NuGetCache.ps1

Clears the NuGet package cache to free up disk space.

**Usage:**
```powershell
# Download and run
$url = 'https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-NuGetCache.ps1'
irm $url | iex
```

## Running in Umbraco Cloud

You can run these scripts in the Kudu console (Advanced Tools > Debug Console > PowerShell):

1. Open the PowerShell tab in Kudu
2. Run the one-liner command shown above (works from any directory)
3. The script will automatically detect the Azure environment and clean up the files, showing you how much space was freed

## Security Note

These scripts use `Invoke-RestMethod` (irm) to download and `Invoke-Expression` (iex) to execute. Always review scripts before running them, especially with `iex`. These scripts are hosted in this repository and you can review them before use.
