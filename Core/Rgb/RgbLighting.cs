using System.Globalization;

namespace MonitorDimmer.Core.Rgb;

/// <summary>Changes RGB lighting while dimming and gives it back afterwards.</summary>
internal sealed class RgbLighting : IDisposable
{
    readonly RazerChroma razer = new();
    bool openRgbApplied;

    public sealed record Options(
        RgbStyle Style, string Color, int Brightness, bool Razer, bool OpenRgb, int OpenRgbPort,
        string OpenRgbDimProfile, string OpenRgbRestoreProfile)
    {
        public static Options From(AppSettings s) => new(s.RgbStyle, s.RgbColor, s.RgbBrightness,
            s.UseRazerChroma, s.UseOpenRgb, s.OpenRgbPort, s.OpenRgbDimProfile, s.OpenRgbRestoreProfile);
    }

    public async Task ApplyAsync(Options o)
    {
        if (o.Razer)
            await razer.ApplyAsync(o.Style == RgbStyle.Off ? null : ScaledBgr(o.Color, o.Brightness));
        if (o.OpenRgb && o.OpenRgbDimProfile.Trim().Length > 0)
            openRgbApplied = await OpenRgbClient.LoadProfileAsync(o.OpenRgbPort, o.OpenRgbDimProfile);
    }

    public async Task RestoreAsync(Options o)
    {
        await razer.ReleaseAsync();
        if (openRgbApplied && o.OpenRgbRestoreProfile.Trim().Length > 0)
            await OpenRgbClient.LoadProfileAsync(o.OpenRgbPort, o.OpenRgbRestoreProfile);
        openRgbApplied = false;
    }

    /// <summary>"#RRGGBB" at a brightness percentage, as Chroma's 0x00BBGGRR.</summary>
    static int ScaledBgr(string hex, int brightness)
    {
        var (r, g, b) = ParseColor(hex);
        double f = brightness / 100.0;
        return ((int)(b * f) << 16) | ((int)(g * f) << 8) | (int)(r * f);
    }

    public static (byte R, byte G, byte B) ParseColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (255, 160, 32);
    }

    public void Dispose() => razer.Dispose();
}
