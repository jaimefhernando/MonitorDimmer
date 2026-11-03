using System.IO;
using System.Windows;
using System.Windows.Media;
using Shadeplay.Core;
using Shadeplay.Core.Rgb;
using Shadeplay.Platform;
using Shadeplay.UI.Controls;

namespace Shadeplay.UI;

/// <summary>
/// First-run screen: shows what was found on this PC and suggests settings to match.
/// Nothing changes until "Apply and finish".
/// </summary>
public partial class WelcomeWindow : Window
{
    public sealed class MonitorRow : ObservableObject
    {
        string badge = "Checking…";
        System.Windows.Media.Brush? badgeBrush;

        public MonitorRow(string name, string detail)
        {
            Name = name;
            Detail = detail;
        }

        public string Name { get; }
        public string Detail { get; }
        /// <summary>How this monitor can be dimmed: brightness control, overlay, or still checking.</summary>
        public string Badge { get => badge; set => Set(ref badge, value); }
        public System.Windows.Media.Brush? BadgeBrush { get => badgeBrush; set => Set(ref badgeBrush, value); }
    }

    public sealed class Suggestion : ObservableObject
    {
        string detail;
        bool isChecked;
        bool isAvailable = true;

        public Suggestion(string title, string detail, bool isChecked, Action<AppSettings, bool> apply)
        {
            Title = title;
            this.detail = detail;
            this.isChecked = isChecked;
            Apply = apply;
        }

        public string Title { get; }
        public string Detail { get => detail; set => Set(ref detail, value); }
        public bool IsChecked { get => isChecked; set => Set(ref isChecked, value); }
        public bool IsAvailable { get => isAvailable; set => Set(ref isAvailable, value); }
        /// <summary>Writes the choice into the settings: (settings, checked).</summary>
        public Action<AppSettings, bool> Apply { get; }
    }

    readonly AppSettings settings;
    readonly DimController controller;
    readonly List<MonitorInfo> monitors = Screens.GetAll();
    readonly Dictionary<string, MonitorRow> monitorRows = new();
    readonly List<Suggestion> suggestions = new();
    Suggestion? brightnessSuggestion;

    public WelcomeWindow(AppSettings settings, DimController controller)
    {
        InitializeComponent();
        ThemeManager.Track(this);
        this.settings = settings;
        this.controller = controller;
        Icon = AppIcon.ImageSource;

        foreach (var m in monitors)
        {
            string name = m.FriendlyName.Length > 0 ? $"Display {m.Number} · {m.FriendlyName}" : $"Display {m.Number}";
            string detail = $"{m.Bounds.Width} × {m.Bounds.Height}" + (m.IsPrimary ? " · primary" : " · side monitor");
            monitorRows[m.Id] = new MonitorRow(name, detail) { BadgeBrush = (System.Windows.Media.Brush)FindResource("TextMuted") };
        }
        MonitorList.ItemsSource = monitorRows.Values.ToList();
        Map.Tiles = monitors.Select(m => new MonitorTile(m.Bounds, $"Display {m.Number}",
            m.IsPrimary ? "PRIMARY" : "SIDE", m.FriendlyName, MonitorTileState.Normal)).ToList();
        System.Windows.Automation.AutomationProperties.SetName(Map,
            "Your monitors: " + string.Join("; ", monitors.Select(m => $"Display {m.Number}, {m.FriendlyName}")));

        BuildSuggestions();
        SuggestionList.ItemsSource = suggestions;

        controller.Brightness.SupportChanged += OnSupportChanged;
        controller.Brightness.Detect(monitors);
        Closed += (_, _) => controller.Brightness.SupportChanged -= OnSupportChanged;
        Loaded += async (_, _) => await DetectRazerAsync();
    }

    void BuildSuggestions()
    {
        // Games
        var games = GameDetector.InstalledGames;
        string gameDetail = games.Count == 0
            ? "No game libraries found yet. Games Windows Game Bar recognises are still detected."
            : $"Found {games.Count} installed games, e.g. {string.Join(", ", games.Take(3).Select(g => g.Name))}.";
        suggestions.Add(new Suggestion("Dim automatically when a game goes fullscreen", gameDetail, true,
            (s, on) => s.AutoDetectGames = on));

        // Real brightness, for monitors that support it (filled in once checked)
        brightnessSuggestion = new Suggestion("Turn down your side monitors' own brightness",
            "Checking which monitors support it…", false,
            (s, on) => s.DimMethod = on ? DimMethod.Brightness : DimMethod.Overlay)
        { IsAvailable = false };
        suggestions.Add(brightnessSuggestion);

        // Razer, filled in by DetectRazerAsync
        suggestions.Add(new Suggestion("Dim your Razer lighting while playing", "Looking for Razer Synapse…", false,
            (s, on) =>
            {
                s.RgbEnabled = on;
                s.UseRazerChroma = on;
            })
        { IsAvailable = false });

        // Wallpaper Engine
        string? wallpaperEngine = ActionRunner.FindRunningWallpaperEngine();
        suggestions.Add(new Suggestion("Pause Wallpaper Engine while playing",
            wallpaperEngine != null
                ? "Wallpaper Engine is running. Pausing it gives the game the GPU time it was using."
                : "Wallpaper Engine is not running right now. If you use it, this pauses it during games.",
            wallpaperEngine != null, (s, on) => s.PauseWallpaperEngine = on));

        // Rainmeter
        bool rainmeter = ActionRunner.FindRunning("Rainmeter") != null
            || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rainmeter", "Rainmeter.exe"));
        suggestions.Add(new Suggestion("Hide Rainmeter skins while playing",
            rainmeter ? "Rainmeter is installed." : "Rainmeter was not found; this only acts if it runs.",
            rainmeter, (s, on) => s.HideRainmeter = on));

        // Discord and OBS stay visible
        var exceptionNames = settings.ExceptionApps.Select(WindowScanner.NormalizeExe).ToHashSet();
        suggestions.Add(new Suggestion("Keep Discord and OBS visible on dimmed monitors",
            "They are cut out of the dimming and never start it.", true,
            (s, on) =>
            {
                foreach (string app in new[] { "discord.exe", "obs64.exe" })
                {
                    if (on && !exceptionNames.Contains(app)) s.ExceptionApps.Add(app);
                    if (!on && s.ExceptionApps.FirstOrDefault(a => WindowScanner.NormalizeExe(a) == app) is { } existing)
                        s.ExceptionApps.Remove(existing);
                }
            }));

        suggestions.Add(new Suggestion("Start with Windows",
            "Runs quietly in the tray from the moment you sign in, so it's ready when you play.", true,
            (s, on) => s.StartWithWindows = on));

        suggestions.Add(new Suggestion("Show notifications",
            "\"Dimming for Elden Ring\" when a game starts, how long you played when it ends, and when an update is ready.",
            true, (s, on) =>
            {
                s.NotifyOnDim = on;
                s.NotifyOnSessionEnd = on;
                s.NotifyOnUpdate = on;
            }));
    }

    void OnSupportChanged() => Dispatcher.BeginInvoke(new Action(ShowBrightnessSupport));

    void ShowBrightnessSupport()
    {
        foreach (var m in monitors)
        {
            var support = controller.Brightness.GetSupport(m.Id);
            var row = monitorRows[m.Id];
            row.Badge = support switch
            {
                null => "Checking…",
                { Supported: true } s => $"Brightness control · {s.Current}",
                _ => "Overlay only",
            };
            row.BadgeBrush = (System.Windows.Media.Brush)FindResource(support?.Supported == true ? "Accent" : "TextMuted");
        }

        if (brightnessSuggestion == null) return;
        if (monitors.Count < 2)
        {
            brightnessSuggestion.Detail = "Only one monitor is connected right now; this matters once you add another.";
            return;
        }
        var side = monitors.Where(m => !m.IsPrimary).ToList();
        if (side.Any(m => controller.Brightness.GetSupport(m.Id) == null)) return;
        var supported = side.Where(m => controller.Brightness.GetSupport(m.Id)?.Supported == true).ToList();

        brightnessSuggestion.IsAvailable = supported.Count > 0;
        brightnessSuggestion.IsChecked = supported.Count > 0;
        brightnessSuggestion.Detail = supported.Count > 0
            ? $"{string.Join(" and ", supported.Select(m => $"Display {m.Number}"))} can lower the real backlight, which looks better than a dark overlay. Monitors that can't use the overlay."
            : "None of your side monitors support it, so the dark overlay is used.";
    }

    async Task DetectRazerAsync()
    {
        var razer = suggestions[2];
        string? version = await RazerChroma.DetectAsync();
        razer.IsAvailable = version != null;
        razer.IsChecked = version != null;
        razer.Detail = version != null
            ? "Razer Synapse is running. Your keyboard, mouse and other Razer devices switch to a dim orange while you play, then back to your own lighting."
            : "Razer Synapse was not found. Other brands can be set up later on the Lighting tab (through OpenRGB).";
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateChromeForState();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateChromeForState();
    }

    void UpdateChromeForState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");

        if (!maximized)
        {
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Margin = new Thickness(0);
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
        int px = Interop.Native.GetSystemMetricsForDpi(Interop.Native.SM_CXSIZEFRAME, dpiValue)
               + Interop.Native.GetSystemMetricsForDpi(Interop.Native.SM_CXPADDEDBORDER, dpiValue);
        RootBorder.BorderThickness = new Thickness(0);
        RootBorder.Margin = new Thickness(px / dpi.DpiScaleX);
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in suggestions.Where(s => s.IsAvailable)) s.Apply(settings, s.IsChecked);
        settings.WelcomeDone = true;
        Log.Write("Welcome screen: suggestions applied");
        Close();
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        settings.WelcomeDone = true;
        Close();
    }
}
