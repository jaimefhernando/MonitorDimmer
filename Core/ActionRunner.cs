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

    public void DimStarted(SessionPlan plan)
    {
        Enqueue(async () =>
        {
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
        Enqueue(async () =>
        {
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
