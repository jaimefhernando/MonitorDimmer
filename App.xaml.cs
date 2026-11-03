using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MonitorDimmer.Core;
using MonitorDimmer.Platform;
using MonitorDimmer.UI;

namespace MonitorDimmer;

public partial class App : Application
{
    const string InstanceMutexName = @"Local\MonitorDimmer.SingleInstance";
    const string ShowSignalName = @"Local\MonitorDimmer.ShowSettings";

    Mutex? instanceMutex;
    EventWaitHandle? showSignal;
    AppSettings settings = null!;
    DimController controller = null!;
    HotkeyManager hotkeys = null!;
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
        ThemeManager.Apply(settings.Theme);
        controller = new DimController(settings);
        hotkeys = new HotkeyManager();
        hotkeys.Pressed += controller.ToggleManual;
        ApplyHotkey();
        tray = new TrayIcon(controller, ShowSettings, () => ExitApp());
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

        if (!e.Args.Contains("--tray")) ShowSettings();
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
            case nameof(AppSettings.StartWithWindows):
                StartupManager.Apply(settings.StartWithWindows);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(settings.Theme);
                break;
        }
        controller.Update();
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

    /// <param name="installUpdate">Restart straight into a downloaded update.</param>
    public void ExitApp(bool installUpdate = false)
    {
        IsExiting = true;
        settings.Changed -= OnSettingChanged;
        saveTimer.Stop();
        updateTimer.Stop();
        settings.Save();
        // Restores brightness, lighting, Wallpaper Engine... before any update replaces the app
        controller.Dispose();
        hotkeys.Dispose();
        tray.Dispose();
        window?.Close();
        showSignal?.Dispose();
        Log.Write("Exited");

        if (installUpdate) Updater.InstallAndRestart(); // exits the process
        else Updater.InstallOnExit();                    // installs once this process is gone
        Shutdown();
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
