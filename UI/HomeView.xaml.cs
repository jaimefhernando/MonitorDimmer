using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MonitorDimmer.Core;
using MonitorDimmer.UI.Controls;

namespace MonitorDimmer.UI;

/// <summary>What is happening right now, the main switches and this week's play time.</summary>
public partial class HomeView : UserControl
{
    public sealed record DayBar(string Label, double Height, Brush Fill, string Tip);
    public sealed record RecentRow(string Mono, string Name, string When, string Time);

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(20) };
    AppSettings settings = null!;
    DimController controller = null!;
    Action<string?> openProfile = _ => { };
    Action openStats = () => { };

    public HomeView()
    {
        InitializeComponent();
        clock.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                RefreshStats();
                clock.Start();
            }
            else
            {
                clock.Stop();
            }
        };
    }

    /// <param name="openProfile">Shows the Games page with this program's profile (created if needed).</param>
    public void Init(AppSettings settings, DimController controller, Action<string?> openProfile, Action openStats)
    {
        this.settings = settings;
        this.controller = controller;
        this.openProfile = openProfile;
        this.openStats = openStats;
        controller.StateChanged += () => Dispatcher.BeginInvoke(new Action(Refresh));
        controller.Sessions.HistoryChanged += () => Dispatcher.BeginInvoke(new Action(RefreshStats));
        settings.Changed += name =>
        {
            if (name == nameof(AppSettings.Enabled)) Refresh();
        };
        Refresh();
        RefreshStats();
    }

    void Refresh()
    {
        if (controller == null) return;
        var now = DateTime.Now;
        Tracking.SetText(ClockText, now.ToString("dddd HH:mm", CultureInfo.CurrentCulture).ToUpperInvariant());

        // Header buttons
        PauseText.Text = controller.AutoPaused
            ? controller.PausedUntil is DateTime until ? $"Resume (paused until {until:HH:mm})" : "Resume"
            : "Pause 1 hour";
        bool auto = settings.Enabled;
        AutoButton.Style = (Style)FindResource(auto ? "AccentButton" : typeof(Button));
        AutoDot.Fill = (Brush)FindResource(auto ? "AccentText" : "KnobOff");
        Tracking.SetText(AutoText, auto ? "AUTO-DIM ON" : "AUTO-DIM OFF");

        // Now dimming
        var states = controller.Monitors;
        var dimmed = states.Where(s => s.Kind == MonitorDimKind.Dimmed).ToList();
        var game = states.FirstOrDefault(s => s.Kind == MonitorDimKind.Game);
        bool dimming = controller.IsDimming;
        var accent = (Brush)FindResource("Accent");

        StateDiamond.Fill = dimming ? accent : (Brush)FindResource("KnobOff");
        StateKicker.Foreground = dimming ? accent : (Brush)FindResource("TextMuted");
        if (dimming && controller.SessionName != null)
        {
            Tracking.SetText(StateKicker, "NOW DIMMING FOR");
            SessionTitle.Text = controller.SessionName;
            string minutes = controller.SessionStartedAt is DateTime start
                ? $" · {SessionTracker.Format(now - start)} this session" : "";
            SessionDetail.Text = Describe(game, dimmed) + minutes;
            ProfileButton.Content = settings.FindProfile(controller.SessionExe ?? "") != null
                ? $"Edit {controller.SessionName} profile" : $"Make a profile for {controller.SessionName}";
            ProfileButton.Visibility = Visibility.Visible;
        }
        else if (dimming)
        {
            Tracking.SetText(StateKicker, "DIMMED BY HAND");
            SessionTitle.Text = "Other monitors dimmed";
            SessionDetail.Text = Describe(game, dimmed);
            ProfileButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            Tracking.SetText(StateKicker, controller.AutoPaused ? "PAUSED" : auto ? "READY" : "AUTO-DIM OFF");
            SessionTitle.Text = controller.AutoPaused ? "Dimming is paused" : "Waiting for a game";
            SessionDetail.Text = controller.AutoPaused
                ? "Nothing dims until the pause ends or you resume."
                : "Start a game or video in fullscreen and the other monitors go dark.";
            ProfileButton.Visibility = Visibility.Collapsed;
        }
        DimNowButton.Content = controller.ManualActive ? "Undo dim now" : "Dim now";
        ExtrasList.ItemsSource = controller.SessionExtras;

        Map.Tiles = states.Select(s => new MonitorTile(s.Monitor.Bounds, $"Display {s.Monitor.Number}", Badge(s),
            s.Monitor.FriendlyName, s.Kind switch
            {
                MonitorDimKind.Game => MonitorTileState.Game,
                MonitorDimKind.Dimmed or MonitorDimKind.Idle => MonitorTileState.Dimmed,
                _ => MonitorTileState.Normal,
            })).ToList();
        // The map draws its own content, so it describes itself to screen readers
        System.Windows.Automation.AutomationProperties.SetName(Map,
            string.Join("; ", states.Select(s => $"Display {s.Monitor.Number}: {Badge(s).ToLowerInvariant()}")));

        GamesDetail.Text = settings.AutoDetectGames
            ? $"{GameDetector.InstalledGames.Count} games detected"
            : $"{settings.WatchedApps.Count} watched apps";
    }

    static string Badge(MonitorState s) => s.Kind switch
    {
        MonitorDimKind.Game => "GAME",
        MonitorDimKind.Dimmed => $"DIMMED {s.Strength} %",
        MonitorDimKind.Idle => $"IDLE {s.Strength} %",
        MonitorDimKind.Night => "NIGHT MODE",
        _ => "BRIGHT",
    };

    static string Describe(MonitorState? game, List<MonitorState> dimmed)
    {
        var parts = new List<string>();
        if (game != null) parts.Add($"Display {game.Monitor.Number} stays bright");
        if (dimmed.Count > 0)
            parts.Add(string.Join(", ", dimmed.Select(d => $"Display {d.Monitor.Number}")) + $" dimmed to {dimmed[0].Strength} %");
        return string.Join(" · ", parts);
    }

    void RefreshStats()
    {
        if (controller == null) return;
        var sessions = controller.Sessions.Load().Where(s => s.IsGame).ToList();

        // Monday to Sunday of this week
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var minutes = Enumerable.Range(0, 7)
            .Select(i => sessions.Where(s => s.Start.Date == monday.AddDays(i)).Sum(s => s.Duration.TotalMinutes))
            .ToList();
        double max = Math.Max(minutes.Max(), 1);
        var accent = (Brush)FindResource("Accent");
        var rest = (Brush)FindResource("KnobOff");
        WeekBars.ItemsSource = minutes.Select((m, i) =>
        {
            var day = monday.AddDays(i);
            return new DayBar(day.ToString("ddd", CultureInfo.CurrentCulture)[..2], m / max * 88,
                m > 0 && m >= max ? accent : rest, $"{day:dddd}: {SessionTracker.Format(TimeSpan.FromMinutes(m))}");
        }).ToList();

        var week = sessions.Where(s => s.Start >= monday).ToList();
        WeekTotal.Text = week.Count == 0 ? "0 min" : SessionTracker.Format(TimeSpan.FromMinutes(week.Sum(s => s.Duration.TotalMinutes)));
        WeekSessions.Text = week.Count switch
        {
            0 => "no games played yet this week",
            1 => "in 1 session",
            _ => $"across {week.Count} sessions",
        };

        var recent = sessions.OrderByDescending(s => s.Start).Take(4).Select(s => new RecentRow(
            Monogram(s.Name), s.Name,
            s.Start.Date == today ? "Today" : s.Start.Date == today.AddDays(-1) ? "Yesterday" : s.Start.ToString("ddd", CultureInfo.CurrentCulture),
            SessionTracker.Format(s.Duration))).ToList();
        RecentList.ItemsSource = recent;
        NoSessionsText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static string Monogram(string name) => MonogramConverter.Of(name);

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (controller.AutoPaused) controller.Resume();
        else controller.PauseFor(TimeSpan.FromHours(1));
        Refresh();
    }

    void Auto_Click(object sender, RoutedEventArgs e) => settings.Enabled = !settings.Enabled;

    void Profile_Click(object sender, RoutedEventArgs e) => openProfile(controller.SessionExe);

    void DimNow_Click(object sender, RoutedEventArgs e)
    {
        controller.ToggleManual();
        Refresh();
    }

    void AllStats_Click(object sender, RoutedEventArgs e) => openStats();
}
