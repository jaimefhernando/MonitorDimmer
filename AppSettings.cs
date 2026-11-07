using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shadeplay.Platform;

namespace Shadeplay;

public enum DimMode
{
    /// <summary>A watched app covers its whole monitor.</summary>
    FullscreenWatched,
    /// <summary>Any app (except exceptions) covers its whole monitor.</summary>
    AnyFullscreen,
    /// <summary>A watched app has a visible window, fullscreen or not.</summary>
    WhileOpen,
}

public enum AppTheme { Dark, Light, System }

public enum ZoneLayout { Off, VerticalHalves, HorizontalHalves, Quadrants }

public enum RgbStyle
{
    /// <summary>Lights off while dimming.</summary>
    Off,
    /// <summary>One static color while dimming.</summary>
    Color,
}

public sealed class MonitorSetting : ObservableObject
{
    string id = "";
    bool neverDim;
    bool useCustomStrength;
    int strength = 80;
    string displayName = "";
    bool isConnected;

    /// <summary>Monitor device path, stable across reboots.</summary>
    public string Id { get => id; set => Set(ref id, value); }
    public bool NeverDim { get => neverDim; set => Set(ref neverDim, value); }
    public bool UseCustomStrength { get => useCustomStrength; set => Set(ref useCustomStrength, value); }
    public int Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 5, 100)); }

    /// <summary>Last known description, kept so a disconnected monitor still has a name.</summary>
    public string DisplayName { get => displayName; set => Set(ref displayName, value); }
    [JsonIgnore] public bool IsConnected { get => isConnected; set => Set(ref isConnected, value); }

    string brightnessStatus = "";
    /// <summary>Whether DDC/CI brightness works on this monitor, for display.</summary>
    [JsonIgnore] public string BrightnessStatus { get => brightnessStatus; set => Set(ref brightnessStatus, value); }
}

public sealed class AppSettings : ObservableObject
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    public static string Folder { get; } = ResolveFolder();

    static string ResolveFolder()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "Shadeplay");
        string old = Path.Combine(appData, "MonitorDimmer");
        try
        {
            if (!Directory.Exists(folder) && Directory.Exists(old))
            {
                try { Directory.Move(old, folder); }
                catch (Exception moveEx)
                {
                    Log.Write("Settings folder move failed, copying file-by-file: " + moveEx.Message);
                    Directory.CreateDirectory(folder);
                    foreach (string file in Directory.EnumerateFiles(old))
                    {
                        try { File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true); }
                        catch (Exception copyEx) { Log.Write("Could not copy " + Path.GetFileName(file) + ": " + copyEx.Message); }
                    }
                }
            }
        }
        catch (Exception ex) { Log.Write("Settings folder migration error: " + ex.Message); }
        return folder;
    }

    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Null when <paramref name="path"/> is a readable settings file, else why not.</summary>
    public static string? CheckImport(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            if (!text.TrimStart().StartsWith('{') || !text.Contains("\"WatchedApps\""))
                return "This doesn't look like a Shadeplay settings file.";
            return JsonSerializer.Deserialize<AppSettings>(text, Json) == null ? "The file is empty." : null;
        }
        catch (Exception ex)
        {
            return "The file could not be read: " + ex.Message;
        }
    }

    public void ExportTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    bool enabled = true;

    /// <summary>Settings format version. Old files without this field load as 0; bump when the schema changes.</summary>
    public int SchemaVersion { get; set; } = 1;
    DimMode mode = DimMode.FullscreenWatched;
    int strength = 80;
    int fadeMs = 400;
    bool hideFromCapture = true;
    bool startWithWindows;
    bool hotkeyEnabled = true;
    uint hotkeyModifiers = ModControl | ModAlt;
    int hotkeyKey = 0x44; // D
    bool pauseWallpaperEngine = true;
    bool hideRainmeter = true;
    bool highPerformancePower;
    bool closeOneDrive;
    bool autoDetectGames = true;
    AppTheme theme = AppTheme.Dark;
    bool rgbEnabled = true;
    RgbStyle rgbStyle = RgbStyle.Color;
    string rgbColor = "#FFA020";
    int rgbBrightness = 20;
    bool useRazerChroma = true;
    bool useOpenRgb;
    int openRgbPort = 6742;
    string openRgbDimProfile = "";
    string openRgbRestoreProfile = "";
    string onDimCommands = "";
    string onRestoreCommands = "";

    // Carried over from the AutoHotkey version
    ObservableCollection<string> watchedApps = new()
    {
        "vlc.exe", "stremio.exe", "crunchyroll.exe", "potplayer.exe", "spotify.exe", "eldenring.exe", "ffxiv_dx11.exe",
    };
    ObservableCollection<string> exceptionApps = new() { "discord.exe", "obs64.exe" };
    ObservableCollection<string> peekApps = new() { "teams.exe", "ms-teams.exe", "slack.exe" };
    ObservableCollection<MonitorSetting> monitors = new();
    ObservableCollection<GameProfile> profiles = new();
    DimMethod dimMethod = DimMethod.Overlay;
    bool trackSessions = true;
    bool lockMouse;
    bool autoUpdate = true;
    string updateSource = "";
    bool idleDimEnabled;
    int idleMinutes = 5;
    int idleStrength = 70;
    ZoneLayout zoneLayout;
    bool focusEnabled;
    bool focusByHover;
    bool dimContrastEnabled;
    int dimContrast = 30;
    bool dimSaturationEnabled;
    int dimSaturation = 40;
    bool osdSafe;
    bool obsEnabled;
    bool liveLockMouse;
    bool liveQuietNotifications = true;
    string obsHost = "127.0.0.1";
    int obsPort = 4455;
    string obsPasswordProtected = "";
    bool streamlabsEnabled;
    bool oledProtect;
    int focusStrength = 70;
    bool mediaOverride = true;
    bool isolateHotkeyEnabled = true;
    bool nightEnabled;
    string nightStart = "21:00";
    string nightEnd = "07:00";
    int nightStrength = 30;
    bool nightAllMonitors;
    bool boostPriority;
    ObservableCollection<string> closeApps = new();
    bool reopenClosedApps = true;
    bool welcomeDone;
    bool notifyOnDim = true;
    bool notifyOnSessionEnd = true;
    bool notifyOnUpdate = true;

    public AppSettings()
    {
        Hook(watchedApps);
        Hook(exceptionApps);
        Hook(monitors);
        Hook(profiles);
        Hook(closeApps);
        PropertyChanged += (_, e) => Changed?.Invoke(e.PropertyName ?? "");
    }

    /// <summary>Raised for any change, including inside the lists and monitor entries.</summary>
    public event Action<string>? Changed;

    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public DimMode Mode { get => mode; set => Set(ref mode, value); }
    /// <summary>Darkness in percent; 100 is (almost) black.</summary>
    public int Strength { get => strength; set => Set(ref strength, Math.Clamp(value, 5, 100)); }
    public int FadeMs { get => fadeMs; set => Set(ref fadeMs, Math.Clamp(value, 0, 3000)); }
    /// <summary>Keep the overlay out of screenshots, OBS and screen sharing.</summary>
    public bool HideFromCapture { get => hideFromCapture; set => Set(ref hideFromCapture, value); }
    public bool StartWithWindows { get => startWithWindows; set => Set(ref startWithWindows, value); }
    public bool HotkeyEnabled { get => hotkeyEnabled; set => Set(ref hotkeyEnabled, value); }
    public uint HotkeyModifiers { get => hotkeyModifiers; set => Set(ref hotkeyModifiers, value); }
    /// <summary>Win32 virtual-key code.</summary>
    public int HotkeyKey { get => hotkeyKey; set => Set(ref hotkeyKey, value); }
    public bool PauseWallpaperEngine { get => pauseWallpaperEngine; set => Set(ref pauseWallpaperEngine, value); }
    public bool HideRainmeter { get => hideRainmeter; set => Set(ref hideRainmeter, value); }
    /// <summary>Switch to the High performance power plan while dimming, then back.</summary>
    public bool HighPerformancePower { get => highPerformancePower; set => Set(ref highPerformancePower, value); }
    /// <summary>Close OneDrive while dimming (stops syncing) and start it again afterwards.</summary>
    public bool CloseOneDrive { get => closeOneDrive; set => Set(ref closeOneDrive, value); }

    /// <summary>Treat installed games as watched apps without listing them.</summary>
    public bool AutoDetectGames { get => autoDetectGames; set => Set(ref autoDetectGames, value); }

    public AppTheme Theme { get => theme; set => Set(ref theme, value); }

    public bool RgbEnabled { get => rgbEnabled; set => Set(ref rgbEnabled, value); }
    public RgbStyle RgbStyle { get => rgbStyle; set => Set(ref rgbStyle, value); }
    /// <summary>#RRGGBB.</summary>
    public string RgbColor { get => rgbColor; set => Set(ref rgbColor, value ?? "#FFA020"); }
    public int RgbBrightness { get => rgbBrightness; set => Set(ref rgbBrightness, Math.Clamp(value, 1, 100)); }
    public bool UseRazerChroma { get => useRazerChroma; set => Set(ref useRazerChroma, value); }
    public bool UseOpenRgb { get => useOpenRgb; set => Set(ref useOpenRgb, value); }
    public int OpenRgbPort { get => openRgbPort; set => Set(ref openRgbPort, Math.Clamp(value, 1, 65535)); }
    /// <summary>OpenRGB profile loaded when dimming starts.</summary>
    public string OpenRgbDimProfile { get => openRgbDimProfile; set => Set(ref openRgbDimProfile, value ?? ""); }
    /// <summary>OpenRGB profile loaded when dimming ends.</summary>
    public string OpenRgbRestoreProfile { get => openRgbRestoreProfile; set => Set(ref openRgbRestoreProfile, value ?? ""); }
    /// <summary>Command lines run through cmd.exe when dimming starts, one per line.</summary>
    public string OnDimCommands { get => onDimCommands; set => Set(ref onDimCommands, value ?? ""); }
    /// <summary>Command lines run through cmd.exe when dimming ends, one per line.</summary>
    public string OnRestoreCommands { get => onRestoreCommands; set => Set(ref onRestoreCommands, value ?? ""); }

    public ObservableCollection<string> WatchedApps { get => watchedApps; set => SetList(ref watchedApps, value); }
    public ObservableCollection<string> ExceptionApps { get => exceptionApps; set => SetList(ref exceptionApps, value); }
    public ObservableCollection<string> PeekApps { get => peekApps; set => SetList(ref peekApps, value); }
    public ObservableCollection<MonitorSetting> Monitors { get => monitors; set => SetList(ref monitors, value); }
    public ObservableCollection<GameProfile> Profiles { get => profiles; set => SetList(ref profiles, value); }

    public DimMethod DimMethod { get => dimMethod; set => Set(ref dimMethod, value); }
    /// <summary>Record play sessions for the Stats tab.</summary>
    public bool TrackSessions { get => trackSessions; set => Set(ref trackSessions, value); }
    /// <summary>Keep the cursor on the game's monitor while the game is in focus.</summary>
    public bool LockMouse { get => lockMouse; set => Set(ref lockMouse, value); }
    /// <summary>Check for new versions at startup and once a day, and download them.</summary>
    public bool AutoUpdate { get => autoUpdate; set => Set(ref autoUpdate, value); }
    /// <summary>Where to look for updates instead of the built-in location (a URL or a folder).</summary>
    public string UpdateSource { get => updateSource; set => Set(ref updateSource, value ?? ""); }

    /// <summary>Darken a monitor the mouse and active window have left alone for a while.</summary>
    public bool IdleDimEnabled { get => idleDimEnabled; set => Set(ref idleDimEnabled, value); }
    public int IdleMinutes { get => idleMinutes; set => Set(ref idleMinutes, Math.Clamp(value, 1, 120)); }
    public int IdleStrength { get => idleStrength; set => Set(ref idleStrength, Math.Clamp(value, 10, 100)); }

    /// <summary>Also lower contrast on dimmed monitors that support it (DDC/CI).</summary>
    public bool DimContrastEnabled { get => dimContrastEnabled; set => Set(ref dimContrastEnabled, value); }
    public int DimContrast { get => dimContrast; set => Set(ref dimContrast, Math.Clamp(value, 5, 70)); }
    /// <summary>Also mute colours on dimmed monitors that support it (DDC/CI).</summary>
    public bool DimSaturationEnabled { get => dimSaturationEnabled; set => Set(ref dimSaturationEnabled, value); }
    public int DimSaturation { get => dimSaturation; set => Set(ref dimSaturation, Math.Clamp(value, 5, 90)); }
    /// <summary>Send DDC/CI commands slowly and in small steps, for monitors that choke on quick changes.</summary>
    public bool OsdSafe { get => osdSafe; set => Set(ref osdSafe, value); }
    public bool ObsEnabled { get => obsEnabled; set => Set(ref obsEnabled, value); }
    public bool LiveLockMouse { get => liveLockMouse; set => Set(ref liveLockMouse, value); }
    public bool LiveQuietNotifications { get => liveQuietNotifications; set => Set(ref liveQuietNotifications, value); }
    public string ObsHost { get => obsHost; set => Set(ref obsHost, value?.Trim() ?? "127.0.0.1"); }
    public int ObsPort { get => obsPort; set => Set(ref obsPort, Math.Clamp(value, 1, 65535)); }
    [JsonIgnore] public string ObsPassword
    {
        get => DpapiSecret.Unprotect(obsPasswordProtected);
        set => Set(ref obsPasswordProtected, DpapiSecret.Protect(value ?? ""), nameof(ObsPassword));
    }
    string steamGridDbKeyProtected = "";
    public string ObsPasswordProtected { get => obsPasswordProtected; set => Set(ref obsPasswordProtected, value ?? ""); }
    /// <summary>The user's own SteamGridDB key, encrypted in the settings file like the OBS password.</summary>
    [JsonIgnore]
    public string SteamGridDbKey
    {
        get => DpapiSecret.Unprotect(steamGridDbKeyProtected);
        set => Set(ref steamGridDbKeyProtected, DpapiSecret.Protect(value ?? ""), nameof(SteamGridDbKey));
    }
    public string SteamGridDbKeyProtected { get => steamGridDbKeyProtected; set => Set(ref steamGridDbKeyProtected, value ?? ""); }
    public bool StreamlabsEnabled { get => streamlabsEnabled; set => Set(ref streamlabsEnabled, value); }
    /// <summary>Slowly drift a faint gradient across dimmed screens so no pixel stays at one level.</summary>
    public bool OledProtect { get => oledProtect; set => Set(ref oledProtect, value); }

    /// <summary>Outside games, dim everything except the window you are working in.</summary>
    public ZoneLayout ZoneLayout { get => zoneLayout; set => Set(ref zoneLayout, value); }
    public bool FocusEnabled { get => focusEnabled; set => Set(ref focusEnabled, value); }
    public int FocusStrength { get => focusStrength; set => Set(ref focusStrength, Math.Clamp(value, 10, 95)); }
    /// <summary>Follow the window under the mouse; leaving all app windows restores the normal view.</summary>
    public bool FocusByHover { get => focusByHover; set => Set(ref focusByHover, value); }
    /// <summary>Keep video players and fullscreen videos lit on a dimmed monitor.</summary>
    public bool MediaOverride { get => mediaOverride; set => Set(ref mediaOverride, value); }
    /// <summary>Ctrl+Alt+I: everything pitch-black except the window in front.</summary>
    public bool IsolateHotkeyEnabled { get => isolateHotkeyEnabled; set => Set(ref isolateHotkeyEnabled, value); }

    /// <summary>Warm, dimmer tint on a schedule.</summary>
    public bool NightEnabled { get => nightEnabled; set => Set(ref nightEnabled, value); }
    /// <summary>"HH:mm".</summary>
    public string NightStart { get => nightStart; set => Set(ref nightStart, value ?? "21:00"); }
    /// <summary>"HH:mm"; earlier than the start means the next morning.</summary>
    public string NightEnd { get => nightEnd; set => Set(ref nightEnd, value ?? "07:00"); }
    public int NightStrength { get => nightStrength; set => Set(ref nightStrength, Math.Clamp(value, 5, 80)); }
    /// <summary>Tint every monitor instead of only the side ones.</summary>
    public bool NightAllMonitors { get => nightAllMonitors; set => Set(ref nightAllMonitors, value); }

    /// <summary>Raise the game's process priority while playing.</summary>
    public bool BoostPriority { get => boostPriority; set => Set(ref boostPriority, value); }
    /// <summary>Apps closed while playing, e.g. launchers and updaters.</summary>
    public ObservableCollection<string> CloseApps { get => closeApps; set => SetList(ref closeApps, value); }
    public bool ReopenClosedApps { get => reopenClosedApps; set => Set(ref reopenClosedApps, value); }

    /// <summary>The welcome screen was finished or skipped.</summary>
    public bool WelcomeDone { get => welcomeDone; set => Set(ref welcomeDone, value); }
    /// <summary>"Dimming for Elden Ring" when a game starts the dimming.</summary>
    public bool NotifyOnDim { get => notifyOnDim; set => Set(ref notifyOnDim, value); }
    /// <summary>"You played Elden Ring for 1 h 25 min" after a longer session.</summary>
    public bool NotifyOnSessionEnd { get => notifyOnSessionEnd; set => Set(ref notifyOnSessionEnd, value); }
    public bool NotifyOnUpdate { get => notifyOnUpdate; set => Set(ref notifyOnUpdate, value); }

    /// <summary>Whether <paramref name="now"/> falls in the night mode hours.</summary>
    public bool IsNightTime(DateTime now)
    {
        if (!TimeSpan.TryParse(nightStart, out var start) || !TimeSpan.TryParse(nightEnd, out var end)) return false;
        var t = now.TimeOfDay;
        return start <= end ? t >= start && t < end : t >= start || t < end;
    }

    public GameProfile? FindProfile(string exeName)
    {
        exeName = Core.WindowScanner.NormalizeExe(exeName);
        return profiles.FirstOrDefault(p => p.ExeName == exeName);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Keep the broken file for inspection and start from defaults
            Log.Write("Settings could not be read, using defaults: " + ex.Message);
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch (Exception) { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Write("Settings could not be saved: " + ex.Message);
        }
    }

    public MonitorSetting? FindMonitor(string id) => monitors.FirstOrDefault(m => m.Id == id);

    void SetList<T>(ref ObservableCollection<T> field, ObservableCollection<T>? value, [CallerMemberName] string? name = null)
    {
        Unhook(field);
        field = value ?? new ObservableCollection<T>();
        Hook(field);
        OnPropertyChanged(name);
    }

    void Hook<T>(ObservableCollection<T> list)
    {
        list.CollectionChanged += OnListChanged;
        HookItems(list);
    }

    void Unhook<T>(ObservableCollection<T> list)
    {
        list.CollectionChanged -= OnListChanged;
        foreach (var item in list.OfType<INotifyPropertyChanged>()) item.PropertyChanged -= OnItemChanged;
    }

    void HookItems(IEnumerable? items)
    {
        if (items == null) return;
        foreach (var item in items.OfType<INotifyPropertyChanged>()) item.PropertyChanged += OnItemChanged;
    }

    void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>()) item.PropertyChanged -= OnItemChanged;
        HookItems(e.NewItems);
        Changed?.Invoke(sender == watchedApps ? nameof(WatchedApps)
            : sender == exceptionApps ? nameof(ExceptionApps)
            : sender == peekApps ? nameof(PeekApps)
            : sender == profiles ? nameof(Profiles)
            : sender == closeApps ? nameof(CloseApps) : nameof(Monitors));
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is GameProfile)
        {
            Changed?.Invoke(nameof(Profiles));
            return;
        }
        // Display-only properties are not settings
        if (e.PropertyName is nameof(MonitorSetting.IsConnected) or nameof(MonitorSetting.BrightnessStatus)) return;
        Changed?.Invoke(nameof(Monitors));
    }
}
