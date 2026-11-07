using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Shadeplay.Core;

/// <param name="ExeName">"eldenring.exe".</param>
/// <param name="Name">What to show: the profile name, else the window title, else the exe.</param>
/// <param name="IsGame">Detected as a game or has a profile, as opposed to e.g. a video player.</param>
public sealed record PlaySession(string ExeName, string Name, bool IsGame, DateTime Start, DateTime End, string ExePath = "")
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Records how long each game or watched app kept the monitors dimmed. Finished sessions are
/// appended to sessions.jsonl; the open one is saved every minute so a crash loses at most that.
/// </summary>
public sealed class SessionTracker : IDisposable
{
    /// <summary>Shorter sessions (alt-tabbing through a fullscreen app) are not kept.</summary>
    static readonly TimeSpan MinimumLength = TimeSpan.FromSeconds(30);

    static string HistoryFile => Path.Combine(AppSettings.Folder, "sessions.jsonl");
    static string OpenFile => Path.Combine(AppSettings.Folder, "session-open.json");

    readonly DispatcherTimer checkpoint = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly object gate = new();
    PlaySession? open;

    public SessionTracker()
    {
        checkpoint.Tick += (_, _) => SaveOpen();
        RecoverUnfinished();
    }

    /// <summary>Raised after a session was added to the history.</summary>
    public event Action? HistoryChanged;

    public string? CurrentExe => open?.ExeName;

    public void Begin(string exeName, string name, bool isGame, string exePath = "")
    {
        End();
        var now = DateTime.Now;
        open = new PlaySession(exeName, name, isGame, now, now, exePath);
        SaveOpen();
        checkpoint.Start();
    }

    /// <returns>The session that just ended, or null when none was open.</returns>
    public PlaySession? End()
    {
        checkpoint.Stop();
        if (open == null) return null;
        var finished = open with { End = DateTime.Now };
        open = null;
        TryDelete(OpenFile);
        Append(finished);
        return finished;
    }

    /// <summary>All recorded sessions, oldest first, including the one in progress.</summary>
    public List<PlaySession> Load()
    {
        var result = new List<PlaySession>();
        lock (gate)
        {
            if (File.Exists(HistoryFile))
            {
                foreach (string line in File.ReadLines(HistoryFile))
                {
                    if (line.Length == 0) continue;
                    try
                    {
                        if (JsonSerializer.Deserialize<PlaySession>(line) is { } s) result.Add(s);
                    }
                    catch (JsonException)
                    {
                        // A line cut short by a crash; skip it
                    }
                }
            }
        }
        if (open != null) result.Add(open with { End = DateTime.Now });
        return result;
    }

    public void ClearHistory()
    {
        lock (gate) TryDelete(HistoryFile);
        HistoryChanged?.Invoke();
    }

    void Append(PlaySession s)
    {
        if (s.Duration < MinimumLength) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(HistoryFile, JsonSerializer.Serialize(s) + Environment.NewLine);
            }
            Log.Write($"Session: {s.Name} ({s.ExeName}) {s.Duration:h\\:mm\\:ss}");
        }
        catch (Exception ex)
        {
            Log.Write("Could not save session: " + ex.Message);
        }
        HistoryChanged?.Invoke();
    }

    void SaveOpen()
    {
        if (open == null) return;
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(OpenFile, JsonSerializer.Serialize(open with { End = DateTime.Now }));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save the open session: " + ex.Message);
        }
    }

    /// <summary>A session left open by a crash or power cut ends at its last checkpoint.</summary>
    void RecoverUnfinished()
    {
        if (!File.Exists(OpenFile)) return;
        try
        {
            if (JsonSerializer.Deserialize<PlaySession>(File.ReadAllText(OpenFile)) is { } s) Append(s);
        }
        catch (Exception ex)
        {
            Log.Write("Could not recover the last session: " + ex.Message);
        }
        TryDelete(OpenFile);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }

    public void Dispose() => End();

    /// <summary>"< 1 min", "45 min", "1 h 25 min".</summary>
    public static string Format(TimeSpan t) =>
        t.TotalMinutes < 1 ? "< 1 min"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes} min";
}
