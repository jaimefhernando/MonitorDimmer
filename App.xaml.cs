using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Shadeplay.Core;
using Shadeplay.Platform;
using Shadeplay.UI;

namespace Shadeplay;

public partial class App : Application
{
    const string InstanceMutexName = @"Local\Shadeplay.SingleInstance";
    const string ShowSignalName = @"Local\Shadeplay.ShowSettings";

    Mutex? instanceMutex;
    EventWaitHandle? showSignal;
    AppSettings settings = null!;
    DimController controller = null!;
    HotkeyManager hotkeys = null!;
    HotkeyManager isolateHotkey = null!;
    ObsSync obs = null!;
    StreamlabsSync streamlabs = null!;
    TrayIcon tray = null!;
    MainWindow? window;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // First check shortly after startup, then once a day
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    public static bool IsExiting { get; private set; }

    public Updater Updater { get; private set; } = null!;

    /// <summary>Empty, or why the shortcut could not be registered.</summary>
    public string HotkeyProblem { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second launch just opens the settings of the running one
        instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowSignalName).Set(); } catch (Exception) { }
            Shutdown();
            return;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowSettings)), null, Timeout.Infinite, false);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled error: " + args.Exception);
            args.Handled = true;
        };
        SessionEnding += (_, _) => controller?.Dispose();

        settings = AppSettings.Load();
        GameArt.Configure(() => settings.SteamGridDbKey);
        ThemeManager.Apply(settings.Theme);
        controller = new DimController(settings);
        obs = new ObsSync(settings);
        obs.StateChanged += () => Dispatcher.BeginInvoke(new Action(() => controller.SetObsState(obs.Active, obs.Status)));
        streamlabs = new StreamlabsSync(settings);
        streamlabs.StateChanged += () => Dispatcher.BeginInvoke(new Action(() => controller.SetStreamlabsState(streamlabs.Active, streamlabs.Status)));
        hotkeys = new HotkeyManager();
        hotkeys.Pressed += controller.ToggleManual;
        ApplyHotkey();
        isolateHotkey = new HotkeyManager();
        isolateHotkey.Pressed += controller.ToggleIsolate;
        ApplyIsolateHotkey();
        tray = new TrayIcon(controller, settings, ShowSettings, () => ExitApp());
        StartupManager.Apply(settings.StartWithWindows);

        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            settings.Save();
        };
        settings.Changed += OnSettingChanged;

        controller.Start();
        Log.Write($"Started, version {Updater.CurrentVersion}");

        Updater = new Updater(settings);
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(24);
            if (settings.AutoUpdate) await Updater.CheckAsync(userAsked: false);
        };
        updateTimer.Start();
        Notifications = new Notifications(settings, tray, controller, Updater);

        // The first time (even when started with Windows), the welcome screen comes first
        if (!settings.WelcomeDone) ShowWelcome();
        else if (!e.Args.Contains("--tray")) ShowSettings();
    }

    internal Notifications? Notifications { get; private set; }
    WelcomeWindow? welcome;

    public void ShowWelcome()
    {
        if (IsExiting) return;
        if (welcome == null)
        {
            welcome = new WelcomeWindow(settings, controller);
            welcome.Closed += (_, _) => welcome = null;
        }
        welcome.Show();
        welcome.Activate();
    }

    void OnSettingChanged(string name)
    {
        saveTimer.Stop();
        saveTimer.Start();

        switch (name)
        {
            case nameof(AppSettings.HotkeyEnabled):
            case nameof(AppSettings.HotkeyModifiers):
            case nameof(AppSettings.HotkeyKey):
                ApplyHotkey();
                break;
            case nameof(AppSettings.IsolateHotkeyEnabled):
                ApplyIsolateHotkey();
                break;
            case nameof(AppSettings.StartWithWindows):
                StartupManager.Apply(settings.StartWithWindows);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(settings.Theme);
                break;
            case nameof(AppSettings.SteamGridDbKey):
                GameArt.KeyChanged();
                break;
            case nameof(AppSettings.ObsEnabled):
            case nameof(AppSettings.ObsHost):
            case nameof(AppSettings.ObsPort):
            case nameof(AppSettings.ObsPasswordProtected):
            case nameof(AppSettings.ObsPassword):
                obs?.Reconnect();
                break;
            case nameof(AppSettings.StreamlabsEnabled):
                streamlabs?.Reconnect();
                break;
        }
        controller.Update();
    }

    void ApplyIsolateHotkey()
    {
        if (!settings.IsolateHotkeyEnabled) isolateHotkey.Unregister();
        else if (!isolateHotkey.Register(AppSettings.ModControl | AppSettings.ModAlt, 0x49))
            Log.Write("Ctrl+Alt+I is already used by another app");
    }

    void ApplyHotkey()
    {
        if (!settings.HotkeyEnabled)
        {
            hotkeys.Unregister();
            HotkeyProblem = "";
        }
        else
        {
            HotkeyProblem = hotkeys.Register(settings.HotkeyModifiers, settings.HotkeyKey)
                ? ""
                : "Another app already uses this shortcut. Pick a different one.";
        }
        window?.ShowHotkeyProblem(HotkeyProblem);
    }

    public void ShowSettings()
    {
        if (IsExiting) return;
        window ??= new MainWindow(settings, controller);
        window.ShowHotkeyProblem(HotkeyProblem);
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Replaces all settings with an exported file by restarting with it.</summary>
    public void ImportSettingsAndRestart(string file)
    {
        string imported = File.ReadAllText(file);
        CleanUp();
        // After CleanUp saved the old settings, so they don't overwrite the imported ones
        File.WriteAllText(AppSettings.FilePath, imported);
        Log.Write("Settings imported from " + file);
        // Let the new instance start as the only one
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        instanceMutex = null;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        Shutdown();
    }

    /// <param name="installUpdate">Restart straight into a downloaded update.</param>
    public void ExitApp(bool installUpdate = false)
    {
        CleanUp();
        if (installUpdate) Updater.InstallAndRestart(); // exits the process
        else Updater.InstallOnExit();                    // installs once this process is gone
        Shutdown();
    }

    void CleanUp()
    {
        IsExiting = true;
        settings.Changed -= OnSettingChanged;
        saveTimer.Stop();
        updateTimer.Stop();
        settings.Save();
        // Restores brightness, lighting, Wallpaper Engine... before any update replaces the app
        controller.Dispose();
        hotkeys.Dispose();
        isolateHotkey.Dispose();
        obs.Dispose();
        streamlabs.Dispose();
        tray.Dispose();
        welcome?.Close();
        window?.Close();
        showSignal?.Dispose();
        Log.Write("Exited");
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("Could not open folder: " + ex.Message);
        }
    }
}
