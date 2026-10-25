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

## Releasing a shareable build

`scripts\release.ps1` builds the self-contained single exe into `dist\`; `-Publish` also creates the GitHub Release
`v<Version>` from the .csproj `<Version>` (bump it first); pass `-WhatsNew "<markdown>"` to describe the changes. The exe installs itself to
`%LOCALAPPDATA%\Programs\WheelOfAwesome` on first run (see `Services/Installer.cs`). Debug builds and `--dev` runs never
self-install unless given `--install-dir`.

## Testing

Use `--config-dir <scratch folder>` with every dev/test run so the user's real settings in `%APPDATA%\WheelOfAwesome`
are never touched. `--dev` skips start-up registration. See README "Development" for `--selftest`, `--render-ui`
and `--live-test-log`.
