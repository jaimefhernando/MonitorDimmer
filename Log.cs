using System.IO;

namespace Shadeplay;

internal static class Log
{
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                // Keep the log small but retain recent history: trim to the last 256 KB
                // rather than wiping everything when it grows past 512 KB.
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    try
                    {
                        byte[] all = File.ReadAllBytes(FilePath);
                        int keep = 256 * 1024;
                        int start = all.Length - keep;
                        // Start at the next newline so we don't cut a line in half
                        while (start < all.Length && all[start] != (byte)'\n') start++;
                        if (start < all.Length) start++;
                        File.WriteAllBytes(FilePath, all.AsSpan(start).ToArray());
                    }
                    catch (Exception)
                    {
                        try { info.Delete(); } catch (Exception) { }
                    }
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down
        }
    }
}
