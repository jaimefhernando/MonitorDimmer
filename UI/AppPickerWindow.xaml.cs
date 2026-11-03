using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shadeplay.Core;

namespace Shadeplay.UI;

public partial class AppPickerWindow : Window
{
    public sealed record RunningApp(string Name, string Title);

    readonly HashSet<string> alreadyListed;
    readonly List<RunningApp> extra;
    List<RunningApp> apps = new();

    /// <param name="extra">More entries to offer besides running apps, e.g. recently played games.</param>
    public AppPickerWindow(string title, IEnumerable<string> alreadyListed,
        IEnumerable<RunningApp>? extra = null, bool singleSelection = false)
    {
        InitializeComponent();
        Platform.ThemeManager.Track(this);
        Title = title;
        this.alreadyListed = alreadyListed.Select(WindowScanner.NormalizeExe).ToHashSet();
        this.extra = extra?.ToList() ?? new();
        AppList.SelectionMode = singleSelection ? SelectionMode.Single : SelectionMode.Extended;
        if (singleSelection)
            HintText.Text = "Apps with an open window right now"
                + (this.extra.Count > 0 ? ", and games you played recently" : "") + ". Pick one and choose Add.";
        LoadApps();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public List<RunningApp> SelectedApps { get; } = new();

    public List<string> SelectedNames => SelectedApps.Select(a => a.Name).ToList();

    void LoadApps()
    {
        var running = WindowScanner.Scan()
            .GroupBy(w => w.ProcessName)
            .Select(g => new RunningApp(g.Key, g.Select(w => w.Title).FirstOrDefault(t => t.Length > 0) ?? ""));

        apps = running
            .Concat(extra)
            .Where(a => !alreadyListed.Contains(WindowScanner.NormalizeExe(a.Name)))
            .GroupBy(a => WindowScanner.NormalizeExe(a.Name))
            .Select(g => g.First())
            .OrderBy(a => a.Name)
            .ToList();
        ApplyFilter();
    }

    void ApplyFilter()
    {
        string q = SearchBox.Text.Trim();
        AppList.ItemsSource = q.Length == 0
            ? apps
            : apps.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    void Refresh_Click(object sender, RoutedEventArgs e) => LoadApps();

    void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedApps.AddRange(AppList.SelectedItems.Cast<RunningApp>());
        DialogResult = SelectedApps.Count > 0;
    }

    void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem != null) Add_Click(sender, e);
    }
}
