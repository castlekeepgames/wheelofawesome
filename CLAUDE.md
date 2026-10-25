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

## Testing

Use `--config-dir <scratch folder>` with every dev/test run so the user's real settings in `%APPDATA%\WheelOfAwesome`
are never touched. `--dev` skips start-up registration. See README "Development" for `--selftest`, `--render-ui`
and `--live-test-log`.
