using MonitorDimmer.Core.Rgb;

namespace MonitorDimmer.Core;

/// <summary>
/// What one dimming session does: the defaults with the game's profile laid over them.
/// </summary>
internal sealed record SessionPlan(
    int? Strength,
    DimMethod Method,
    IReadOnlySet<string> KeepBrightMonitorIds,
    bool Rgb,
    RgbLighting.Options RgbOptions,
    bool WallpaperEngine,
    bool Rainmeter,
    bool Power,
    bool OneDrive,
    bool BoostPriority,
    IReadOnlyList<string> CloseApps,
    bool ReopenClosedApps,
    bool MouseLock,
    string OnDimCommands,
    string OnRestoreCommands)
{
    public static SessionPlan Resolve(AppSettings s, GameProfile? p)
    {
        var rgb = RgbLighting.Options.From(s);
        bool rgbOn = s.RgbEnabled;
        switch (p?.Lighting)
        {
            case ProfileLighting.Unchanged:
                rgbOn = false;
                break;
            case ProfileLighting.Off:
                rgbOn = true;
                rgb = rgb with { Style = RgbStyle.Off };
                break;
            case ProfileLighting.Color:
                rgbOn = true;
                rgb = rgb with { Style = RgbStyle.Color, Color = p.RgbColor, Brightness = p.RgbBrightness };
                break;
        }

        bool actions = p?.CustomActions == true;
        return new SessionPlan(
            Strength: p?.CustomStrength == true ? p.Strength : null,
            Method: p?.Method switch
            {
                ProfileDimMethod.Overlay => DimMethod.Overlay,
                ProfileDimMethod.Brightness => DimMethod.Brightness,
                ProfileDimMethod.Both => DimMethod.Both,
                _ => s.DimMethod,
            },
            KeepBrightMonitorIds: p?.KeepBrightMonitorIds.ToHashSet() ?? new HashSet<string>(),
            Rgb: rgbOn,
            RgbOptions: rgb,
            WallpaperEngine: actions ? p!.PauseWallpaperEngine : s.PauseWallpaperEngine,
            Rainmeter: actions ? p!.HideRainmeter : s.HideRainmeter,
            Power: actions ? p!.HighPerformancePower : s.HighPerformancePower,
            OneDrive: actions ? p!.CloseOneDrive : s.CloseOneDrive,
            BoostPriority: actions ? p!.BoostPriority : s.BoostPriority,
            CloseApps: actions && !p!.CloseApps
                ? Array.Empty<string>()
                : s.CloseApps.Select(WindowScanner.NormalizeExe).Where(n => n.Length > 0).ToList(),
            ReopenClosedApps: s.ReopenClosedApps,
            MouseLock: p?.MouseLock switch
            {
                ProfileSwitch.On => true,
                ProfileSwitch.Off => false,
                _ => s.LockMouse,
            },
            OnDimCommands: s.OnDimCommands,
            OnRestoreCommands: s.OnRestoreCommands);
    }
}
