# Cleanup Scripts

This folder contains PowerShell scripts to help maintain Umbraco Cloud environments.

<a name="logs"></a>
## Clear-UmbracoLogs.ps1

Removes old Umbraco log files to free up disk space.

**Usage:**
```powershell
# Download and run in one line (PowerShell 7+)
irm https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-UmbracoLogs.ps1 | iex

# Or specify how many days to keep
irm https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-UmbracoLogs.ps1 | iex -Args 30
```

**Parameters:**
- `DaysToKeep` (optional, default: 30) - Number of days of logs to retain

<a name="nuget"></a>
## Clear-NuGetCache.ps1

Clears the NuGet package cache to free up disk space.

**Usage:**
```powershell
# Download and run in one line
irm https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/scripts/Clear-NuGetCache.ps1 | iex
```

## Running in Umbraco Cloud

You can run these scripts in the Kudu console (Advanced Tools > Debug Console > PowerShell):

1. Navigate to your site root: `cd D:\home\site\wwwroot`
2. Run the one-liner command shown above
3. The script will clean up the files and show you how much space was freed

## Security Note

These scripts use `Invoke-RestMethod` (irm) to download and `Invoke-Expression` (iex) to execute. Always review scripts before running them, especially with `iex`. These scripts are hosted in this repository and you can review them before use.
