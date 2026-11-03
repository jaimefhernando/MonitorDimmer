using System.Windows.Threading;
using MonitorDimmer.Interop;
using MonitorDimmer.Platform;

namespace MonitorDimmer.Core;

/// <summary>
/// Whenever windows change: finds the monitors that should stay bright, dims the others (overlay
/// and/or real brightness), keeps exception apps cut out, and runs the game's profile.
/// </summary>
public sealed class DimController : IDisposable
{
    const string ManualSession = "(manual)";

    readonly AppSettings settings;
    readonly DispatcherTimer timer;
    readonly DispatcherTimer eventDelay;
    readonly Dictionary<string, OverlayForm> overlays = new();
    readonly Dictionary<string, (MonitorInfo Monitor, int Strength)> brightnessDimmed = new();
    readonly ActionRunner actions = new();
    readonly MouseLock mouseLock = new();
    WindowEventHook? windowEvents;

    // The session in progress: which app started it and the plan its actions were run with
    string? sessionKey;
    SessionPlan? sessionPlan;

    string? manualBrightId;        // set while dimming was turned on by hand
    string? lastForegroundMonitor; // monitor of the last app window the user was in

    public DimController(AppSettings settings)
    {
        this.settings = settings;
        // Window events trigger updates right away; this regular check only catches anything
        // the events miss (a game switching display mode, a process exiting without notice...)
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Update();
        // Dragging a window fires dozens of events; they are batched into one update
        eventDelay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        eventDelay.Tick += (_, _) =>
        {
            eventDelay.Stop();
            Update();
        };
    }

    public event Action? StateChanged;

    public bool ManualActive => manualBrightId != null;
    public bool AutoPaused { get; private set; }
    public string Status { get; private set; } = "Starting";
    public bool IsDimming => sessionKey != null;

    public MonitorBrightness Brightness { get; } = new();
    public SessionTracker Sessions { get; } = new();

    public void Start()
    {
        var monitors = Screens.GetAll();
        Brightness.RestoreLeftovers(monitors);
        Brightness.Detect(monitors);
        windowEvents = new WindowEventHook();
        windowEvents.WindowChanged += OnWindowChanged;
        Update();
        timer.Start();
    }

    void OnWindowChanged()
    {
        if (!eventDelay.IsEnabled) eventDelay.Start();
    }

    /// <summary>Dims every monitor except the one you were last working on, or undoes it.</summary>
    public void ToggleManual()
    {
        manualBrightId = manualBrightId == null
            ? lastForegroundMonitor ?? Screens.GetAll().FirstOrDefault(m => m.IsPrimary)?.Id ?? ""
            : null;
        Update();
    }

    public void SetAutoPaused(bool paused)
    {
        AutoPaused = paused;
        Update();
    }

    /// <summary>Shows the lighting used while dimming for a few seconds.</summary>
    public void PreviewLighting() =>
        actions.PreviewRgb(sessionPlan ?? SessionPlan.Resolve(settings, null), IsDimming);

    public void Update()
    {
        List<MonitorInfo> monitors;
        List<WindowInfo> windows;
        try
        {
            monitors = Screens.GetAll();
            windows = WindowScanner.Scan();
        }
        catch (Exception ex)
        {
            Log.Write("Scan failed: " + ex);
            return;
        }

        var byHandle = monitors.ToDictionary(m => m.Handle);
        IntPtr foreground = TrackForeground(byHandle);
        var exceptions = NormalizedSet(settings.ExceptionApps);

        // 1. What keeps monitors bright, and which app the session is for
        var bright = new HashSet<string>();
        string? key = null;
        GameProfile? profile = null;
        WindowInfo? trigger = null;
        MonitorInfo? triggerMonitor = null;
        string status;

        if (manualBrightId != null)
        {
            bright.Add(manualBrightId);
            key = ManualSession;
            status = "Dimmed manually";
        }
        else if (!settings.Enabled)
        {
            status = "Automatic dimming is off";
        }
        else if (AutoPaused)
        {
            status = "Paused";
        }
        else
        {
            var watched = NormalizedSet(settings.WatchedApps);
            var triggers = new List<(WindowInfo Window, MonitorInfo Monitor)>();
            foreach (var w in windows)
            {
                if (exceptions.Contains(w.ProcessName)) continue;
                if (!byHandle.TryGetValue(w.Monitor, out var monitor)) continue;
                if (!IsTrigger(w, monitor, watched)) continue;
                triggers.Add((w, monitor));
                bright.Add(monitor.Id);
            }

            if (triggers.Count > 0)
            {
                // The game you are in decides the profile when several qualify
                var main = triggers.FirstOrDefault(t => t.Window.Handle == foreground);
                if (main.Window == null) main = triggers[0];
                trigger = main.Window;
                triggerMonitor = main.Monitor;
                key = trigger.ProcessName;
                profile = settings.FindProfile(key);
                status = $"Dimming for {DisplayName(trigger, profile)} on Display {main.Monitor.Number}";
            }
            else
            {
                status = "Waiting for a fullscreen app";
            }
        }

        // 2. Start, switch or end the session (actions, lighting, stats)
        if (key != sessionKey)
        {
            if (sessionKey != null) EndSession();
            if (key != null) BeginSession(key, profile, trigger);
        }
        else if (key != null)
        {
            // Same session: pick up profile edits (darkness, monitors) right away
            sessionPlan = SessionPlan.Resolve(settings, profile);
        }
        var plan = sessionPlan;

        // 3. Dim every other monitor, with the overlay and/or its own brightness
        var overlayIds = new HashSet<string>();
        var brightnessIds = new HashSet<string>();
        if (plan != null)
        {
            bright.UnionWith(plan.KeepBrightMonitorIds);
            foreach (var monitor in monitors)
            {
                var ms = settings.FindMonitor(monitor.Id);
                if (bright.Contains(monitor.Id) || ms?.NeverDim == true) continue;

                int strength = ms?.UseCustomStrength == true ? ms.Strength : plan.Strength ?? settings.Strength;
                bool ddc = plan.Method != DimMethod.Overlay && Brightness.GetSupport(monitor.Id)?.Supported == true;
                bool overlay = plan.Method != DimMethod.Brightness || !ddc;

                if (ddc)
                {
                    brightnessIds.Add(monitor.Id);
                    if (!brightnessDimmed.TryGetValue(monitor.Id, out var current) || current.Strength != strength)
                    {
                        Brightness.Dim(monitor, strength);
                        brightnessDimmed[monitor.Id] = (monitor, strength);
                    }
                }
                if (overlay)
                {
                    overlayIds.Add(monitor.Id);
                    ShowOverlay(monitor, strength / 100.0, ExceptionHoles(windows, monitor, exceptions));
                }
            }
        }

        // 4. Keep the cursor on the game's monitor while the fullscreen game is in focus;
        //    Alt+Tab, the Windows key or any other window taking focus frees it
        bool lockCursor = plan?.MouseLock == true && trigger != null && triggerMonitor != null
            && trigger.Handle == foreground && trigger.IsFullscreenOn(triggerMonitor);
        mouseLock.Apply(lockCursor ? triggerMonitor!.Bounds : null);

        foreach (var (id, dimmed) in brightnessDimmed.ToList())
        {
            if (brightnessIds.Contains(id)) continue;
            Brightness.Restore(monitors.FirstOrDefault(m => m.Id == id) ?? dimmed.Monitor);
            brightnessDimmed.Remove(id);
        }

        foreach (var (id, overlay) in overlays.ToList())
        {
            if (overlayIds.Contains(id)) continue;
            overlay.FadeTo(0, settings.FadeMs, () =>
            {
                if (overlays.TryGetValue(id, out var current) && current == overlay) overlays.Remove(id);
                overlay.Close();
                overlay.Dispose();
            });
        }

        if (status != Status)
        {
            Status = status;
            StateChanged?.Invoke();
        }
    }

    void BeginSession(string key, GameProfile? profile, WindowInfo? trigger)
    {
        sessionKey = key;
        sessionPlan = SessionPlan.Resolve(settings, profile);
        actions.DimStarted(sessionPlan);
        if (trigger != null && settings.TrackSessions)
        {
            bool isGame = profile != null || GameDetector.IsGame(trigger.ExePath);
            Sessions.Begin(trigger.ProcessName, DisplayName(trigger, profile), isGame);
        }
        Log.Write($"Dimming started: {key}" + (profile != null ? $" (profile '{profile.Name}')" : ""));
    }

    void EndSession()
    {
        if (sessionPlan != null) actions.DimEnded(sessionPlan);
        Sessions.End();
        Log.Write($"Dimming ended: {sessionKey}");
        sessionKey = null;
        sessionPlan = null;
    }

    static string DisplayName(WindowInfo w, GameProfile? profile)
    {
        if (profile?.Name.Length > 0) return profile.Name;
        string title = w.Title.Trim();
        return title.Length > 0 && title.Length <= 60 ? title : w.ProcessName;
    }

    bool IsTrigger(WindowInfo w, MonitorInfo monitor, HashSet<string> watched)
    {
        var profile = settings.FindProfile(w.ProcessName);
        if (profile?.NeverDim == true) return false;
        bool isWatched = profile != null || watched.Contains(w.ProcessName)
            || (settings.AutoDetectGames && GameDetector.IsGame(w.ExePath));
        return settings.Mode switch
        {
            DimMode.FullscreenWatched => isWatched && w.IsFullscreenOn(monitor),
            DimMode.AnyFullscreen => w.IsFullscreenOn(monitor),
            DimMode.WhileOpen => isWatched,
            _ => false,
        };
    }

    void ShowOverlay(MonitorInfo monitor, double opacity, List<RECT> holes)
    {
        if (!overlays.TryGetValue(monitor.Id, out var overlay) || overlay.IsDisposed)
        {
            overlay = new OverlayForm(monitor.Bounds, settings.HideFromCapture);
            overlays[monitor.Id] = overlay;
        }
        overlay.FadeTo(opacity, settings.FadeMs); // shows the window on first use
        overlay.Place(monitor.Bounds);
        overlay.SetCaptureExcluded(settings.HideFromCapture);
        overlay.SetHoles(holes);
        overlay.KeepOnTop();
    }

    static List<RECT> ExceptionHoles(List<WindowInfo> windows, MonitorInfo monitor, HashSet<string> exceptions) =>
        windows
            .Where(w => exceptions.Contains(w.ProcessName) && w.Bounds.IntersectsWith(monitor.Bounds))
            .Select(w => w.Bounds.Intersect(monitor.Bounds))
            .ToList();

    /// <returns>The foreground window.</returns>
    IntPtr TrackForeground(Dictionary<IntPtr, MonitorInfo> byHandle)
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return fg;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId) return fg; // our tray menu or settings window
        if (byHandle.TryGetValue(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST), out var m))
            lastForegroundMonitor = m.Id;
        return fg;
    }

    static HashSet<string> NormalizedSet(IEnumerable<string> names) =>
        names.Select(WindowScanner.NormalizeExe).Where(n => n.Length > 0).ToHashSet();

    bool disposed;

    public void Dispose()
    {
        // Called on exit and on Windows sign-out, which can both happen
        if (disposed) return;
        disposed = true;
        timer.Stop();
        eventDelay.Stop();
        windowEvents?.Dispose();
        mouseLock.Dispose();
        foreach (var overlay in overlays.Values)
        {
            overlay.Close();
            overlay.Dispose();
        }
        overlays.Clear();
        foreach (var (monitor, _) in brightnessDimmed.Values) Brightness.Restore(monitor);
        brightnessDimmed.Clear();
        if (sessionKey != null) EndSession();

        // Give lighting, brightness, Wallpaper Engine etc. a moment to be restored before exit
        actions.Flush(TimeSpan.FromSeconds(4));
        Brightness.Flush(TimeSpan.FromSeconds(4));
        actions.Dispose();
        Brightness.Dispose();
        Sessions.Dispose();
    }
}
