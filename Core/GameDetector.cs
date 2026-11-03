using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MonitorDimmer.Core;

public sealed record DetectedGame(string Name, string Source);

/// <summary>
/// Recognises games by where they are installed (Steam, Epic, GOG, Ubisoft, Xbox, EA, Riot,
/// Battle.net folders) and by the list of games Windows Game Bar has seen.
/// </summary>
public static class GameDetector
{
    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);

    // Tools, launchers and helpers that live inside game folders but are not games
    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "wallpaper64.exe", "wallpaper32.exe", "wallpaperservice32.exe", "ui32.exe",
        "steam.exe", "steamwebhelper.exe", "steamservice.exe", "gameoverlayui.exe",
        "epicgameslauncher.exe", "epicwebhelper.exe", "galaxyclient.exe", "galaxyclient helper.exe",
        "upc.exe", "uplaywebcore.exe", "ubisoftconnect.exe", "eadesktop.exe", "battle.net.exe",
        "riotclientservices.exe", "riotclientux.exe", "unitycrashhandler64.exe", "crashreportclient.exe",
        "vc_redist.x64.exe", "vc_redist.x86.exe", "dxsetup.exe",
    };

    static readonly object Gate = new();
    static DateTime lastRefresh = DateTime.MinValue;
    static List<string> roots = new();
    static HashSet<string> gameBarExes = new(StringComparer.OrdinalIgnoreCase);
    static List<DetectedGame> installed = new();

    /// <summary>Installed games found in the launchers' libraries, for display.</summary>
    public static IReadOnlyList<DetectedGame> InstalledGames
    {
        get
        {
            EnsureFresh();
            lock (Gate) return installed;
        }
    }

    public static bool IsGame(string exePath)
    {
        if (exePath.Length == 0 || NotGames.Contains(Path.GetFileName(exePath))) return false;
        EnsureFresh();
        lock (Gate)
        {
            if (gameBarExes.Contains(exePath)) return true;
            foreach (string root in roots)
                if (exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static void Refresh()
    {
        var newRoots = new List<string>();
        var newInstalled = new List<DetectedGame>();
        var newGameBar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Try("Steam", () => ReadSteam(newRoots, newInstalled));
        Try("Epic", () => ReadEpic(newRoots, newInstalled));
        Try("GOG", () => ReadGog(newRoots, newInstalled));
        Try("Ubisoft", () => ReadUbisoft(newRoots, newInstalled));
        Try("Folders", () => ReadKnownFolders(newRoots));
        Try("Game Bar", () => ReadGameBar(newGameBar));

        lock (Gate)
        {
            roots = newRoots
                .Select(r => r.TrimEnd('\\', '/') + "\\")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            gameBarExes = newGameBar;
            installed = newInstalled
                .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            lastRefresh = DateTime.UtcNow;
        }
        Log.Write($"Game detection: {roots.Count} library folders, {installed.Count} installed games, {gameBarExes.Count} Game Bar games");
    }

    static bool refreshing;

    /// <summary>First use reads synchronously; later refreshes run in the background.</summary>
    static void EnsureFresh()
    {
        bool never, stale;
        lock (Gate)
        {
            never = lastRefresh == DateTime.MinValue;
            stale = !refreshing && DateTime.UtcNow - lastRefresh > RefreshEvery;
            if (stale) refreshing = true;
        }
        if (!stale) return;
        if (never)
        {
            Refresh();
            lock (Gate) refreshing = false;
            return;
        }
        Task.Run(() =>
        {
            Refresh();
            lock (Gate) refreshing = false;
        });
    }

    static void ReadSteam(List<string> rootsOut, List<DetectedGame> gamesOut)
    {
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam)) return;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(lib, "steamapps");
            string common = Path.Combine(apps, "common");
            if (!Directory.Exists(common)) continue;
            rootsOut.Add(common);

            foreach (string acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var name = Regex.Match(File.ReadAllText(acf), "\"name\"\\s+\"(.+?)\"");
                if (!name.Success) continue;
                string n = name.Groups[1].Value;
                // Runtimes and tools that Steam installs next to games
                if (n.Contains("Redistributable") || n.StartsWith("Steam Linux Runtime") || n.StartsWith("Proton")
                    || n == "Wallpaper Engine" || n == "Steamworks Common Redistributables") continue;
                gamesOut.Add(new DetectedGame(n, "Steam"));
            }
        }
    }

    static void ReadEpic(List<string> rootsOut, List<DetectedGame> gamesOut)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return;
        foreach (string item in Directory.EnumerateFiles(dir, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(item));
            var root = doc.RootElement;
            if (root.TryGetProperty("InstallLocation", out var loc) && loc.GetString() is { Length: > 0 } path)
                rootsOut.Add(path);
            if (root.TryGetProperty("DisplayName", out var dn) && dn.GetString() is { Length: > 0 } name)
                gamesOut.Add(new DetectedGame(name, "Epic"));
        }
    }

    static void ReadGog(List<string> rootsOut, List<DetectedGame> gamesOut)
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games == null) return;
        foreach (string id in games.GetSubKeyNames())
        {
            using var g = games.OpenSubKey(id);
            if (g?.GetValue("path") is string path && path.Length > 0) rootsOut.Add(path);
            if (g?.GetValue("gameName") is string name && name.Length > 0) gamesOut.Add(new DetectedGame(name, "GOG"));
        }
    }

    static void ReadUbisoft(List<string> rootsOut, List<DetectedGame> gamesOut)
    {
        using var installs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (installs == null) return;
        foreach (string id in installs.GetSubKeyNames())
        {
            using var g = installs.OpenSubKey(id);
            if (g?.GetValue("InstallDir") is not string path || path.Length == 0) continue;
            path = path.Replace('/', '\\');
            rootsOut.Add(path);
            gamesOut.Add(new DetectedGame(Path.GetFileName(path.TrimEnd('\\')), "Ubisoft"));
        }
    }

    /// <summary>Folders that game stores create on any drive.</summary>
    static void ReadKnownFolders(List<string> rootsOut)
    {
        var candidates = new List<string>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            string r = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(r, "XboxGames"));
            candidates.Add(Path.Combine(r, "Riot Games"));
            candidates.Add(Path.Combine(r, "Program Files", "EA Games"));
            candidates.Add(Path.Combine(r, "Program Files (x86)", "Origin Games"));
            candidates.Add(Path.Combine(r, "Program Files", "Epic Games"));
            candidates.Add(Path.Combine(r, "Program Files (x86)", "GOG Galaxy", "Games"));
            candidates.Add(Path.Combine(r, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games"));
        }
        rootsOut.AddRange(candidates.Where(Directory.Exists));
    }

    static void ReadGameBar(HashSet<string> exesOut)
    {
        using var children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
        if (children == null) return;
        foreach (string id in children.GetSubKeyNames())
        {
            using var c = children.OpenSubKey(id);
            if (c?.GetValue("MatchedExeFullPath") is string exe && exe.Length > 0
                && !NotGames.Contains(Path.GetFileName(exe)))
                exesOut.Add(exe);
        }
    }

    static void Try(string source, Action read)
    {
        try { read(); }
        catch (Exception ex) { Log.Write($"Game detection ({source}) failed: {ex.Message}"); }
    }
}
