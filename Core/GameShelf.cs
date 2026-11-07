namespace Shadeplay.Core;

/// <param name="Game">The installed game, when it could be identified.</param>
/// <param name="LastPlayed">Latest of Shadeplay's sessions and the store's own record.</param>
/// <param name="Duration">Length of the last recorded session, when Shadeplay saw it.</param>
public sealed record ShelfGame(
    string Name, string ExeName, string ExePath, DateTime LastPlayed, TimeSpan? Duration, DetectedGame? Game);

/// <summary>
/// The games worth showing first: Shadeplay's own play history, topped up with the last-played
/// dates Steam keeps, so the shelves are useful from the first run.
/// </summary>
public static class GameShelf
{
    // Steam's list has no way to tell these apart from games
    static readonly HashSet<string> SteamTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "3DMark", "Aseprite", "Blender", "DisplayFusion", "Lossless Scaling", "OBS Studio", "Soundpad",
        "SteamVR", "VTube Studio",
    };

    /// <summary>"Today", "Yesterday", "Sat" within a week, else "3 Nov".</summary>
    public static string When(DateTime time)
    {
        var days = (DateTime.Today - time.Date).Days;
        return days <= 0 ? "Today" : days == 1 ? "Yesterday" : days < 7 ? time.ToString("ddd") : time.ToString("d MMM");
    }

    public static List<ShelfGame> Recent(IEnumerable<PlaySession> sessions, int count)
    {
        var shelf = new Dictionary<string, ShelfGame>();

        foreach (var s in sessions.Where(s => s.IsGame).OrderByDescending(s => s.Start))
        {
            var game = GameDetector.Identify(s.ExePath) ?? GameDetector.FindByName(s.Name);
            string key = GameDetector.NameKey(game?.Name ?? s.Name);
            if (key.Length == 0 || shelf.ContainsKey(key)) continue;
            shelf[key] = new ShelfGame(game?.Name ?? s.Name, s.ExeName, s.ExePath, s.Start, s.Duration, game);
        }

        foreach (var g in GameDetector.InstalledGames.Where(g => g.LastPlayed != null && !SteamTools.Contains(g.Name)))
        {
            string key = GameDetector.NameKey(g.Name);
            if (key.Length == 0) continue;
            if (shelf.TryGetValue(key, out var seen) && seen.LastPlayed >= g.LastPlayed) continue;
            shelf[key] = new ShelfGame(g.Name, seen?.ExeName ?? "", seen?.ExePath ?? "", g.LastPlayed!.Value, seen?.Duration, g);
        }

        return shelf.Values.OrderByDescending(g => g.LastPlayed).Take(count).ToList();
    }
}
