using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shadeplay.Core;

namespace Shadeplay.UI.Controls;

/// <summary>Loads game art from disk at the size it is shown, keeping recent images in memory.</summary>
public static class ArtImages
{
    const int MaxCached = 96;

    static readonly Dictionary<string, ImageSource> Cache = new();
    static readonly Queue<string> Order = new();

    /// <returns>The image, or null when there is no path or the file can't be read.</returns>
    public static ImageSource? Load(string? path, int decodeWidth)
    {
        if (path == null) return null;
        string key = $"{path}|{decodeWidth}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var image = new BitmapImage();
            // Read through a stream so an image in Steam's folder is never held open
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodeWidth;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();

            Cache[key] = image;
            Order.Enqueue(key);
            if (Order.Count > MaxCached) Cache.Remove(Order.Dequeue());
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Log.Write($"Could not read art {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>A profile's cover image, for the Games page tiles.</summary>
public sealed class GameCoverConverter : IValueConverter
{
    public static GameCoverConverter Instance { get; } = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GameProfile p ? ArtImages.Load(GameArt.Find(p.Name, p.ExeName, p.ExePath).BestCover, 344) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
