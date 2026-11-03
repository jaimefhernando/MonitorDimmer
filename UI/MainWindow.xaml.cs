using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MonitorDimmer.Core;
using MonitorDimmer.Core.Rgb;
using MonitorDimmer.Platform;

namespace MonitorDimmer.UI;

public partial class MainWindow : Window
{
    public sealed record ModeOption(DimMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record ThemeOption(AppTheme Theme, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record Swatch(string Hex)
    {
        public Brush Brush => ToBrush(Hex);
    }

    static readonly ModeOption[] Modes =
    {
        new(DimMode.FullscreenWatched, "A watched app or game is fullscreen"),
        new(DimMode.AnyFullscreen, "Any app is fullscreen (except exceptions)"),
        new(DimMode.WhileOpen, "A watched app or game is open, fullscreen or not"),
    };

    public sealed record MethodOption(DimMethod Value, string Label)
    {
        public override string ToString() => Label;
    }

    static readonly MethodOption[] Methods =
    {
        new(DimMethod.Overlay, "Black overlay"),
        new(DimMethod.Brightness, "Monitor brightness (DDC/CI)"),
        new(DimMethod.Both, "Both: lower brightness and overlay"),
    };

    static readonly ThemeOption[] Themes =
    {
        new(AppTheme.Dark, "Dark"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.System, "Same as Windows"),
    };

    static readonly Swatch[] SwatchColors =
    {
        new("#FFA020"), new("#FF3B30"), new("#B000FF"), new("#2F6BFF"), new("#00C8FF"), new("#00E676"), new("#FFFFFF"),
    };

    readonly AppSettings settings;
    readonly DimController controller;
    readonly ListCollectionView connectedMonitors;
    bool capturingHotkey;

    public MainWindow(AppSettings settings, DimController controller)
    {
        InitializeComponent();
        ThemeManager.Track(this);
        this.settings = settings;
        this.controller = controller;
        DataContext = settings;

        Icon = AppIcon.ImageSource;
        HeaderIcon.Source = AppIcon.ImageSource;
        ModeBox.ItemsSource = Modes;
        MethodBox.ItemsSource = Methods;
        ThemeBox.ItemsSource = Themes;
        Profiles.Init(settings, controller);
        Stats.Init(controller.Sessions);
        controller.Brightness.SupportChanged += () => Dispatcher.BeginInvoke(new Action(ShowBrightnessSupport));
        Swatches.ItemsSource = SwatchColors;
        WatchedEditor.Items = settings.WatchedApps;
        ExceptionEditor.Items = settings.ExceptionApps;
        CloseAppsEditor.Items = settings.CloseApps;

        connectedMonitors = new ListCollectionView(settings.Monitors)
        {
            Filter = m => ((MonitorSetting)m).IsConnected,
        };
        MonitorList.ItemsSource = connectedMonitors;

        controller.StateChanged += UpdateStatus;
        settings.Changed += OnSettingChanged;
        ThemeManager.Changed += UpdateStatus;
        Activated += (_, _) => RefreshDevices();

        var updater = ((App)Application.Current).Updater;
        VersionText.Text = $"Monitor Dimmer {Updater.CurrentVersion}";
        updater.StateChanged += () => Dispatcher.BeginInvoke(new Action(ShowUpdateState));
        ShowUpdateState();

        UpdateStatus();
        ShowHotkey();
        ShowRgbStyle();
        ShowRgbColor();
        ShowGames();
        RefreshDevices();
    }

    public void ShowHotkeyProblem(string problem)
    {
        HotkeyProblemText.Text = problem;
        HotkeyProblemText.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnSettingChanged(string name)
    {
        switch (name)
        {
            case nameof(AppSettings.HotkeyModifiers):
            case nameof(AppSettings.HotkeyKey):
                ShowHotkey();
                break;
            case nameof(AppSettings.RgbColor):
                ShowRgbColor();
                break;
            case nameof(AppSettings.UseOpenRgb):
            case nameof(AppSettings.OpenRgbPort):
                RefreshDevices();
                break;
        }
    }

    void UpdateStatus()
    {
        StatusText.Text = controller.Status;
        StatusDot.Fill = controller.IsDimming
            ? (Brush)FindResource("Accent")
            : settings.Enabled && !controller.AutoPaused ? (Brush)FindResource("Success") : Brushes.Gray;
    }

    /// <summary>Adds newly connected monitors and checks which apps and devices are available.</summary>
    DateTime lastBrightnessCheck = DateTime.MinValue;

    async void RefreshDevices()
    {
        var monitors = Screens.GetAll();
        // Asking monitors over DDC/CI is slow; once every half minute is plenty
        if (DateTime.UtcNow - lastBrightnessCheck > TimeSpan.FromSeconds(30))
        {
            lastBrightnessCheck = DateTime.UtcNow;
            controller.Brightness.Detect(monitors);
        }

        foreach (var ms in settings.Monitors) ms.IsConnected = false;
        foreach (var m in monitors)
        {
            var ms = settings.FindMonitor(m.Id);
            if (ms == null)
            {
                ms = new MonitorSetting { Id = m.Id, Strength = settings.Strength };
                settings.Monitors.Add(ms);
            }
            ms.DisplayName = m.Description;
            ms.IsConnected = true;
        }
        connectedMonitors.Refresh();
        ShowBrightnessSupport();
        Profiles.RefreshMonitors();

        WallpaperStatus.Text = ActionRunner.FindRunningWallpaperEngine() is string we
            ? "Running: " + we
            : "Wallpaper Engine is not running right now.";
        RainmeterStatus.Text = ActionRunner.FindRunning("Rainmeter") is string rm
            ? "Running: " + rm
            : "Rainmeter is not running right now.";

        string? chroma = await RazerChroma.DetectAsync();
        RazerStatus.Text = chroma != null
            ? $"Connected to Razer Synapse (Chroma SDK {chroma})."
            : "Razer Synapse's Chroma SDK was not found. Install or start Razer Synapse to use it.";

        if (!settings.UseOpenRgb)
            OpenRgbStatus.Text = "";
        else
            OpenRgbStatus.Text = await OpenRgbClient.IsReachableAsync(settings.OpenRgbPort)
                ? $"OpenRGB SDK server found on port {settings.OpenRgbPort}."
                : $"No OpenRGB SDK server on port {settings.OpenRgbPort}. Start OpenRGB and its SDK server.";
    }

    void ShowBrightnessSupport()
    {
        foreach (var ms in settings.Monitors)
        {
            ms.BrightnessStatus = controller.Brightness.GetSupport(ms.Id) switch
            {
                null => "Checking brightness control…",
                { Supported: true } s => $"Brightness control works (DDC/CI). Normal brightness: {s.Current}.",
                _ => "No brightness control: the monitor did not answer over DDC/CI, so the overlay is used. "
                    + "Some monitors have DDC/CI turned off in their own menu.",
            };
        }
    }

    void ShowGames()
    {
        var games = GameDetector.InstalledGames;
        GamesList.ItemsSource = games;
        GamesSummary.Text = games.Count == 0
            ? "No installed games found yet. Games Windows Game Bar recognises are still detected."
            : $"{games.Count} installed games found. Games Windows Game Bar recognises are detected too.";
    }

    void Rescan_Click(object sender, RoutedEventArgs e)
    {
        GameDetector.Refresh();
        ShowGames();
    }

    // --- Lighting ---

    void ShowRgbStyle()
    {
        RgbOffRadio.IsChecked = settings.RgbStyle == RgbStyle.Off;
        RgbColorRadio.IsChecked = settings.RgbStyle == RgbStyle.Color;
        ColorPanel.IsEnabled = settings.RgbStyle == RgbStyle.Color;
    }

    void RgbStyle_Checked(object sender, RoutedEventArgs e)
    {
        settings.RgbStyle = RgbColorRadio.IsChecked == true ? RgbStyle.Color : RgbStyle.Off;
        ColorPanel.IsEnabled = settings.RgbStyle == RgbStyle.Color;
    }

    void ShowRgbColor() => ColorPreview.Background = ToBrush(settings.RgbColor);

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) settings.RgbColor = hex;
    }

    void PreviewLighting_Click(object sender, RoutedEventArgs e) => controller.PreviewLighting();

    static Brush ToBrush(string hex)
    {
        var (r, g, b) = RgbLighting.ParseColor(hex);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // --- Shortcut capture ---

    void ShowHotkey()
    {
        if (!capturingHotkey) HotkeyBox.Text = HotkeyManager.Describe(settings.HotkeyModifiers, settings.HotkeyKey);
    }

    void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        capturingHotkey = true;
        HotkeyBox.Text = "Press the new shortcut…";
    }

    void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        capturingHotkey = false;
        ShowHotkey();
    }

    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
            return;

        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= AppSettings.ModControl;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= AppSettings.ModAlt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= AppSettings.ModShift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= AppSettings.ModWin;

        // A bare letter would fire while typing; function keys alone are fine
        bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
        if (mods == 0 && !isFunctionKey)
        {
            HotkeyBox.Text = "Add Ctrl, Alt, Shift or Win…";
            return;
        }

        settings.HotkeyModifiers = mods;
        settings.HotkeyKey = KeyInterop.VirtualKeyFromKey(key);
        capturingHotkey = false;
        ShowHotkey();
        Keyboard.ClearFocus();
    }

    // --- Updates ---

    void ShowUpdateState()
    {
        var updater = ((App)Application.Current).Updater;
        UpdateStatusText.Text = updater.Status;
        UpdateStatusText.Visibility = updater.Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = !updater.Busy && !updater.ReadyToInstall;
        InstallUpdateButton.Visibility = updater.ReadyToInstall ? Visibility.Visible : Visibility.Collapsed;
    }

    async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await ((App)Application.Current).Updater.CheckAsync(userAsked: true);

    void InstallUpdate_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ExitApp(installUpdate: true);

    // --- Settings backup ---

    void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export settings",
            FileName = $"Monitor Dimmer settings {DateTime.Now:yyyy-MM-dd}.json",
            Filter = "Monitor Dimmer settings (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            settings.ExportTo(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The settings could not be saved: " + ex.Message, "Export settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import settings",
            Filter = "Monitor Dimmer settings (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;

        if (AppSettings.CheckImport(dialog.FileName) is string problem)
        {
            MessageBox.Show(this, problem, "Import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(this,
            "Replace all current settings with the ones in this file? Monitor Dimmer restarts to load them.",
            "Import settings", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK) ((App)Application.Current).ImportSettingsAndRestart(dialog.FileName);
    }

    // --- Buttons ---

    void Identify_Click(object sender, RoutedEventArgs e) => IdentifyForm.ShowOnAll();

    void OpenLog_Click(object sender, RoutedEventArgs e) => App.OpenFolder(AppSettings.Folder);

    void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitApp();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the app running in the tray
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        controller.StateChanged -= UpdateStatus;
        settings.Changed -= OnSettingChanged;
        ThemeManager.Changed -= UpdateStatus;
        base.OnClosed(e);
    }
}
