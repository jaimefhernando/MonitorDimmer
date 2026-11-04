using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shadeplay.Interop;

namespace Shadeplay.UI;

/// <summary>Shared maximize/restore chrome handling for WindowChrome windows.</summary>
static class WindowChromeHelper
{
    /// <summary>
    /// A maximized WindowChrome window hangs past the screen edge by the resize frame,
    /// so pad it back in, drop the outline and swap the maximize/restore icon.
    /// </summary>
    public static void Apply(Window window, Border rootBorder, FrameworkElement maximizeIcon, FrameworkElement restoreIcon, Button maximizeButton)
    {
        bool maximized = window.WindowState == WindowState.Maximized;
        maximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        restoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        maximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(maximizeButton, maximized ? "Restore" : "Maximize");

        if (!maximized)
        {
            rootBorder.BorderThickness = new Thickness(1);
            rootBorder.Margin = new Thickness(0);
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
        int px = Native.GetSystemMetricsForDpi(Native.SM_CXSIZEFRAME, dpiValue)
               + Native.GetSystemMetricsForDpi(Native.SM_CXPADDEDBORDER, dpiValue);
        rootBorder.BorderThickness = new Thickness(0);
        rootBorder.Margin = new Thickness(px / dpi.DpiScaleX);
    }
}
