using Shadeplay.Core.Rgb;

namespace Shadeplay.Core;

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
    string OnRestoreCommands,
    bool SideDisplay)
{
    /// <summary>The normal settings, then the scene, then the game's profile on top.</summary>
    public static SessionPlan Resolve(AppSettings s, GameProfile? p, Scene? scene = null)
    {
        var rgb = RgbLighting.Options.From(s);
        bool rgbOn = s.RgbEnabled;
        if (scene != null) (rgbOn, rgb) = ApplyLighting(rgbOn, rgb, scene.Lighting, scene.RgbColor, scene.RgbBrightness);
        if (p != null) (rgbOn, rgb) = ApplyLighting(rgbOn, rgb, p.Lighting, p.RgbColor, p.RgbBrightness);

        var method = ToMethod(p?.Method ?? ProfileDimMethod.Default) ?? ToMethod(scene?.Method ?? ProfileDimMethod.Default) ?? s.DimMethod;
        bool actions = p?.CustomActions == true;
        return new SessionPlan(
            Strength: p?.CustomStrength == true ? p.Strength : scene?.CustomStrength == true ? scene.Strength : null,
            Method: method,
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
            MouseLock: Switch(p?.MouseLock) ?? Switch(scene?.MouseLock) ?? s.LockMouse,
            OnDimCommands: s.OnDimCommands,
            OnRestoreCommands: s.OnRestoreCommands,
            SideDisplay: Switch(scene?.SideDisplay) ?? s.SideDisplayEnabled);
    }

    static (bool On, RgbLighting.Options Options) ApplyLighting(bool on, RgbLighting.Options rgb,
        ProfileLighting lighting, string color, int brightness) => lighting switch
    {
        ProfileLighting.Unchanged => (false, rgb),
        ProfileLighting.Off => (true, rgb with { Style = RgbStyle.Off }),
        ProfileLighting.Color => (true, rgb with { Style = RgbStyle.Color, Color = color, Brightness = brightness }),
        _ => (on, rgb),
    };

    static DimMethod? ToMethod(ProfileDimMethod m) => m switch
    {
        ProfileDimMethod.Overlay => DimMethod.Overlay,
        ProfileDimMethod.Brightness => DimMethod.Brightness,
        ProfileDimMethod.Both => DimMethod.Both,
        _ => null,
    };

    static bool? Switch(ProfileSwitch? s) => s switch
    {
        ProfileSwitch.On => true,
        ProfileSwitch.Off => false,
        _ => null,
    };
}
