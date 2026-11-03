using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using MonitorDimmer.Core.Rgb;

namespace MonitorDimmer.Core;

/// <summary>
/// Things to do when dimming starts and ends: built-in actions, RGB lighting and the user's own
/// commands. Everything runs in order on a background queue so the UI never waits on it.
/// </summary>
internal sealed class ActionRunner : IDisposable
{
    const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    readonly RgbLighting rgb = new();
    readonly object queueGate = new();
    Task queue = Task.CompletedTask;

    // What was changed at the start, so the end only undoes that
    string? pausedWallpaperEngine;
    string? hiddenRainmeter;
    string? previousPowerPlan;
    string? closedOneDrive;
    RgbLighting.Options? appliedRgb;
    (int Pid, ProcessPriorityClass Previous)? boosted;
    readonly List<string> closedApps = new();

    /// <param name="gamePid">The process that started the session; null when dimmed by hand.</param>
    public void DimStarted(SessionPlan plan, int? gamePid)
    {
        Enqueue(async () =>
        {
            if (plan.BoostPriority && gamePid is int pid) Boost(pid);
            if (plan.CloseApps.Count > 0) await CloseAppsAsync(plan.CloseApps, gamePid);
            if (plan.WallpaperEngine && FindRunning("wallpaper64", "wallpaper32") is string we && Run(we, "-control pause"))
                pausedWallpaperEngine = we;
            if (plan.Rainmeter && FindRunning("Rainmeter") is string rm && Run(rm, "!HideFade *"))
                hiddenRainmeter = rm;
            if (plan.Power) SwitchToHighPerformance();
            if (plan.OneDrive && FindRunning("OneDrive") is string od && Run(od, "/shutdown"))
                closedOneDrive = od;
            if (plan.Rgb)
            {
                await rgb.ApplyAsync(plan.RgbOptions);
                appliedRgb = plan.RgbOptions;
            }
            RunCommands(plan.OnDimCommands);
        });
    }

    public void DimEnded(SessionPlan plan)
    {
        string commands = plan.OnRestoreCommands;
        bool reopen = plan.ReopenClosedApps;
        Enqueue(async () =>
        {
            Unboost();
            if (reopen)
                foreach (string app in closedApps) Start(app);
            closedApps.Clear();
            if (pausedWallpaperEngine != null) Run(pausedWallpaperEngine, "-control play");
            if (hiddenRainmeter != null) Run(hiddenRainmeter, "!ShowFade *");
            if (previousPowerPlan != null) Run("powercfg.exe", "/setactive " + previousPowerPlan);
            if (closedOneDrive != null) Run(closedOneDrive, "/background");
            if (appliedRgb != null) await rgb.RestoreAsync(appliedRgb);
            pausedWallpaperEngine = hiddenRainmeter = previousPowerPlan = closedOneDrive = null;
            appliedRgb = null;
            RunCommands(commands);
        });
    }

    /// <summary>Shows the dimming lighting for a few seconds, unless dimming is on anyway.</summary>
    public void PreviewRgb(SessionPlan plan, bool dimmingActive)
    {
        var options = plan.RgbOptions;
        Enqueue(async () =>
        {
            await rgb.ApplyAsync(options);
            await Task.Delay(3000);
            if (dimmingActive && appliedRgb != null) await rgb.ApplyAsync(appliedRgb);
            else await rgb.RestoreAsync(options);
        });
    }

    /// <summary>Waits for queued actions, e.g. so lighting and Wallpaper Engine are restored before exit.</summary>
    public void Flush(TimeSpan timeout)
    {
        Task current;
        lock (queueGate) current = queue;
        current.Wait(timeout);
    }

    /// <summary>Path of a running process by name (without .exe), or null.</summary>
    public static string? FindRunning(params string[] names)
    {
        foreach (string name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    string path = Interop.Native.GetProcessPath((uint)p.Id);
                    if (path.Length > 0 && File.Exists(path)) return path;
                }
            }
        }
        return null;
    }

    public static string? FindRunningWallpaperEngine() => FindRunning("wallpaper64", "wallpaper32");

    /// <summary>Normal to Above normal; never touches a priority the game or the user already chose.</summary>
    void Boost(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.PriorityClass != ProcessPriorityClass.Normal) return;
            p.PriorityClass = ProcessPriorityClass.AboveNormal;
            boosted = (pid, ProcessPriorityClass.Normal);
            Log.Write($"Priority: {p.ProcessName} raised to Above normal");
        }
        catch (Exception ex)
        {
            // Games running as administrator or with anti-cheat refuse this
            Log.Write($"Priority: could not raise process {pid}: {ex.Message}");
        }
    }

    void Unboost()
    {
        if (boosted is not var (pid, previous)) return;
        boosted = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.PriorityClass == ProcessPriorityClass.AboveNormal) p.PriorityClass = previous;
        }
        catch (Exception)
        {
            // The game has already exited
        }
    }

    /// <summary>Asks each listed app to close, ends it if it is still running after 3 seconds.</summary>
    async Task CloseAppsAsync(IReadOnlyList<string> names, int? gamePid)
    {
        // The launcher that started the game (Steam, Epic...) may be on the list; closing it
        // could end the game or its overlay, so the game's parents are always left alone
        var protectedIds = gamePid is int g ? Interop.Native.GetAncestorProcessIds(g) : new HashSet<int>();
        if (gamePid is int self) protectedIds.Add(self);
        protectedIds.Add(Environment.ProcessId);

        foreach (string exe in names)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                using (p)
                {
                    if (protectedIds.Contains(p.Id))
                    {
                        Log.Write($"Left {exe} running: the game was started by it");
                        continue;
                    }
                    try
                    {
                        string path = Interop.Native.GetProcessPath((uint)p.Id);
                        p.CloseMainWindow();
                        if (!await WaitForExitAsync(p, TimeSpan.FromSeconds(3)))
                        {
                            // Launchers and updaters often live in the tray without a window to close
                            p.Kill(); // only this process: a child of it could be something you need
                            await WaitForExitAsync(p, TimeSpan.FromSeconds(2));
                        }
                        if (path.Length > 0 && !closedApps.Contains(path, StringComparer.OrdinalIgnoreCase))
                            closedApps.Add(path);
                        Log.Write($"Closed {exe} while playing");
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Could not close {exe}: {ex.Message}");
                    }
                }
            }
        }
    }

    static async Task<bool> WaitForExitAsync(Process p, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    static void Start(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Log.Write($"Reopened {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log.Write($"Could not reopen {path}: {ex.Message}");
        }
    }

    void SwitchToHighPerformance()
    {
        string active = Output("powercfg.exe", "/getactivescheme");
        var guid = Regex.Match(active, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (!guid.Success || guid.Value.Equals(HighPerformancePlan, StringComparison.OrdinalIgnoreCase)) return;
        Output("powercfg.exe", "/setactive " + HighPerformancePlan, out int exitCode);
        if (exitCode == 0)
            previousPowerPlan = guid.Value;
        else
            Log.Write("The High performance power plan is not available on this PC");
    }

    void Enqueue(Func<Task> work)
    {
        lock (queueGate)
        {
            queue = queue.ContinueWith(async _ =>
            {
                try { await work(); }
                catch (Exception ex) { Log.Write("Action failed: " + ex); }
            }, TaskScheduler.Default).Unwrap();
        }
    }

    static void RunCommands(string commands)
    {
        foreach (string raw in commands.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            Run("cmd.exe", "/c " + line);
        }
    }

    static bool Run(string file, string arguments)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Log.Write($"Ran: {file} {arguments}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Could not run {file} {arguments}: {ex.Message}");
            return false;
        }
    }

    static string Output(string file, string arguments) => Output(file, arguments, out _);

    static string Output(string file, string arguments, out int exitCode)
    {
        exitCode = -1;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (p == null) return "";
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            exitCode = p.ExitCode;
            return text;
        }
        catch (Exception ex)
        {
            Log.Write($"Could not run {file} {arguments}: {ex.Message}");
            return "";
        }
    }

    public void Dispose() => rgb.Dispose();
}
