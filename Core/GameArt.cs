using System.IO;
using System.Net.Http;

namespace Shadeplay.Core;

/// <param name="Cover">Portrait cover, about 600×900.</param>
/// <param name="Hero">Wide banner.</param>
/// <param name="Header">Small landscape image, about 460×215.</param>
public sealed record GameArtSet(string? Cover, string? Hero, string? Header)
{
    public bool HasCover => Cover != null;
    public bool HasHero => Hero != null;

    /// <summary>The best picture for a portrait slot, for a wide one and for a thumbnail.</summary>
    public string? BestCover => Cover ?? Header ?? Hero;
    public string? BestHero => Hero ?? Header ?? Cover;
    public string? BestThumb => Header ?? Hero ?? Cover;
}

/// <summary>
/// Finds art for a game: first in Steam's own local library cache, which needs no account or
/// network, then in what was downloaded from SteamGridDB with the user's own API key.
/// </summary>
public static class GameArt
{
    static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);
    const int MaxImageBytes = 20 * 1024 * 1024;

    static readonly object Gate = new();
    static readonly HashSet<string> Requested = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static Task queue = Task.CompletedTask;
    static Func<string> apiKey = () => "";

    static string Folder => Path.Combine(AppSettings.Folder, "art");

    /// <summary>Raised on a background thread when new art has been downloaded.</summary>
    public static event Action? Changed;

    /// <summary>Tells downloads where to read the SteamGridDB key; empty means no downloads.</summary>
    public static void Configure(Func<string> readApiKey) => apiKey = readApiKey;

    /// <summary>After the key changes: games that were skipped can be tried again.</summary>
    public static void KeyChanged()
    {
        lock (Gate) Requested.Clear();
        Changed?.Invoke();
    }

    /// <summary>Art for a game, from whichever of the name and paths identifies it.</summary>
    public static GameArtSet Find(string name, string exeName = "", string exePath = "")
    {
        var game = GameDetector.Identify(exePath)
            ?? GameDetector.FindByName(name)
            ?? (exeName.Length > 0 ? GameDetector.Identify(GameDetector.PathOfExe(exeName)) : null);

        string? cover = null, hero = null, header = null;
        if (game?.SteamAppId != null && GameDetector.SteamPath is string steam)
        {
            string cache = Path.Combine(steam, "appcache", "librarycache");
            cover = SteamFile(cache, game.SteamAppId, "library_600x900.jpg", "library_capsule.jpg");
            hero = SteamFile(cache, game.SteamAppId, "library_hero.jpg");
            header = SteamFile(cache, game.SteamAppId, "header.jpg", "library_header.jpg");
        }

        // Downloaded art fills what Steam doesn't have
        string key = GameDetector.NameKey(game?.Name ?? name);
        cover ??= Downloaded(key, "cover");
        hero ??= Downloaded(key, "hero");
        return new GameArtSet(cover, hero, header);
    }

    /// <summary>
    /// Downloads a game's missing art in the background, when the user has set a key. Only
    /// call this for games on screen: one request at a time, each game once per run, and a
    /// game with no art isn't asked for again for a week.
    /// </summary>
    public static void Request(string name, string? steamAppId = null)
    {
        string key = GameDetector.NameKey(name);
        if (key.Length == 0 || apiKey().Length == 0) return;
        lock (Gate)
        {
            if (!Requested.Add(key)) return;
            queue = queue.ContinueWith(_ => FetchAsync(name, key, steamAppId), TaskScheduler.Default).Unwrap();
        }
    }

    static async Task FetchAsync(string name, string key, string? steamAppId)
    {
        string missMarker = Path.Combine(Folder, key + ".none");
        try
        {
            if (File.Exists(missMarker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missMarker) < RetryMissingAfter) return;

            var client = new SteamGridDb(Http, apiKey());
            var (coverUrl, heroUrl) = await client.FindAsync(name, steamAppId);
            bool saved = false;
            if (coverUrl != null && Downloaded(key, "cover") == null) saved |= await SaveAsync(coverUrl, key, "cover");
            if (heroUrl != null && Downloaded(key, "hero") == null) saved |= await SaveAsync(heroUrl, key, "hero");

            Directory.CreateDirectory(Folder);
            if (saved)
            {
                File.Delete(missMarker);
                Changed?.Invoke();
            }
            else if (coverUrl == null && heroUrl == null)
            {
                File.WriteAllText(missMarker, "");
            }
        }
        catch (UnauthorizedAccessException)
        {
            Log.Write("SteamGridDB refused the API key");
        }
        catch (Exception ex)
        {
            Log.Write($"Could not get art for {name}: {ex.Message}");
        }
    }

    static async Task<bool> SaveAsync(string url, string key, string kind)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) return false;
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return false;

        Directory.CreateDirectory(Folder);
        string path = DownloadPath(key, kind);
        string tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, path, true);
        return true;
    }

    static string DownloadPath(string key, string kind) => Path.Combine(Folder, $"{key}-{kind}.img");

    static string? Downloaded(string key, string kind)
    {
        string path = DownloadPath(key, kind);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Steam keeps art in a folder per game, for newer games one level deeper in a hashed
    /// folder and under newer names. Older versions used one flat folder.
    /// </summary>
    static string? SteamFile(string cache, string appId, params string[] names)
    {
        string folder = Path.Combine(cache, appId);
        foreach (string file in names)
        {
            string direct = Path.Combine(folder, file);
            if (File.Exists(direct)) return direct;
            try
            {
                if (Directory.Exists(folder)
                    && Directory.EnumerateFiles(folder, file, SearchOption.AllDirectories).FirstOrDefault() is string nested)
                    return nested;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            string flat = Path.Combine(cache, $"{appId}_{file}");
            if (File.Exists(flat)) return flat;
        }
        return null;
    }
}
