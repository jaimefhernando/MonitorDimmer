# Shadeplay

![Shadeplay](assets/branding/shadeplay-banner.png)

Tray app for multi-monitor setups. When a game or video player goes fullscreen, every other
monitor is dimmed with a black, click-through overlay; when it closes, they fade back.

Successor to the `AutoDimmer.ahk` / `OverlayDimmer.ahk` AutoHotkey scripts; no AutoHotkey needed.

**Download:** `Shadeplay-win-Setup.exe` from the
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
- **Game art:** recent games and profiles shown with their covers, and the game you're playing on Home.
  Steam games use the art Steam already keeps on your PC. For other games, add your own free
  SteamGridDB key in Settings (optional, personal use).
- **Real brightness (DDC/CI):** turn down the monitor's own backlight instead of, or as well as, the
  overlay, in a short eased fade with at least 100 ms between monitor commands. Original levels are
  saved first and put back even after a crash. Dimmed monitors can also lower contrast and
  saturation where they support it, and OSD-safe mode sends commands more slowly for monitors that
  freeze or flash their menu on quick changes.
- **OLED protection:** slowly drifts a faint lighter band across the black overlay, so no pixel sits
  at the same level for hours.
- **Mouse lock:** keeps the cursor on the fullscreen game's monitor while the game is in focus;
  never overrides a game's own cursor lock.
- **Idle dimming:** darkens a monitor the mouse and active window have left alone for a few minutes
  (burn-in protection), and wakes it as soon as the mouse moves onto it.
- **Night mode:** a warm, dimmer tint on side monitors (or all) on a schedule; on/off from the tray.
- **Focus mode:** outside games, dims everything except the window you are working in, on every
  monitor, optionally following the window under the mouse; also in the tray menu.
- **Isolate shortcut** (Ctrl+Alt+I): turns everything pitch-black except the window in front,
  lighting included. Press it again to undo.
- **Keep videos lit:** recognised media players and browser windows with video-service titles stay
  lit on a dimmed monitor.
- **Peek:** a title change briefly reveals that window at half brightness (overlay dimming only).
  Best effort, since not every app changes its title for a new message.
- **Display zones:** split a monitor left/right or top/bottom. During a game or manual dim, the
  cursor's zone stays bright and the display's other zones are dimmed.
- **Creator mode:** detects streaming and recording through OBS WebSocket v5 or Streamlabs Desktop,
  shows it on Home, and can lock the cursor to the active monitor and silence Shadeplay's
  notifications while live. The OBS password is stored encrypted for your Windows account.
- **Game mode:** raises the game's priority and closes listed apps (launchers, updaters) while
  playing, reopening them afterwards. The launcher that started the game is never closed.
- **Tray:** pause for 30 min / 1 h / 2 h, night mode, focus mode, dim now.
- **Settings backup:** export and import all settings, profiles and lists.
- **Welcome screen:** on first run, shows the monitors, games, Razer, Wallpaper Engine and
  Rainmeter it found and suggests matching settings.
- **Notifications:** "Dimming for Elden Ring", how long you played after longer sessions, and when
  an update is ready; each can be turned off.
- **Play stats:** time per game, sessions, longest session, the last 14 days; stored locally in
  `sessions.jsonl`.
- **Graphite and amber design:** a sidebar with Home, Games, Monitors, Lighting, Automations, Stats
  and Settings, under a themed title bar; the Home page shows what is dimmed right now on a
  to-scale map of your monitors. Dark (default), light or follow Windows. Uses the Exo 2 font (SIL Open Font License,
  `assets/fonts/OFL.txt`).
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
.\scripts\release.ps1 -Version 0.9.3 -Publish
```

builds the release and publishes it on GitHub (using the `gh` CLI login); without `-Publish` it
only builds. Either way it writes to `releases\`:

- `Shadeplay-win-Setup.exe`: the installer. Per user, no admin rights; Start menu and
  desktop shortcuts and an entry in Windows' installed apps.
- `Shadeplay-win-Portable.zip`: runs without installing.
- `*-full.nupkg`, `*-delta.nupkg`, `releases.win.json`: what installed copies download to update.
  Keep the folder between releases; deltas are made against the previous version.

The installed app checks for updates 20 seconds after it starts and once a day, downloads them in
the background and installs them on the next restart (or right away with "Restart and update").
It looks at this repository's GitHub releases (`Updater.DefaultSource`); the `UpdateSource`
setting in `settings.json` overrides that for one PC, e.g. with a local `releases` folder.

The installer is not code-signed, so Windows SmartScreen warns the first time it is run.

`scripts\make-icon.ps1` regenerates `assets\app.ico`.

## Files

Settings and a small troubleshooting log live in `%AppData%\Shadeplay\`.

| Folder | Contents |
| --- | --- |
| `Core/` | Monitor and window scanning, game detection, the dimming controller, overlay windows, actions |
| `Core/Rgb/` | Razer Chroma and OpenRGB clients |
| `Themes/` | Dark and light palettes, control templates |
| `Interop/` | Win32 / DWM / DisplayConfig declarations |
| `Platform/` | Tray icon, global shortcut, startup entry, theme, mouse lock, updater, OBS and Streamlabs sync |
| `UI/` | Settings window, Home, profiles, stats, welcome screen, app list editor, running-app picker |
| `scripts/` | Release and icon scripts |

## License

[MIT](LICENSE): free to use, change and share, including commercially, as long as the copyright
notice stays with it. No warranty.
