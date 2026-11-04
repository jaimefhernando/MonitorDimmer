using Shadeplay.Core;

namespace Shadeplay.Platform;

/// <summary>Decides when to notify, so alt-tabbing in and out of a game doesn't flood you.</summary>
internal sealed class Notifications
{
    /// <summary>The same game announces itself again only after this long.</summary>
    static readonly TimeSpan DimCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Shorter sessions get no "you played" summary.</summary>
    static readonly TimeSpan SummaryMinimum = TimeSpan.FromMinutes(5);

    readonly AppSettings settings;
    readonly DimController controller;
    readonly TrayIcon tray;
    readonly Dictionary<string, DateTime> lastDimNotice = new(StringComparer.OrdinalIgnoreCase);
    string? announcedUpdate;

    public Notifications(AppSettings settings, TrayIcon tray, DimController controller, Updater updater)
    {
        this.settings = settings;
        this.controller = controller;
        this.tray = tray;
        controller.SessionStarted += OnSessionStarted;
        controller.SessionEnded += OnSessionEnded;
        updater.StateChanged += () => OnUpdaterChanged(updater);
    }

    bool QuietForObs => settings.LiveQuietNotifications && controller.LiveActive;

    void OnSessionStarted(string name, string exe)
    {
        if (!settings.NotifyOnDim || QuietForObs) return;
        var now = DateTime.Now;
        if (lastDimNotice.TryGetValue(exe, out var last) && now - last < DimCooldown) return;
        lastDimNotice[exe] = now;
        // Prune entries outside the cooldown so the map doesn't grow without bound
        List<string> stale = new();
        foreach (var (key, value) in lastDimNotice)
            if (now - value >= DimCooldown) stale.Add(key);
        foreach (var key in stale) lastDimNotice.Remove(key);
        tray.Notify($"Dimming for {name}", "Your other monitors are dimmed. Right-click the tray icon to pause.");
    }

    void OnSessionEnded(PlaySession session)
    {
        if (!settings.NotifyOnSessionEnd || QuietForObs || session.Duration < SummaryMinimum) return;
        tray.Notify($"You played {session.Name} for {SessionTracker.Format(session.Duration)}",
            "Your monitors are back to normal. See all your play time on the Stats tab.");
    }

    void OnUpdaterChanged(Updater updater)
    {
        if (!settings.NotifyOnUpdate || QuietForObs || !updater.ReadyToInstall) return;
        // Status reads "Version X is ready…"; announce each version once
        if (announcedUpdate == updater.Status) return;
        announcedUpdate = updater.Status;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            tray.Notify("Shadeplay update ready", updater.Status)));
    }
}
