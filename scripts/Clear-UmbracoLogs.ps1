# Clear old Umbraco log files
# Usage: .\Clear-UmbracoLogs.ps1 -DaysToKeep 30

param(
    [Parameter(Mandatory=$false)]
    [int]$DaysToKeep = 30
)

$logsPath = Join-Path $PSScriptRoot "..\umbraco\Logs"

if (-not (Test-Path $logsPath)) {
    Write-Warning "Logs folder not found at: $logsPath"
    exit 1
}

$cutoffDate = (Get-Date).AddDays(-$DaysToKeep)
$files = Get-ChildItem -Path $logsPath -File -Recurse | Where-Object { $_.LastWriteTime -lt $cutoffDate }

if ($files.Count -eq 0) {
    Write-Host "No log files older than $DaysToKeep days found."
    exit 0
}

$totalSize = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Found $($files.Count) log files older than $DaysToKeep days (Total: $($totalSize.ToString('F2')) MB)"

$files | Remove-Item -Force
Write-Host "Successfully removed $($files.Count) old log files."
