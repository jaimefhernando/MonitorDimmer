using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shadeplay.Core;

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

    AppSettings settings = null!;
    DimController controller = null!;

    public ProfilesView()
    {
        InitializeComponent();
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

    /// <summary>Selects the profile for <paramref name="exe"/>, making one when there is none.</summary>
    public void ShowProfile(string exe, string suggestedName)
    {
        exe = WindowScanner.NormalizeExe(exe);
        if (exe.Length == 0) return;
        var profile = settings.FindProfile(exe);
        if (profile == null)
        {
            profile = new GameProfile { ExeName = exe, Name = suggestedName.Length > 0 ? suggestedName : Path.GetFileNameWithoutExtension(exe) };
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
            .Select(g => new AppPickerWindow.RunningApp(g.Key, $"{g.First().Name} (played {g.First().Start:d MMM})"));

        var picker = new AppPickerWindow("Add a game profile",
            settings.Profiles.Select(p => p.ExeName), recent, singleSelection: true)
        {
            Owner = Window.GetWindow(this),
        };
        if (picker.ShowDialog() != true || picker.SelectedApps.Count == 0) return;

        var app = picker.SelectedApps[0];
        ShowProfile(app.Name, SuggestName(app));
    }

    void UpdateEmptyState()
    {
        EmptyListText.Visibility = settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = ProfileList.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshMonitors();
        UpdateEmptyState();
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
