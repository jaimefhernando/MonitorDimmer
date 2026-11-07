using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Shadeplay.Platform;

/// <summary>The app's typeface (Exo 2) for the WinForms windows drawn outside WPF.</summary>
internal static class WinFormsFonts
{
    static readonly PrivateFontCollection Fonts = new();
    static FontFamily? family;
    static bool tried;

    /// <summary>Exo 2, or Segoe UI when it can't be loaded.</summary>
    public static FontFamily Family
    {
        get
        {
            if (!tried)
            {
                tried = true;
                try
                {
                    foreach (string file in new[] { "Exo2-Regular.ttf", "Exo2-Bold.ttf" })
                    {
                        var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/assets/fonts/{file}"));
                        if (info == null) continue;
                        using var stream = info.Stream;
                        using var copy = new MemoryStream();
                        stream.CopyTo(copy);
                        byte[] bytes = copy.ToArray();
                        // GDI+ reads the font from this memory for as long as the app runs
                        IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
                        Marshal.Copy(bytes, 0, memory, bytes.Length);
                        Fonts.AddMemoryFont(memory, bytes.Length);
                    }
                    family = Fonts.Families.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Log.Write("Could not load the app font for overlays: " + ex.Message);
                }
            }
            return family ?? new FontFamily("Segoe UI");
        }
    }

    public static Font Create(float pixels, FontStyle style = FontStyle.Regular)
    {
        var f = Family;
        if (!f.IsStyleAvailable(style)) style = FontStyle.Regular;
        return new Font(f, pixels, style, GraphicsUnit.Pixel);
    }
}
