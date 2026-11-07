using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shadeplay.Core;
using Shadeplay.UI.Controls;

namespace Shadeplay.UI;

public partial class ProfilesView : UserControl
{
    /// <summary>One "keep this monitor bright" checkbox for the selected profile.</summary>
    public sealed class MonitorChoice
    {
        readonly GameProfile profile;

        public MonitorChoice(GameProfile profile, string id, string label)
        {
            this.profile = profile;
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }

        public bool IsChecked
        {
            get => profile.KeepBrightMonitorIds.Contains(Id);
            set
            {
                if (value && !IsChecked) profile.KeepBrightMonitorIds.Add(Id);
                else if (!value) profile.KeepBrightMonitorIds.Remove(Id);
            }
        }
    }

    /// <summary>A cover on the Recently played shelf.</summary>
    public sealed record RecentTile(ShelfGame Game, string Mono, string Meta, ImageSource? Cover, Visibility BadgeVisibility, string Hint,
        Visibility PlayVisibility)
    {
        public string Name => Game.Name;
        public string PlayName => "Play " + Game.Name;
    }

    /// <summary>An entry in a profile's scene list.</summary>
    public sealed record SceneChoice(string Id, string Name)
    {
        // The themed combo box shows the item itself in its closed state
        public override string ToString() => Name;
    }

    const int RecentCount = 6;

    AppSettings settings = null!;
    DimController controller = null!;
    ShelfGame? bannerGame;
    bool loadingScene;

    /// <summary>Raised when a game is opened or closed, so the page can hide what's below it.</summary>
    public event Action<bool>? DetailChanged;

    /// <summary>Raised by "See all games".</summary>
    public event Action? ShowAllRequested;

    public ProfilesView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshShelf();
        };
    }

    public void Init(AppSettings settings, DimController controller)
    {
        this.settings = settings;
        this.controller = controller;
        ProfileList.ItemsSource = settings.Profiles;
        settings.Profiles.CollectionChanged += OnProfilesChanged;
        controller.Sessions.HistoryChanged += OnArtOrHistoryChanged;
        controller.StateChanged += OnArtOrHistoryChanged;
        GameArt.Changed += OnArtOrHistoryChanged;
        settings.Changed += OnSettingsChanged;
        UpdateEmptyState();
        RefreshShelf();
    }

    void OnSettingsChanged(string name)
    {
        if (name == nameof(AppSettings.Scenes)) ShowScene();
    }

    /// <summary>Fills the selected profile's scene list and picks its scene.</summary>
    void ShowScene()
    {
        loadingScene = true;
        try
        {
            var choices = new List<SceneChoice> { new("", "Whatever scene is picked") };
            choices.AddRange(settings.Scenes.Select(s => new SceneChoice(s.Id, s.Name)));
            SceneBox.ItemsSource = choices;
            string id = (ProfileList.SelectedItem as GameProfile)?.SceneId ?? "";
            SceneBox.SelectedValue = choices.Any(c => c.Id == id) ? id : "";
        }
        finally
        {
            loadingScene = false;
        }
    }

    void SceneBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loadingScene && ProfileList.SelectedItem is GameProfile p && SceneBox.SelectedValue is string id) p.SceneId = id;
    }

    void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        RefreshShelf();
    }

    void OnArtOrHistoryChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        ProfileList.Items.Refresh();
        ShowDetailArt();
        RefreshShelf();
    }));

    /// <summary>Rebuilds the banner and the Recently played shelf.</summary>
    public void RefreshShelf()
    {
        if (controller == null) return;
        var recent = GameShelf.Recent(controller.Sessions.Load(), RecentCount);

        // The game being played goes first, whether or not it has been recorded yet
        ShelfGame? running = null;
        if (controller.SessionName != null)
        {
            string path = controller.SessionExePath ?? "";
            var game = GameDetector.Identify(path);
            running = recent.FirstOrDefault(g => g.Game != null && g.Game == game)
                ?? new ShelfGame(controller.SessionName, controller.SessionExe ?? "", path, DateTime.Now, null, game);
        }
        bannerGame = running ?? recent.FirstOrDefault();

        Banner.Visibility = bannerGame != null ? Visibility.Visible : Visibility.Collapsed;
        if (bannerGame != null)
        {
            var art = FindArt(bannerGame);
            BannerArt.Source = ArtImages.Load(art.BestHero, 1280);
            Tracking.SetText(BannerKicker, running != null ? "NOW PLAYING" : "LAST PLAYED");
            BannerTitle.Text = bannerGame.Name;
            BannerDetail.Text = running != null ? controller.Status : Describe(bannerGame);
            BannerButton.Content = ProfileFor(bannerGame) != null ? "Edit profile" : "Make a profile";
            BannerPlay.Visibility = running == null && GameLauncher.CanLaunch(bannerGame.Game, bannerGame.ExePath)
                ? Visibility.Visible : Visibility.Collapsed;
            ResetPlay(BannerPlay);
        }

        var tiles = recent.Select(g => new RecentTile(g, MonogramConverter.Of(g.Name), Describe(g),
            ArtImages.Load(FindArt(g).BestCover, 264),
            ProfileFor(g) != null ? Visibility.Visible : Visibility.Collapsed,
            ProfileFor(g) != null ? "Edit profile" : "Make a profile",
            controller.SessionName == null && GameLauncher.CanLaunch(g.Game, g.ExePath) ? Visibility.Visible : Visibility.Collapsed)).ToList();
        RecentList.ItemsSource = tiles;
        RecentShelf.Visibility = tiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        int more = GameDetector.InstalledGames.Count - tiles.Count(t => t.Game.Game != null);
        MoreBar.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreTitle.Text = more == 1 ? "1 more installed game" : $"{more} more installed games";
    }

    static string Describe(ShelfGame g) => g.Duration is TimeSpan d
        ? $"{GameShelf.When(g.LastPlayed)} · {SessionTracker.Format(d)}"
        : GameShelf.When(g.LastPlayed);

    /// <summary>Art for a game, asking for what's missing now that it's on screen.</summary>
    static GameArtSet FindArt(ShelfGame g)
    {
        var art = GameArt.Find(g.Name, g.ExeName, g.ExePath);
        if (!art.HasCover || !art.HasHero) GameArt.Request(g.Name, g.Game?.SteamAppId);
        return art;
    }

    GameProfile? ProfileFor(ShelfGame g)
    {
        string exe = WindowScanner.NormalizeExe(g.ExeName);
        string name = GameDetector.NameKey(g.Name);
        return settings.Profiles.FirstOrDefault(p => (exe.Length > 0 && p.ExeName == exe) || GameDetector.NameKey(p.Name) == name);
    }

    /// <summary>Opens a game's profile, making one from its detected program if needed.</summary>
    void Open(ShelfGame g)
    {
        if (ProfileFor(g) is { } existing)
        {
            ProfileList.SelectedItem = existing;
            return;
        }
        string path = g.ExePath.Length > 0 ? g.ExePath : g.Game != null ? GameDetector.MainExe(g.Game) ?? "" : "";
        string exe = g.ExeName.Length > 0 ? g.ExeName : Path.GetFileName(path);
        if (exe.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this)!, $"Shadeplay couldn't find {g.Name}'s program. Start the game, then use Add game.",
                "Make a profile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowProfile(exe, g.Name, path);
    }

    void Banner_Click(object sender, RoutedEventArgs e)
    {
        if (bannerGame != null) Open(bannerGame);
    }

    void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecentTile tile }) Open(tile.Game);
    }

    void Back_Click(object sender, RoutedEventArgs e) => ProfileList.SelectedItem = null;

    void BannerPlay_Click(object sender, RoutedEventArgs e)
    {
        if (bannerGame != null) Play(BannerPlay, bannerGame.Game, bannerGame.ExePath);
    }

    void RecentPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecentTile tile } button) Play(button, tile.Game.Game, tile.Game.ExePath);
    }

    void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is GameProfile p) Play(DetailPlay, GameOf(p), p.ExePath);
    }

    static DetectedGame? GameOf(GameProfile p) =>
        GameDetector.Identify(p.ExePath) ?? GameDetector.FindByName(p.Name)
        ?? GameDetector.Identify(GameDetector.PathOfExe(p.ExeName));

    /// <summary>Starts the game; the button says so for a few seconds, since games take a while to show.</summary>
    void Play(Button button, DetectedGame? game, string exePath)
    {
        if (!GameLauncher.Launch(game, exePath))
        {
            MessageBox.Show(Window.GetWindow(this)!, "Shadeplay couldn't start this game. Start it from its launcher instead.",
                "Play", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        button.IsEnabled = false;
        if (button.Content is StackPanel { Children: [_, TextBlock label] }) label.Text = "Starting…";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ResetPlay(button);
        };
        timer.Start();
    }

    static void ResetPlay(Button button)
    {
        button.IsEnabled = true;
        if (button.Content is StackPanel { Children: [_, TextBlock label] }) label.Text = "Play";
    }

    void SeeAll_Click(object sender, RoutedEventArgs e) => ShowAllRequested?.Invoke();

    /// <summary>The tile list never scrolls itself, so the wheel scrolls the page.</summary>
    void ProfileList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ((UIElement)((FrameworkElement)sender).Parent).RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
        });
    }

    void ShowDetailArt()
    {
        if (ProfileList.SelectedItem is not GameProfile p)
        {
            DetailHero.Source = null;
            return;
        }
        var art = GameArt.Find(p.Name, p.ExeName, p.ExePath);
        if (!art.HasCover || !art.HasHero) GameArt.Request(p.Name, GameDetector.FindByName(p.Name)?.SteamAppId);
        DetailHero.Source = ArtImages.Load(art.BestHero, 1280);
        DetailHeroBox.Visibility = DetailHero.Source != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Releases event subscriptions. Called by the host window on close.</summary>
    public void Detach()
    {
        if (settings != null)
        {
            settings.Profiles.CollectionChanged -= OnProfilesChanged;
            settings.Changed -= OnSettingsChanged;
        }
        if (controller != null)
        {
            controller.Sessions.HistoryChanged -= OnArtOrHistoryChanged;
            controller.StateChanged -= OnArtOrHistoryChanged;
        }
        GameArt.Changed -= OnArtOrHistoryChanged;
    }

    /// <summary>Rebuilds the monitor checkboxes, e.g. after monitors were plugged in.</summary>
    public void RefreshMonitors()
    {
        if (ProfileList.SelectedItem is not GameProfile p)
        {
            KeepBrightList.ItemsSource = null;
            return;
        }
        KeepBrightList.ItemsSource = Screens.GetAll()
            .Select(m => new MonitorChoice(p, m.Id, m.Description))
            .ToList();
    }

    /// <summary>Selects the profile for <paramref name="exe"/>, making one when there is none.</summary>
    public void ShowProfile(string exe, string suggestedName, string exePath = "")
    {
        exe = WindowScanner.NormalizeExe(exe);
        if (exe.Length == 0) return;
        var profile = settings.FindProfile(exe);
        if (profile == null)
        {
            profile = new GameProfile { ExeName = exe, ExePath = exePath, Name = suggestedName.Length > 0 ? suggestedName : Path.GetFileNameWithoutExtension(exe) };
            settings.Profiles.Add(profile);
        }
        ProfileList.SelectedItem = profile;
        ProfileList.ScrollIntoView(profile);
    }

    /// <summary>Picks a running or recently played app and makes a profile for it.</summary>
    public void AddGame()
    {
        // Recently played games are offered too, so a profile can be made without the game running
        var recent = controller.Sessions.Load()
            .OrderByDescending(s => s.Start)
            .GroupBy(s => s.ExeName)
            .Select(g => new AppPickerWindow.RunningApp(g.Key, $"{g.First().Name} (played {g.First().Start:d MMM})", g.First().ExePath));

        var picker = new AppPickerWindow("Add a game profile",
            settings.Profiles.Select(p => p.ExeName), recent, singleSelection: true)
        {
            Owner = Window.GetWindow(this),
        };
        if (picker.ShowDialog() != true || picker.SelectedApps.Count == 0) return;

        var app = picker.SelectedApps[0];
        ShowProfile(app.Name, SuggestName(app), app.ExePath);
    }

    void UpdateEmptyState()
    {
        EmptyListText.Visibility = settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool open = ProfileList.SelectedItem != null;
        DetailPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ShelfPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        DetailChanged?.Invoke(open);
    }

    void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshMonitors();
        UpdateEmptyState();
        ShowDetailArt();
        ShowScene();
        if (ProfileList.SelectedItem is GameProfile p)
        {
            DetailPlay.Visibility = controller.SessionName == null && GameLauncher.CanLaunch(GameOf(p), p.ExePath)
                ? Visibility.Visible : Visibility.Collapsed;
            ResetPlay(DetailPlay);
        }
    }

    static string SuggestName(AppPickerWindow.RunningApp app)
    {
        string title = app.Title;
        int played = title.IndexOf(" (played ", StringComparison.Ordinal);
        if (played > 0) title = title[..played];
        title = title.Trim();
        return title.Length is > 0 and <= 40 ? title : Path.GetFileNameWithoutExtension(app.Name);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not GameProfile p) return;
        ProfileList.SelectedItem = null;
        settings.Profiles.Remove(p);
    }
}
