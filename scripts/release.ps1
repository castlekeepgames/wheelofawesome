<#
  Builds the shareable WheelOfAwesome: one self-contained exe (the .NET runtime is bundled, so nothing else needs
  installing) in dist\. With -Publish, also creates a GitHub Release for the version in the .csproj.
  -WhatsNew adds a "What's new" section (Markdown) above the install steps.
#>
param([switch]$Publish, [string]$WhatsNew)
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$proj = Join-Path $root "src\WheelOfAwesome\WheelOfAwesome.csproj"
$version = (([xml](Get-Content $proj)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$out = Join-Path $root "dist"
$build = Join-Path $out "build"

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:SkipInstall=true -o $build
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$extra = Get-ChildItem $build | Where-Object Name -ne "WheelOfAwesome.exe"
if ($extra) { throw "Expected a single exe, but publish also produced: $($extra.Name -join ', ')" }

$asset = Join-Path $out "WheelOfAwesome-$version-win-x64.exe"
Move-Item (Join-Path $build "WheelOfAwesome.exe") $asset
Remove-Item -Recurse -Force $build
Write-Host ("Built {0} ({1:N1} MB)" -f $asset, ((Get-Item $asset).Length / 1MB))

if ($Publish) {
    $notes = Join-Path $out "release-notes.md"
    $whatsNewSection = if ($WhatsNew) { "## What's new`n`n$WhatsNew`n`n" } else { "" }
    @"
$whatsNewSection## Install

1. Download **WheelOfAwesome-$version-win-x64.exe** below and run it. Nothing else is needed (it works on Windows 10 and 11, 64-bit).
2. Windows may say *"Windows protected your PC"* because the app isn't code-signed. Click **More info** → **Run anyway**.
3. It installs itself for your user (no admin needed), starts with Windows, and opens its settings. You can delete the download afterwards.

Hold the **middle mouse button** anywhere to open the wheel. Click the tray icon to change shortcuts or the trigger.

To update, download a newer version and run it; it replaces the old one and keeps your shortcuts.
To uninstall, use **Settings → Apps → Installed apps → WheelOfAwesome**, or **General → Uninstall** in the app.
"@ | ForEach-Object { [IO.File]::WriteAllText($notes, $_, (New-Object System.Text.UTF8Encoding $false)) }  # no BOM: GitHub would show it

    gh release create "v$version" $asset --title "WheelOfAwesome $version" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}
