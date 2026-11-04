using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Shadeplay.Core;

/// <summary>
/// Lowers and restores a monitor's real backlight over DDC/CI. Monitor commands take up to
/// half a second each, so they run in order on a background thread.
/// </summary>
public sealed class MonitorBrightness : IDisposable
{
    public sealed record Support(bool Supported, int Current);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetMonitorContrast(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetMonitorContrast(IntPtr monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

    const byte VcpSaturation = 0x8A;

    sealed class ExtraLevels
    {
        public uint? Contrast { get; set; }
        public uint? Saturation { get; set; }
    }

    static string ExtraRestoreFile => Path.Combine(AppSettings.Folder, "contrast-restore.json");

    static string RestoreFile => Path.Combine(AppSettings.Folder, "brightness-restore.json");

    readonly BlockingCollection<Action> work = new();
    readonly Thread thread;
    readonly ConcurrentDictionary<string, Support> support = new();

    // Worker thread only: brightness each monitor had before it was dimmed, by monitor id
    readonly Dictionary<string, uint> originals = new();
    readonly Dictionary<string, ExtraLevels> extraOriginals = new();

    /// <summary>Slower, gentler commands for monitors that hang on quick DDC/CI changes.</summary>
    public volatile bool OsdSafe;

    TimeSpan CommandGap => TimeSpan.FromMilliseconds(OsdSafe ? 400 : 100);
    int FadeSteps => OsdSafe ? 10 : 4;

    public MonitorBrightness()
    {
        thread = new Thread(() =>
        {
            foreach (var job in work.GetConsumingEnumerable())
            {
                try { job(); }
                catch (Exception ex) { Log.Write("Brightness: " + ex.Message); }
            }
        })
        { IsBackground = true, Name = "DDC/CI brightness" };
        thread.Start();
    }

    /// <summary>Raised (on the worker thread) when a monitor's support was checked.</summary>
    public event Action? SupportChanged;

    /// <summary>Null while not checked yet.</summary>
    public Support? GetSupport(string monitorId) => support.TryGetValue(monitorId, out var s) ? s : null;

    /// <summary>Checks which monitors accept brightness commands.</summary>
    public void Detect(IEnumerable<MonitorInfo> monitors)
    {
        foreach (var m in monitors.ToList())
        {
            work.Add(() =>
            {
                // While dimmed, report the level the monitor will go back to
                int? current = originals.TryGetValue(m.Id, out uint original)
                    ? (int)original
                    : WithPhysical(m.Handle, h => TryRead(h, out _, out uint c, out _) ? (int?)c : null);
                support[m.Id] = new Support(current != null, current ?? 0);
                SupportChanged?.Invoke();
            });
        }
    }

    /// <summary>
    /// Lowers the backlight by <paramref name="strength"/> percent of its current level, and
    /// optionally contrast and colour saturation by the given percentages.
    /// </summary>
    public void Dim(MonitorInfo m, int strength, int contrastCut = 0, int saturationCut = 0)
    {
        work.Add(() =>
        {
            WithPhysical(m.Handle, h =>
            {
                if (!TryRead(h, out uint min, out uint current, out _)) return (int?)null;
                // Keep the level from before the first dim, not an already dimmed one
                if (!originals.TryGetValue(m.Id, out uint original))
                {
                    original = current;
                    originals[m.Id] = original;
                    SaveRestoreFile();
                }
                uint target = (uint)Math.Round(min + (original - min) * (1 - strength / 100.0));
                if (target != current && Ease(h, current, target))
                    Log.Write($"Brightness: Display {m.Number} from {original} to {target}");
                DimExtras(m, h, contrastCut, saturationCut);
                return (int?)target;
            });
        });
    }

    /// <summary>Worker thread only. Contrast and saturation follow the same keep-and-restore rule as brightness.</summary>
    void DimExtras(MonitorInfo m, IntPtr h, int contrastCut, int saturationCut)
    {
        if (!extraOriginals.TryGetValue(m.Id, out var saved))
        {
            saved = new ExtraLevels();
            extraOriginals[m.Id] = saved;
        }
        bool changed = false;
        if (contrastCut > 0 || saved.Contrast != null)
        {
            wait();
            if (GetMonitorContrast(h, out uint min, out uint current, out uint max) && max > min)
            {
                if (saved.Contrast == null && contrastCut > 0) { saved.Contrast = current; changed = true; }
                if (saved.Contrast is uint orig)
                    SetThrottled(h, (uint)Math.Round(min + (orig - min) * (1 - contrastCut / 100.0)), contrast: true);
            }
        }
        if (saturationCut > 0 || saved.Saturation != null)
        {
            wait();
            if (GetVCPFeatureAndVCPFeatureReply(h, VcpSaturation, out _, out uint current, out uint max) && max > 0)
            {
                if (saved.Saturation == null && saturationCut > 0) { saved.Saturation = current; changed = true; }
                if (saved.Saturation is uint orig)
                {
                    wait();
                    SetVCPFeature(h, VcpSaturation, (uint)Math.Round(orig * (1 - saturationCut / 100.0)));
                    lastCommand = DateTime.UtcNow;
                }
            }
        }
        if (changed) SaveRestoreFile();
        if (saved.Contrast == null && saved.Saturation == null) extraOriginals.Remove(m.Id);

        void wait()
        {
            var gap = lastCommand + CommandGap - DateTime.UtcNow;
            if (gap > TimeSpan.Zero) Thread.Sleep(gap);
        }
    }

    public void Restore(MonitorInfo m)
    {
        work.Add(() => RestoreNow(m.Id, m.Handle, m.Number));
    }

    /// <summary>Puts back levels saved by a previous run that did not get to restore them.</summary>
    public void RestoreLeftovers(IEnumerable<MonitorInfo> monitors)
    {
        var list = monitors.ToList();
        work.Add(() =>
        {
            if (!File.Exists(RestoreFile)) return;
            try
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(RestoreFile)) ?? new();
                foreach (var (id, level) in saved) originals[id] = level;
            }
            catch (Exception ex)
            {
                Log.Write("Brightness: could not read saved levels: " + ex.Message);
            }
            try
            {
                if (File.Exists(ExtraRestoreFile))
                    foreach (var (id, levels) in JsonSerializer.Deserialize<Dictionary<string, ExtraLevels>>(File.ReadAllText(ExtraRestoreFile)) ?? new())
                        extraOriginals[id] = levels;
            }
            catch (Exception ex)
            {
                Log.Write("Brightness: could not read saved contrast levels: " + ex.Message);
            }
            foreach (var m in list.Where(m => originals.ContainsKey(m.Id)))
            {
                Log.Write($"Brightness: restoring Display {m.Number} after an unexpected exit");
                RestoreNow(m.Id, m.Handle, m.Number);
            }
        });
    }

    /// <summary>Waits until queued commands are done, e.g. before the app exits.</summary>
    public void Flush(TimeSpan timeout)
    {
        using var done = new ManualResetEventSlim();
        if (!work.IsAddingCompleted) work.Add(done.Set);
        done.Wait(timeout);
    }

    void RestoreNow(string id, IntPtr hMonitor, int number)
    {
        if (!originals.TryGetValue(id, out uint original)) return;
        bool ok = WithPhysical(hMonitor, h =>
        {
            // Fade back from where it is; if it can't be read, go straight to the saved level
            bool done = TryRead(h, out _, out uint current, out _) ? Ease(h, current, original) : SetThrottled(h, original);
            if (extraOriginals.TryGetValue(id, out var extra))
            {
                if (extra.Contrast is uint c) SetThrottled(h, c, contrast: true);
                if (extra.Saturation is uint sat)
                {
                    var gap = lastCommand + CommandGap - DateTime.UtcNow;
                    if (gap > TimeSpan.Zero) Thread.Sleep(gap);
                    SetVCPFeature(h, VcpSaturation, sat);
                    lastCommand = DateTime.UtcNow;
                }
            }
            return done ? (int?)1 : null;
        }) != null;
        if (ok)
        {
            originals.Remove(id);
            extraOriginals.Remove(id);
            SaveRestoreFile();
            Log.Write($"Brightness: Display {number} back to {original}");
        }
        else
        {
            Log.Write($"Brightness: Display {number} did not accept {original}; will retry next time");
        }
    }

    // Monitor firmware handles DDC/CI slowly; commands closer together than this can be
    // dropped or make the monitor stop answering until it is switched off and on
    DateTime lastCommand = DateTime.MinValue;

    /// <summary>
    /// Moves brightness in a few steps (ease-out: big change first, small at the end) instead of
    /// one jump, at least <see cref="CommandGap"/> apart. True when the final level was accepted.
    /// </summary>
    bool Ease(IntPtr h, uint from, uint to)
    {
        if (from == to) return true;
        bool ok = true;
        for (int i = 1; i <= FadeSteps; i++)
        {
            double t = (double)i / FadeSteps;
            double eased = 1 - (1 - t) * (1 - t);
            uint level = (uint)Math.Round(from + ((double)to - from) * eased);
            ok = SetThrottled(h, level);
        }
        return ok;
    }

    bool SetThrottled(IntPtr h, uint level, bool contrast = false)
    {
        var wait = lastCommand + CommandGap - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        bool ok = contrast ? SetMonitorContrast(h, level) : SetMonitorBrightness(h, level);
        lastCommand = DateTime.UtcNow;
        return ok;
    }

    /// <summary>Some monitors miss the first DDC/CI request; ask up to three times.</summary>
    bool TryRead(IntPtr h, out uint min, out uint current, out uint max)
    {
        for (int i = 0; i < 3; i++)
        {
            var wait = lastCommand + CommandGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            bool ok = GetMonitorBrightness(h, out min, out current, out max);
            lastCommand = DateTime.UtcNow;
            if (ok && max > min) return true;
        }
        min = current = max = 0;
        return false;
    }

    /// <summary>Runs <paramref name="use"/> on the first physical monitor that gives a result.</summary>
    static int? WithPhysical(IntPtr hMonitor, Func<IntPtr, int?> use)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) return null;
        var physical = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return null;
        try
        {
            foreach (var p in physical)
                if (use(p.Handle) is int result) return result;
            return null;
        }
        finally
        {
            DestroyPhysicalMonitors(count, physical);
        }
    }

    void SaveRestoreFile()
    {
        try
        {
            if (extraOriginals.Count == 0) File.Delete(ExtraRestoreFile);
            else
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.WriteAllText(ExtraRestoreFile, JsonSerializer.Serialize(extraOriginals));
            }
            if (originals.Count == 0)
            {
                File.Delete(RestoreFile);
                return;
            }
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(RestoreFile, JsonSerializer.Serialize(originals));
        }
        catch (Exception ex)
        {
            Log.Write("Brightness: could not save levels: " + ex.Message);
        }
    }

    public void Dispose()
    {
        work.CompleteAdding();
        thread.Join(TimeSpan.FromSeconds(5));
        work.Dispose();
    }
}
