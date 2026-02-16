# Clear old Umbraco log files
# Usage: .\Clear-UmbracoLogs.ps1 -DaysToKeep 30

param(
    [Parameter(Mandatory=$false)]
    [int]$DaysToKeep = 30
)

# Safe output function for Kudu console compatibility
function Write-Message {
    param([string]$Message)
    try {
        Write-Host $Message
    } catch {
        # Fall back to Write-Output if Write-Host fails (common in Kudu)
        Write-Output $Message
    }
}

# Determine the logs path
if ($env:HOME) {
    # Running in Azure/Kudu with HOME environment variable set
    $logsPath = Join-Path $env:HOME "site\wwwroot\umbraco\Logs"
} elseif ($env:WEBSITE_SITE_NAME) {
    # Running in Azure but HOME not set - use Umbraco Cloud default
    $logsPath = "C:\home\site\wwwroot\umbraco\Logs"
} elseif ($PSScriptRoot) {
    # Running from file - use relative path
    $logsPath = Join-Path $PSScriptRoot "..\umbraco\Logs"
} else {
    # Running via iex locally - try current directory
    $logsPath = Join-Path (Get-Location) "umbraco\Logs"
}

if (-not (Test-Path $logsPath)) {
    Write-Warning "Logs folder not found at: $logsPath"
    Write-Message "Tip: Run this from your Umbraco site root or in Azure Kudu"
    exit 1
}

$cutoffDate = (Get-Date).AddDays(-$DaysToKeep)
$files = Get-ChildItem -Path $logsPath -File -Recurse | Where-Object { $_.LastWriteTime -lt $cutoffDate }

if ($files.Count -eq 0) {
    Write-Message "No log files older than $DaysToKeep days found."
    exit 0
}

$totalSize = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Message "Found $($files.Count) log files older than $DaysToKeep days (Total: $($totalSize.ToString('F2')) MB)"

$files | Remove-Item -Force
Write-Message "Successfully removed $($files.Count) old log files."
