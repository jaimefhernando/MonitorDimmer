# Monitor Dimmer

Tray app for multi-monitor setups. When a game or video player goes fullscreen, every other
monitor is dimmed with a black, click-through overlay; when it closes, they fade back.

Successor to the `AutoDimmer.ahk` / `OverlayDimmer.ahk` AutoHotkey scripts; no AutoHotkey needed.

**Download:** `MonitorDimmer-win-Setup.exe` from the
[latest release](https://github.com/jaimefhernando/MonitorDimmer/releases/latest). It installs for
your user only (no admin rights) and keeps itself up to date. Windows 10 or 11, 64-bit.

## Features

- **Three triggers:** a watched app is fullscreen, any app is fullscreen, or a watched app is open at all.
- **Game detection:** games installed through Steam, Epic, GOG, Ubisoft, EA, Xbox or Riot, and games
  Windows Game Bar recognises, count as watched apps automatically.
- **RGB lighting:** lights off or a dim color while dimming, then back to normal. Razer Chroma
  directly; ASUS Aura, Corsair, Logitech, MSI and others through OpenRGB profiles.
- **Built-in actions:** pause Wallpaper Engine, hide Rainmeter skins, High performance power plan,
  close OneDrive; each is undone when dimming ends.
- **Game profiles:** per game darkness, dim method, extra monitors to keep bright (e.g. Discord's),
  lighting and actions. Anything not set follows the defaults.
- **Real brightness (DDC/CI):** turn down the monitor's own backlight instead of, or as well as, the
  overlay. Original levels are saved first and put back even after a crash.
- **Mouse lock:** keeps the cursor on the fullscreen game's monitor while the game is in focus;
  never overrides a game's own cursor lock.
- **Play stats:** time per game, sessions, longest session, the last 14 days; stored locally in
  `sessions.jsonl`.
- **Dark mode** (default), light mode or follow Windows.
- **Exceptions** (Discord, OBS...): never trigger dimming, and their windows are cut out of the
  overlay so they stay fully visible on a dimmed monitor.
- **Per-monitor settings:** never dim a monitor, or give it its own darkness. Monitors are
  recognised by device path, so settings survive reboots and port changes.
- **Hidden from capture:** the overlay is excluded from screenshots, OBS and screen sharing (optional).
- **Shortcut** (default Ctrl+Alt+D): dims every monitor except the one you are using.
- **Your own commands** on start/end, run through cmd.exe.
- Start with Windows, single instance (launching again opens the settings).

## Build and run

Requires the .NET 8 SDK.

```
dotnet build
dotnet run
```

Run with `--tray` to start without opening the settings window.

## Releases, installer and updates

Installing and updating use [Velopack](https://velopack.io). The `vpk` packaging tool is a
local .NET tool of this repository (`.config/dotnet-tools.json`).

```
.\scripts\release.ps1 -Version 0.2.3 -Publish
```

builds the release and publishes it on GitHub (using the `gh` CLI login); without `-Publish` it
only builds. Either way it writes to `releases\`:

- `MonitorDimmer-win-Setup.exe`: the installer. Per user, no admin rights; Start menu and
  desktop shortcuts and an entry in Windows' installed apps.
- `MonitorDimmer-win-Portable.zip`: runs without installing.
- `*-full.nupkg`, `*-delta.nupkg`, `releases.win.json`: what installed copies download to update.
  Keep the folder between releases; deltas are made against the previous version.

The installed app checks for updates 20 seconds after it starts and once a day, downloads them in
the background and installs them on the next restart (or right away with "Restart and update").
It looks at this repository's GitHub releases (`Updater.DefaultSource`); the `UpdateSource`
setting in `settings.json` overrides that for one PC, e.g. with a local `releases` folder.

The installer is not code-signed, so Windows SmartScreen warns the first time it is run.

`scripts\make-icon.ps1` regenerates `assets\app.ico`.

## Files

Settings and a small troubleshooting log live in `%AppData%\MonitorDimmer\`.

| Folder | Contents |
| --- | --- |
| `Core/` | Monitor and window scanning, game detection, the dimming controller, overlay windows, actions |
| `Core/Rgb/` | Razer Chroma and OpenRGB clients |
| `Themes/` | Dark and light palettes, control templates |
| `Interop/` | Win32 / DWM / DisplayConfig declarations |
| `Platform/` | Tray icon, global shortcut, startup entry, theme, mouse lock, updater |
| `UI/` | Settings window, profiles, stats, app list editor, running-app picker |
| `scripts/` | Release and icon scripts |
