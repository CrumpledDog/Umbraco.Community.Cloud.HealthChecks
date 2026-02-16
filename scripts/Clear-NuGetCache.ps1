# Clear NuGet cache folders
# Usage: .\Clear-NuGetCache.ps1

# Determine the NuGet cache path
if ($env:WEBSITE_SITE_NAME) {
    # Running in Azure - use Azure path
    $nugetPath = "C:\home\.nuget"
} else {
    # Running locally - use user profile
    $nugetPath = Join-Path $env:USERPROFILE ".nuget\packages"
}

if (-not (Test-Path $nugetPath)) {
    Write-Warning "NuGet cache folder not found at: $nugetPath"
    exit 1
}

$sizeBeforeMB = (Get-ChildItem -Path $nugetPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host "Clearing NuGet cache at: $nugetPath"
Write-Host "Current size: $($sizeBeforeMB.ToString('F2')) MB"

# Use dotnet CLI to clear the cache properly
dotnet nuget locals all --clear

$sizeAfterMB = 0
if (Test-Path $nugetPath) {
    $sizeAfterMB = (Get-ChildItem -Path $nugetPath -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
}

$freedMB = $sizeBeforeMB - $sizeAfterMB
Write-Host "Successfully cleared cache. Freed: $($freedMB.ToString('F2')) MB"
