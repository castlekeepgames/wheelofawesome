# WheelOfAwesome

Windows radial quick-launcher, C# / .NET 10 WPF. See README.md for features and layout.

## After changing the app

Always finish a change by publishing, which installs the update on this PC:

```
cd src\WheelOfAwesome
dotnet publish -c Release -o ..\..\publish
```

Publishing runs `scripts\install.ps1`, which tells the running copy to close (`--exit`), replaces
`%LOCALAPPDATA%\Programs\WheelOfAwesome\WheelOfAwesome.exe`, and starts it again. Windows start-up points at that
installed exe. Use `-p:SkipInstall=true` to publish without installing.

## Never start the installed app as a child of this shell

The Claude desktop app is a packaged (MSIX) app. Programs launched from its shell have their `%APPDATA%` and HKCU
writes silently redirected into `%LOCALAPPDATA%\Packages\Claude_*\LocalCache` / a private hive, and reads from this
shell see that redirected view. An app started that way uses different settings from the user's, and anything it
registers (Run entries, uninstall info) is invisible to Windows. So:

- Start the installed app with `Start-ScheduledTask WheelOfAwesome` (or `explorer.exe "<exe>"`), never `Start-Process`.
- `--exit` is fine to send from here (named events aren't redirected).
- To see the user's *real* settings or registry, run the check via a temporary scheduled task that writes its output
  to the scratchpad. Dev/test runs with `--config-dir` in the scratchpad are unaffected.

## Releasing a shareable build

`scripts\release.ps1` builds the self-contained single exe into `dist\`; `-Publish` also creates the GitHub Release
`v<Version>` from the .csproj `<Version>` (bump it first); pass `-WhatsNew "<markdown>"` to describe the changes. The exe installs itself to
`%LOCALAPPDATA%\Programs\WheelOfAwesome` on first run (see `Services/Installer.cs`). Debug builds and `--dev` runs never
self-install unless given `--install-dir`.

## Testing

Use `--config-dir <scratch folder>` with every dev/test run so the user's real settings in `%APPDATA%\WheelOfAwesome`
are never touched. `--dev` skips start-up registration. See README "Development" for `--selftest`, `--render-ui`
and `--live-test-log`.
