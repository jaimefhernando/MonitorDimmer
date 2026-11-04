using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Shadeplay.Platform;

/// <summary>Checks for, downloads and applies new versions through Velopack.</summary>
public sealed class Updater
{
    /// <summary>
    /// Where releases are published (scripts\release.ps1 -Publish uploads them there).
    /// AppSettings.UpdateSource can point a single PC somewhere else, e.g. a local releases folder.
    /// </summary>
    public const string DefaultSource = "https://github.com/jaimefhernando/MonitorDimmer";

    readonly AppSettings settings;
    readonly object busyGate = new();
    UpdateInfo? downloaded;

    public Updater(AppSettings settings) => this.settings = settings;

    public event Action? StateChanged;

    public string Status { get; private set; } = "";
    public bool ReadyToInstall => downloaded != null;
    public bool Busy { get; private set; }

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "?";

    string Source => settings.UpdateSource.Trim().Length > 0 ? settings.UpdateSource.Trim() : DefaultSource;

    /// <summary>Checks for a new version and downloads it in the background.</summary>
    /// <param name="userAsked">From the "Check now" button: report "up to date" as well.</param>
    public async Task CheckAsync(bool userAsked)
    {
        lock (busyGate)
        {
            if (Busy || downloaded != null) return;
            Busy = true;
        }
        if (Source.Length == 0)
        {
            SetStatus(userAsked ? "Updates are not set up for this copy yet." : "");
            Busy = false;
            StateChanged?.Invoke();
            return;
        }

        var manager = CreateManager();
        if (!manager.IsInstalled)
        {
            SetStatus(userAsked ? "Updates work in the installed app; this copy runs without installing." : "");
            Busy = false;
            StateChanged?.Invoke();
            return;
        }

        try
        {
            SetStatus("Checking for updates…");
            var info = await manager.CheckForUpdatesAsync();
            if (info == null)
            {
                SetStatus(userAsked ? $"You have the latest version ({CurrentVersion})." : "");
                return;
            }

            string version = info.TargetFullRelease.Version.ToString();
            SetStatus($"Downloading version {version}…");
            await manager.DownloadUpdatesAsync(info, p => SetStatus($"Downloading version {version}… {p} %"));
            downloaded = info;
            SetStatus($"Version {version} is ready. It installs when you restart Shadeplay.");
            Log.Write($"Update {version} downloaded");
        }
        catch (Exception ex)
        {
            Log.Write("Update check failed: " + ex.Message);
            SetStatus(userAsked ? "Could not check for updates. See the log for details." : "");
        }
        finally
        {
            Busy = false;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Restarts into the downloaded version. The app must clean up before calling this.</summary>
    public void InstallAndRestart()
    {
        if (downloaded == null) return;
        Log.Write($"Installing update {downloaded.TargetFullRelease.Version} and restarting");
        CreateManager().ApplyUpdatesAndRestart(downloaded.TargetFullRelease);
    }

    /// <summary>On a normal exit, installs a downloaded update so the next start runs it.</summary>
    public void InstallOnExit()
    {
        if (downloaded == null) return;
        CreateManager().WaitExitThenApplyUpdates(downloaded.TargetFullRelease, silent: true, restart: false);
    }

    UpdateManager CreateManager() =>
        Source.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            ? new UpdateManager(new GithubSource(Source, null, prerelease: false))
            : new UpdateManager(Source); // a web address or a folder

    void SetStatus(string status)
    {
        Status = status;
        StateChanged?.Invoke();
    }
}
