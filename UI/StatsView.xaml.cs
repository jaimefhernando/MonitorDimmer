using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Shadeplay.Core;

namespace Shadeplay.UI;

public partial class StatsView : UserControl
{
    public sealed record RangeOption(string Label, int? Days)
    {
        public override string ToString() => Label;
    }

    public sealed record GameRow(string Name, string ExeName, string Time, GridLength Bar, GridLength Rest);
    public sealed record DayColumn(string Label, double Height, string Tip);
    public sealed record SessionRow(string Name, string When, string Time);

    static readonly RangeOption[] Ranges =
    {
        new("Last 7 days", 7),
        new("Last 30 days", 30),
        new("All time", null),
    };

    SessionTracker sessions = null!;

    public StatsView()
    {
        InitializeComponent();
        RangeBox.ItemsSource = Ranges;
        RangeBox.SelectedIndex = 0;
        // Includes the session in progress whenever the tab is opened
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    public void Init(SessionTracker tracker)
    {
        sessions = tracker;
        sessions.HistoryChanged += OnHistoryChanged;
        Refresh();
    }

    void OnHistoryChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    /// <summary>Releases event subscriptions. Called by the host window on close.</summary>
    public void Detach()
    {
        if (sessions != null) sessions.HistoryChanged -= OnHistoryChanged;
    }

    void Filter_Changed(object sender, RoutedEventArgs e) => Refresh();

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            "Delete all recorded play sessions? This cannot be undone.", "Clear history",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) sessions.ClearHistory();
    }

    void Refresh()
    {
        if (sessions == null || RangeBox.SelectedItem is not RangeOption range) return;

        var all = sessions.Load();
        if (GamesOnlyCheck.IsChecked == true) all = all.Where(s => s.IsGame).ToList();

        var since = range.Days is int rangeDays ? DateTime.Today.AddDays(1 - rangeDays) : DateTime.MinValue;
        var inRange = all.Where(s => s.End >= since).ToList();

        // Summary
        var total = Sum(inRange);
        TotalText.Text = Format(total);
        CountText.Text = inRange.Count.ToString(CultureInfo.CurrentCulture);
        LongestText.Text = inRange.Count > 0 ? Format(inRange.Max(s => s.Duration)) : "–";

        var byGame = inRange
            .GroupBy(s => s.ExeName)
            .Select(g => (Name: g.OrderByDescending(s => s.Start).First().Name, Exe: g.Key, Time: Sum(g)))
            .OrderByDescending(g => g.Time)
            .ToList();
        TopText.Text = byGame.Count > 0 ? byGame[0].Name : "–";

        bool empty = inRange.Count == 0;
        EmptyText.Text = empty
            ? "Nothing recorded in this period yet. Sessions appear here after a game has dimmed your monitors for at least 30 seconds."
            : "";
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ByGameCard.Visibility = RecentCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        // Per game, the longest bar is the most played game
        double max = byGame.Count > 0 ? byGame[0].Time.TotalSeconds : 1;
        ByGameList.ItemsSource = byGame.Take(10).Select(g =>
        {
            double share = max > 0 ? g.Time.TotalSeconds / max : 0;
            return new GameRow(g.Name, g.Exe, Format(g.Time),
                new GridLength(Math.Max(share, 0.001), GridUnitType.Star),
                new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star));
        }).ToList();

        // Last 14 days, regardless of the range above
        var days = Enumerable.Range(0, 14).Select(i => DateTime.Today.AddDays(i - 13)).ToList();
        var perDay = days.Select(d => (Day: d, Time: Sum(all.Where(s => s.Start.Date == d)))).ToList();
        double dayMax = Math.Max(perDay.Max(d => d.Time.TotalMinutes), 1);
        ByDayList.ItemsSource = perDay.Select(d => new DayColumn(
            d.Day.ToString("ddd", CultureInfo.CurrentCulture)[..2],
            d.Time.TotalMinutes / dayMax * 90,
            $"{d.Day:dddd d MMM}: {Format(d.Time)}")).ToList();

        RecentList.ItemsSource = inRange
            .OrderByDescending(s => s.Start)
            .Take(15)
            .Select(s => new SessionRow(s.Name, s.Start.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture), Format(s.Duration)))
            .ToList();
    }

    static TimeSpan Sum(IEnumerable<PlaySession> list) =>
        TimeSpan.FromSeconds(list.Sum(s => s.Duration.TotalSeconds));

    static string Format(TimeSpan t) => SessionTracker.Format(t);
}
