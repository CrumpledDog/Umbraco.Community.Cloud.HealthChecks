# Clear NuGet cache folders
# Usage: .\Clear-NuGetCache.ps1

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

# Determine the NuGet cache path
if ($env:NUGET_PACKAGES) {
    # Use the official NuGet packages environment variable
    $nugetPath = $env:NUGET_PACKAGES
} elseif ($env:HOME) {
    # Running in Azure/Kudu with HOME set
    $nugetPath = Join-Path $env:HOME ".nuget"
} elseif ($env:WEBSITE_SITE_NAME) {
    # Running in Azure but HOME not set - use Umbraco Cloud default
    $nugetPath = "C:\home\.nuget"
} else {
    # Running locally - use user profile
    $nugetPath = Join-Path $env:USERPROFILE ".nuget\packages"
}

if (-not (Test-Path $nugetPath)) {
    Write-Warning "NuGet cache folder not found at: $nugetPath"
    Write-Message "Tip: Set the NUGET_PACKAGES environment variable to specify the cache location"
    exit 1
}

$sizeBeforeMB = (Get-ChildItem -Path $nugetPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Message "Clearing NuGet cache at: $nugetPath"
Write-Message "Current size: $($sizeBeforeMB.ToString('F2')) MB"

# Use dotnet CLI to clear the cache properly
dotnet nuget locals all --clear

$sizeAfterMB = 0
if (Test-Path $nugetPath) {
    $sizeAfterMB = (Get-ChildItem -Path $nugetPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
}

$freedMB = $sizeBeforeMB - $sizeAfterMB
Write-Message "Successfully cleared cache. Freed: $($freedMB.ToString('F2')) MB)"
