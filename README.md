# WheelOfAwesome

A radial quick-launch menu for Windows. Hold a mouse button or key, flick toward a slice, and release to run it.

## Run it

```
publish\WheelOfAwesome.exe
```

It runs in the system tray. Click the tray icon to open settings, or right-click it to pause or exit. On first run it opens the settings window with a starter wheel and registers itself to start with Windows (you can turn that off under **General**). If you move the exe, start it once from its new location and the start-up entry follows.

It needs the .NET 10 Desktop Runtime, which comes with the .NET 10 SDK. To build a copy that runs on a PC without .NET, add `--self-contained true -p:EnableCompressionInSingleFile=true` to the publish command (about 70 MB).

## Using the wheel

- **Open:** hold the trigger (the middle mouse button by default) for 150 ms. A quicker tap still does the button's normal job.
- **Pick:** move toward a slice (you don't have to reach it), then release the trigger or left-click.
- **Cancel:** release in the centre, right-click, or press Esc.
- If the cursor is near a screen edge, the wheel shifts onto the screen and moves the cursor to its centre.

## Shortcut types

| Type | What it does |
| --- | --- |
| Link | Opens the URL as a new tab in your current Chrome window, or in the default browser if Chrome isn't installed. The icon is fetched from the site. |
| App / file | Opens an app from the Start menu list (including Microsoft Store apps), or any exe, shortcut, document or folder. With "switch to it", an app that's already open is brought to the front. Choosing it again cycles through its windows. |
| Key macro | Runs a sequence of key combos, waits and typed text in whatever app was in front when you opened the wheel. |

Icons are automatic (the site's or app's own icon), any of the ~2,200 bundled [Lucide](https://lucide.dev) icons in your choice of colour, or your own PNG/JPG/ICO/SVG.

## Things to know

- **Admin apps:** Windows stops normal apps from seeing or sending input to apps running as administrator. Turn on **General → Run as administrator** if you need the wheel over those. It then starts at sign-in through a scheduled task, without a permission prompt.
- **Ctrl+Alt+Del** can't be sent by any app.
- **Games:** some games with anti-cheat ignore simulated keys.

## Files

Settings, the cached icons and a log are kept in `%APPDATA%\WheelOfAwesome\`:

- `config.json`: settings and shortcuts
- `icons\`: cached and custom icons
- `log.txt`: the log

## Development

```
cd src\WheelOfAwesome
dotnet build
dotnet publish -c Release -o ..\..\publish
```

A Release publish also installs the build for your user and restarts it (via `scripts\install.ps1`): the running copy is asked to close, `%LOCALAPPDATA%\Programs\WheelOfAwesome\WheelOfAwesome.exe` is replaced, and the new one starts. Add `-p:SkipInstall=true` to only publish.

All developer modes use `--config-dir <folder>` so they never touch your real settings:

| Flag | What it does |
| --- | --- |
| `--dev` | Doesn't register start-up and can run beside your normal copy. |
| `--selftest <report.txt>` | Tests the trigger logic, config, icon library, installed-app list, shell icons, Chrome lookup and favicon fetching. |
| `--render-ui <folder>` | Renders the wheel, every settings page and both pickers to PNGs without showing them on screen. |
| `--live-test-log <file>` | Logs each fired slice and passed-through tap, for scripted end-to-end tests that inject real input. |

### Layout

| Folder | Contents |
| --- | --- |
| `Input/` | Global low-level hooks on their own thread, the hold/tap state machine (`TriggerEngine`), SendInput |
| `Wheel/` | Wheel rendering, the click-through no-focus overlay, and the bridge from the hook thread to the UI |
| `Actions/` | Chrome links, app launch/focus, macros |
| `Icons/` | Lucide font, favicon fetcher, shell icon extraction, image/SVG loading |
| `UI/` | Settings window, icon picker, installed-app picker, hotkey capture box |
| `Services/` | Config, paths, log, tray, start-up, elevation |
