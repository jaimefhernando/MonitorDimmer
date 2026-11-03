using System.Windows.Threading;
using MonitorDimmer.Interop;
using MonitorDimmer.Platform;

namespace MonitorDimmer.Core;

public enum MonitorDimKind { Normal, Game, Dimmed, Idle, Night }

/// <param name="Strength">Darkness in percent while dimmed, idle or tinted.</param>
/// <param name="UsesBrightness">Dimmed through the monitor's own backlight (DDC/CI).</param>
public sealed record MonitorState(MonitorInfo Monitor, MonitorDimKind Kind, int Strength, bool UsesBrightness);

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

    // Idle dimming: when the mouse or the active window was last on each monitor
    readonly DispatcherTimer activityTimer;
    readonly Dictionary<string, DateTime> lastActive = new();
    readonly DateTime startedAt = DateTime.Now;
    HashSet<string> idleCandidates = new();
    List<MonitorInfo> lastMonitors = new();

    DateTime? pausedUntil;
    bool? nightOverride;     // night mode switched on/off from the tray, until the schedule next changes
    bool nightOverrideFrom;  // what the schedule said when it was switched

    static readonly System.Drawing.Color Black = System.Drawing.Color.Black;
    // Dark amber: over a white page at 30 % it gives a warm, dimmer tone
    static readonly System.Drawing.Color NightTint = System.Drawing.Color.FromArgb(110, 45, 0);

    public DimController(AppSettings settings)
    {
        this.settings = settings;
        // Cheap: only reads the cursor position, and runs a full update only when a monitor
        // becomes idle or active again
        activityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        activityTimer.Tick += (_, _) => OnActivityTick();
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

    /// <summary>A game or watched app started the dimming (not when dimmed by hand): name, exe.</summary>
    public event Action<string, string>? SessionStarted;

    /// <summary>A recorded play session ended.</summary>
    public event Action<PlaySession>? SessionEnded;

    public bool ManualActive => manualBrightId != null;
    /// <summary>Paused from the tray: no game, idle or night dimming until it ends.</summary>
    public bool AutoPaused => pausedUntil is DateTime until && DateTime.Now < until;
    public DateTime? PausedUntil => AutoPaused && pausedUntil != DateTime.MaxValue ? pausedUntil : null;
    public bool NightOn { get; private set; }
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
        activityTimer.Start();
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

    /// <param name="duration">Null pauses until <see cref="Resume"/>.</param>
    public void PauseFor(TimeSpan? duration)
    {
        pausedUntil = duration is TimeSpan d ? DateTime.Now + d : DateTime.MaxValue;
        Log.Write(duration is TimeSpan t ? $"Paused for {t.TotalMinutes} min" : "Paused");
        Update();
    }

    public void Resume()
    {
        pausedUntil = null;
        Update();
    }

    /// <summary>Night mode on or off now; the schedule takes over again at its next start or end.</summary>
    public void ToggleNight()
    {
        bool scheduled = NightScheduled(DateTime.Now);
        bool target = !(nightOverride ?? scheduled);
        nightOverride = target == scheduled ? null : target;
        nightOverrideFrom = scheduled;
        Update();
    }

    bool NightScheduled(DateTime now) => settings.NightEnabled && settings.IsNightTime(now);

    bool NightNow(DateTime now)
    {
        bool scheduled = NightScheduled(now);
        if (nightOverride != null && scheduled != nightOverrideFrom) nightOverride = null;
        return nightOverride ?? scheduled;
    }

    void OnActivityTick()
    {
        if (!settings.IdleDimEnabled)
        {
            if (idleCandidates.Count > 0) Update();
            return;
        }
        if (Native.GetCursorPos(out var pt))
        {
            IntPtr hMonitor = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            var m = lastMonitors.FirstOrDefault(x => x.Handle == hMonitor);
            if (m != null) lastActive[m.Id] = DateTime.Now;
        }
        if (!IdleCandidates(lastMonitors).SetEquals(idleCandidates)) Update();
    }

    /// <summary>Monitors the mouse and the active window have stayed away from long enough.</summary>
    HashSet<string> IdleCandidates(List<MonitorInfo> monitors)
    {
        if (!settings.IdleDimEnabled) return new HashSet<string>();
        var limit = DateTime.Now - TimeSpan.FromMinutes(settings.IdleMinutes);
        return monitors
            .Where(m => (lastActive.TryGetValue(m.Id, out var t) ? t : startedAt) < limit)
            .Select(m => m.Id)
            .ToHashSet();
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
        exclusiveFullscreen = Native.IsExclusiveFullscreenGameRunning() ? foreground : IntPtr.Zero;
        var exceptions = NormalizedSet(settings.ExceptionApps);
        var watched = NormalizedSet(settings.WatchedApps);
        lastMonitors = monitors;

        // 1. What keeps monitors bright, and which app the session is for
        var bright = new HashSet<string>();
        var triggerIds = new HashSet<string>();
        string? key = null;
        GameProfile? profile = null;
        WindowInfo? trigger = null;
        MonitorInfo? triggerMonitor = null;
        string status;

        if (manualBrightId != null)
        {
            bright.Add(manualBrightId);
            triggerIds.Add(manualBrightId);
            key = ManualSession;
            status = "Dimmed manually";
        }
        else if (!settings.Enabled)
        {
            status = "Automatic dimming is off";
        }
        else if (AutoPaused)
        {
            status = PausedUntil is DateTime until ? $"Paused until {until:HH:mm}" : "Paused";
        }
        else
        {
            var triggers = new List<(WindowInfo Window, MonitorInfo Monitor)>();
            foreach (var w in windows)
            {
                if (exceptions.Contains(w.ProcessName)) continue;
                if (!byHandle.TryGetValue(w.Monitor, out var monitor)) continue;
                if (!IsTrigger(w, monitor, watched)) continue;
                triggers.Add((w, monitor));
                bright.Add(monitor.Id);
                triggerIds.Add(monitor.Id);
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

        // 3. For each monitor the first that applies: game dimming (overlay and/or its own
        //    brightness), idle dimming, night tint
        var now = DateTime.Now;
        bool active = !AutoPaused;
        NightOn = active && NightNow(now);

        // A monitor showing a fullscreen window, a game, a watched app or an exception app is
        // in use even when the mouse stays away (a video on a side screen, Discord during a game)
        idleCandidates = IdleCandidates(monitors);
        var idle = new HashSet<string>();
        if (active && idleCandidates.Count > 0)
        {
            var inUse = new HashSet<string>();
            foreach (var w in windows)
            {
                if (!byHandle.TryGetValue(w.Monitor, out var m)) continue;
                if (IsFullscreen(w, m) || exceptions.Contains(w.ProcessName) || IsWatched(w, watched)) inUse.Add(m.Id);
            }
            idle = idleCandidates.Where(id => !inUse.Contains(id)).ToHashSet();
        }

        var overlayIds = new HashSet<string>();
        var brightnessIds = new HashSet<string>();
        var states = new List<MonitorState>();
        if (plan != null) bright.UnionWith(plan.KeepBrightMonitorIds);
        foreach (var monitor in monitors)
        {
            var ms = settings.FindMonitor(monitor.Id);
            var kind = triggerIds.Contains(monitor.Id) ? MonitorDimKind.Game : MonitorDimKind.Normal;
            if (ms?.NeverDim == true)
            {
                states.Add(new MonitorState(monitor, kind, 0, false));
                continue;
            }

            if (plan != null && !bright.Contains(monitor.Id))
            {
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
                    ShowOverlay(monitor, Black, strength / 100.0, ExceptionHoles(windows, monitor, exceptions));
                }
                states.Add(new MonitorState(monitor, MonitorDimKind.Dimmed, strength, ddc));
            }
            else if (idle.Contains(monitor.Id))
            {
                overlayIds.Add(monitor.Id);
                ShowOverlay(monitor, Black, settings.IdleStrength / 100.0, ExceptionHoles(windows, monitor, exceptions));
                states.Add(new MonitorState(monitor, MonitorDimKind.Idle, settings.IdleStrength, false));
            }
            else if (NightOn && (settings.NightAllMonitors || !monitor.IsPrimary) && !triggerIds.Contains(monitor.Id))
            {
                overlayIds.Add(monitor.Id);
                ShowOverlay(monitor, NightTint, settings.NightStrength / 100.0, new List<RECT>());
                states.Add(new MonitorState(monitor, MonitorDimKind.Night, settings.NightStrength, false));
            }
            else
            {
                states.Add(new MonitorState(monitor, kind, 0, false));
            }
        }

        if (plan == null && active)
        {
            var extras = new List<string>();
            if (NightOn) extras.Add("night mode on");
            extras.AddRange(monitors.Where(m => idle.Contains(m.Id)).Select(m => $"Display {m.Number} idle"));
            if (extras.Count > 0) status += " · " + string.Join(", ", extras);
        }

        // 4. Keep the cursor on the game's monitor while the fullscreen game is in focus;
        //    Alt+Tab, the Windows key or any other window taking focus frees it
        bool lockCursor = plan?.MouseLock == true && trigger != null && triggerMonitor != null
            && trigger.Handle == foreground && IsFullscreen(trigger, triggerMonitor);
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

        // What the Home page and tray show
        var extrasNow = new List<string>();
        if (plan != null)
        {
            if (states.Any(s => s.UsesBrightness)) extrasNow.Add("Real brightness · DDC/CI");
            if (plan.Rgb) extrasNow.Add(plan.RgbOptions.Style == RgbStyle.Off ? "RGB lights off" : "RGB dimmed");
            if (plan.WallpaperEngine) extrasNow.Add("Wallpaper Engine paused");
            if (plan.Rainmeter) extrasNow.Add("Rainmeter hidden");
            if (plan.BoostPriority) extrasNow.Add("Game priority raised");
            if (plan.Power) extrasNow.Add("High performance power");
            if (mouseLock.IsLocked && triggerMonitor != null) extrasNow.Add($"Mouse locked to Display {triggerMonitor.Number}");
        }
        string signature = status + "|" + string.Join(";", states.Select(s => $"{s.Monitor.Id}:{s.Kind}:{s.Strength}"))
            + "|" + string.Join(";", extrasNow);
        Monitors = states;
        SessionExtras = extrasNow;

        if (signature != lastSignature)
        {
            lastSignature = signature;
            Status = status;
            StateChanged?.Invoke();
        }
    }

    string lastSignature = "";

    /// <summary>Each monitor and what is happening to it, as of the last update.</summary>
    public IReadOnlyList<MonitorState> Monitors { get; private set; } = Array.Empty<MonitorState>();

    /// <summary>The game or app the current session is for; null when not dimming for one.</summary>
    public string? SessionName { get; private set; }
    public DateTime? SessionStartedAt { get; private set; }
    /// <summary>Program of the current session, e.g. "eldenring.exe".</summary>
    public string? SessionExe { get; private set; }

    /// <summary>Short descriptions of what the current session changed ("Wallpaper Engine paused").</summary>
    public IReadOnlyList<string> SessionExtras { get; private set; } = Array.Empty<string>();

    void BeginSession(string key, GameProfile? profile, WindowInfo? trigger)
    {
        sessionKey = key;
        sessionPlan = SessionPlan.Resolve(settings, profile);
        SessionName = trigger != null ? DisplayName(trigger, profile) : null;
        SessionExe = trigger?.ProcessName;
        SessionStartedAt = DateTime.Now;
        actions.DimStarted(sessionPlan, trigger?.ProcessId);
        if (trigger != null && settings.TrackSessions)
        {
            bool isGame = profile != null || GameDetector.IsGame(trigger.ExePath);
            Sessions.Begin(trigger.ProcessName, DisplayName(trigger, profile), isGame);
        }
        Log.Write($"Dimming started: {key}" + (profile != null ? $" (profile '{profile.Name}')" : ""));
        if (trigger != null) SessionStarted?.Invoke(DisplayName(trigger, profile), trigger.ProcessName);
    }

    void EndSession()
    {
        if (sessionPlan != null) actions.DimEnded(sessionPlan);
        if (Sessions.End() is { } finished && !disposed) SessionEnded?.Invoke(finished);
        Log.Write($"Dimming ended: {sessionKey}");
        sessionKey = null;
        sessionPlan = null;
        SessionName = null;
        SessionExe = null;
        SessionStartedAt = null;
    }

    static string DisplayName(WindowInfo w, GameProfile? profile)
    {
        if (profile?.Name.Length > 0) return profile.Name;
        string title = w.Title.Trim();
        return title.Length > 0 && title.Length <= 60 ? title : w.ProcessName;
    }

    // The foreground window while Windows reports an exclusive-fullscreen DirectX game
    IntPtr exclusiveFullscreen;

    /// <summary>Covers its monitor, or is the exclusive-fullscreen game Windows reports.</summary>
    bool IsFullscreen(WindowInfo w, MonitorInfo monitor) =>
        w.IsFullscreenOn(monitor) || (w.Handle == exclusiveFullscreen && exclusiveFullscreen != IntPtr.Zero);

    bool IsWatched(WindowInfo w, HashSet<string> watched) =>
        settings.FindProfile(w.ProcessName) != null || watched.Contains(w.ProcessName)
        || (settings.AutoDetectGames && GameDetector.IsGame(w.ExePath));

    bool IsTrigger(WindowInfo w, MonitorInfo monitor, HashSet<string> watched)
    {
        if (settings.FindProfile(w.ProcessName)?.NeverDim == true) return false;
        bool isWatched = IsWatched(w, watched);
        return settings.Mode switch
        {
            DimMode.FullscreenWatched => isWatched && IsFullscreen(w, monitor),
            DimMode.AnyFullscreen => IsFullscreen(w, monitor),
            DimMode.WhileOpen => isWatched,
            _ => false,
        };
    }

    void ShowOverlay(MonitorInfo monitor, System.Drawing.Color color, double opacity, List<RECT> holes)
    {
        if (!overlays.TryGetValue(monitor.Id, out var overlay) || overlay.IsDisposed)
        {
            overlay = new OverlayForm(monitor.Bounds, settings.HideFromCapture);
            overlays[monitor.Id] = overlay;
        }
        overlay.BackColor = color;
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
        {
            if (m.Id != lastForegroundMonitor) lastActive[m.Id] = DateTime.Now; // you just switched to it
            lastForegroundMonitor = m.Id;
        }
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
        activityTimer.Stop();
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
