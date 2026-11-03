using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Shadeplay.Interop;

namespace Shadeplay.Platform;

/// <summary>Switches the light/dark palette, including window title bars and the tray menu.</summary>
internal static class ThemeManager
{
    static AppTheme selected = AppTheme.Dark;
    static bool listening;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after the palette changed.</summary>
    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
        selected = theme;
        if (!listening)
        {
            // Follow Windows when it switches between light and dark
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && selected == AppTheme.System)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply(AppTheme.System)));
            };
            listening = true;
        }

        bool dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.System => !WindowsUsesLightTheme(),
            _ => true,
        };
        IsDark = dark;

        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        merged[0] = palette;

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        Changed?.Invoke();
    }

    /// <summary>Dark title bar for a window; call once it has a handle.</summary>
    public static void ApplyTitleBar(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>Makes a window follow the theme's title bar color from the moment it opens.</summary>
    public static void Track(Window window) => window.SourceInitialized += (_, _) => ApplyTitleBar(window);

    static bool WindowsUsesLightTheme()
    {
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return value is not int i || i != 0;
    }
}
