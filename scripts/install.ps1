<#
  Installs (or updates) WheelOfAwesome for the current user:
  asks the running copy to save and quit, replaces the exe in %LOCALAPPDATA%\Programs\WheelOfAwesome, and starts it.
  Runs automatically after `dotnet publish -c Release` (skip with -p:SkipInstall=true).
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\publish\WheelOfAwesome.exe")
)
$ErrorActionPreference = "Stop"

$Source = (Resolve-Path $Source).Path
$dest = Join-Path $env:LOCALAPPDATA "Programs\WheelOfAwesome"
$target = Join-Path $dest "WheelOfAwesome.exe"
New-Item -ItemType Directory -Force $dest | Out-Null

function Get-Installed {
    # Only the installed copy; dev builds running from bin\ are left alone.
    Get-Process WheelOfAwesome -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }
}

if (Test-Path $target) {
    if (Get-Installed) {
        Write-Host "WheelOfAwesome: asking the running copy to close..."
        Start-Process $target -ArgumentList "--exit" -Wait
        for ($i = 0; $i -lt 50 -and (Get-Installed); $i++) { Start-Sleep -Milliseconds 100 }
        # Older builds don't understand --exit.
        Get-Installed | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

$copied = $false
for ($i = 0; $i -lt 20 -and -not $copied; $i++) {
    try { Copy-Item $Source $target -Force; $copied = $true }
    catch { Start-Sleep -Milliseconds 250 }
}
if (-not $copied) {
    throw "WheelOfAwesome: couldn't replace $target. It may be running as administrator; exit it from the tray menu and publish again."
}

# Start it through Windows rather than as our child: when this script runs from inside a packaged app (like the
# Claude desktop app), children inherit that app's sandbox and their AppData/registry writes get redirected.
if (Get-ScheduledTask -TaskName "WheelOfAwesome" -ErrorAction SilentlyContinue) {
    Start-ScheduledTask -TaskName "WheelOfAwesome"
} else {
    Start-Process explorer.exe -ArgumentList "`"$target`""
}
Write-Host "WheelOfAwesome: installed and started $target"
