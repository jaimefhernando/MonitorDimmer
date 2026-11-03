using System.IO;
using System.Windows;
using System.Windows.Controls;
using MonitorDimmer.Core;

namespace MonitorDimmer.UI;

public partial class ProfilesView : UserControl
{
    public sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

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

    static readonly Option<ProfileDimMethod>[] Methods =
    {
        new(ProfileDimMethod.Default, "Same as the General tab"),
        new(ProfileDimMethod.Overlay, "Black overlay"),
        new(ProfileDimMethod.Brightness, "Monitor brightness (DDC/CI)"),
        new(ProfileDimMethod.Both, "Both"),
    };

    static readonly Option<ProfileLighting>[] Lightings =
    {
        new(ProfileLighting.Default, "Same as the Lighting tab"),
        new(ProfileLighting.Unchanged, "Leave my lighting alone"),
        new(ProfileLighting.Off, "Turn the lights off"),
        new(ProfileLighting.Color, "Use a dim color"),
    };

    static readonly Option<ProfileSwitch>[] MouseLocks =
    {
        new(ProfileSwitch.Default, "Same as the General tab"),
        new(ProfileSwitch.On, "Keep the mouse on the game's monitor"),
        new(ProfileSwitch.Off, "Off for this game"),
    };

    AppSettings settings = null!;
    DimController controller = null!;

    public ProfilesView()
    {
        InitializeComponent();
        MethodBox.ItemsSource = Methods;
        LightingBox.ItemsSource = Lightings;
        MouseLockBox.ItemsSource = MouseLocks;
    }

    public void Init(AppSettings settings, DimController controller)
    {
        this.settings = settings;
        this.controller = controller;
        ProfileList.ItemsSource = settings.Profiles;
        settings.Profiles.CollectionChanged += (_, _) => UpdateEmptyState();
        if (settings.Profiles.Count > 0) ProfileList.SelectedIndex = 0;
        UpdateEmptyState();
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

    void UpdateEmptyState()
    {
        EmptyListText.Visibility = settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool selected = ProfileList.SelectedItem != null;
        Editor.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        NoSelectionText.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
    }

    void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshMonitors();
        UpdateEmptyState();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        // Recently played games are offered too, so a profile can be made without the game running
        var recent = controller.Sessions.Load()
            .OrderByDescending(s => s.Start)
            .GroupBy(s => s.ExeName)
            .Select(g => new AppPickerWindow.RunningApp(g.Key, $"{g.First().Name} (played {g.First().Start:d MMM})"));

        var picker = new AppPickerWindow("Add a game profile",
            settings.Profiles.Select(p => p.ExeName), recent, singleSelection: true)
        {
            Owner = Window.GetWindow(this),
        };
        if (picker.ShowDialog() != true || picker.SelectedApps.Count == 0) return;

        var app = picker.SelectedApps[0];
        string exe = WindowScanner.NormalizeExe(app.Name);
        if (settings.FindProfile(exe) is { } existing)
        {
            ProfileList.SelectedItem = existing;
            return;
        }
        var profile = new GameProfile { ExeName = exe, Name = SuggestName(app) };
        settings.Profiles.Add(profile);
        ProfileList.SelectedItem = profile;
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
        int index = ProfileList.SelectedIndex;
        settings.Profiles.Remove(p);
        if (settings.Profiles.Count > 0) ProfileList.SelectedIndex = Math.Min(index, settings.Profiles.Count - 1);
        UpdateEmptyState();
    }
}
