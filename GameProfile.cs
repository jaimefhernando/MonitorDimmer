using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Shadeplay;

/// <summary>How the other monitors are darkened.</summary>
public enum DimMethod
{
    /// <summary>Black see-through window on top.</summary>
    Overlay,
    /// <summary>The monitor's own backlight, over DDC/CI. Falls back to the overlay where unsupported.</summary>
    Brightness,
    /// <summary>Both at once.</summary>
    Both,
}

public enum ProfileDimMethod { Default, Overlay, Brightness, Both }

/// <summary>An on/off setting a profile can leave to the defaults.</summary>
public enum ProfileSwitch { Default, On, Off }

public enum ProfileLighting
{
    /// <summary>Use the Lighting tab's settings.</summary>
    Default,
    /// <summary>Leave RGB lighting alone for this game.</summary>
    Unchanged,
    Off,
    Color,
}

/// <summary>
/// Settings for one game or app. Every section either follows the defaults or overrides them.
/// Having a profile also makes the app start the dimming, like a watched app.
/// </summary>
public sealed class GameProfile : ObservableObject
{
    string exeName = "";
    string name = "";
    bool neverDim;
    bool customStrength;
    int strength = 80;
    ProfileDimMethod method = ProfileDimMethod.Default;
    ObservableCollection<string> keepBrightMonitorIds = new();
    ProfileLighting lighting = ProfileLighting.Default;
    string rgbColor = "#FFA020";
    int rgbBrightness = 20;
    ProfileSwitch mouseLock = ProfileSwitch.Default;
    bool customActions;
    bool pauseWallpaperEngine = true;
    bool hideRainmeter = true;
    bool highPerformancePower;
    bool closeOneDrive;
    bool boostPriority;
    bool closeApps = true;

    public GameProfile()
    {
        keepBrightMonitorIds.CollectionChanged += OnKeepBrightChanged;
    }

    /// <summary>Normalized program name, e.g. "eldenring.exe".</summary>
    public string ExeName { get => exeName; set => Set(ref exeName, value ?? ""); }
    public string Name { get => name; set => Set(ref name, value ?? ""); }

    /// <summary>This app never starts the dimming.</summary>
    public bool NeverDim { get => neverDim; set => Set(ref neverDim, value); }

    public bool CustomStrength { get => customStrength; set => Set(ref customStrength, value); }
    public int Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 5, 100)); }
    public ProfileDimMethod Method { get => method; set => Set(ref method, value); }

    /// <summary>Monitors that stay bright too, e.g. the one with Discord.</summary>
    public ObservableCollection<string> KeepBrightMonitorIds
    {
        get => keepBrightMonitorIds;
        set
        {
            ObservableCollection<string>? old = keepBrightMonitorIds;
            keepBrightMonitorIds = value ?? new();
            if (old != keepBrightMonitorIds)
            {
                old.CollectionChanged -= OnKeepBrightChanged;
                keepBrightMonitorIds.CollectionChanged += OnKeepBrightChanged;
            }
            OnPropertyChanged();
        }
    }

    void OnKeepBrightChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(KeepBrightMonitorIds));

    public ProfileLighting Lighting { get => lighting; set => Set(ref lighting, value); }
    public string RgbColor { get => rgbColor; set => Set(ref rgbColor, value ?? "#FFA020"); }
    public int RgbBrightness { get => rgbBrightness; set => Set(ref rgbBrightness, Math.Clamp(value, 1, 100)); }

    /// <summary>Keep the cursor on the game's monitor while it is in focus.</summary>
    public ProfileSwitch MouseLock { get => mouseLock; set => Set(ref mouseLock, value); }

    public bool CustomActions { get => customActions; set => Set(ref customActions, value); }
    public bool PauseWallpaperEngine { get => pauseWallpaperEngine; set => Set(ref pauseWallpaperEngine, value); }
    public bool HideRainmeter { get => hideRainmeter; set => Set(ref hideRainmeter, value); }
    public bool HighPerformancePower { get => highPerformancePower; set => Set(ref highPerformancePower, value); }
    public bool CloseOneDrive { get => closeOneDrive; set => Set(ref closeOneDrive, value); }
    public bool BoostPriority { get => boostPriority; set => Set(ref boostPriority, value); }
    /// <summary>Close the apps on the Actions tab's list.</summary>
    public bool CloseApps { get => closeApps; set => Set(ref closeApps, value); }
}
