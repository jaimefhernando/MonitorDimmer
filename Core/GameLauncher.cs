using System.Diagnostics;
using System.IO;

namespace Shadeplay.Core;

/// <summary>Starts an installed game: Steam games through Steam, others from their program.</summary>
public static class GameLauncher
{
    public static bool CanLaunch(DetectedGame? game, string exePath = "") =>
        SteamId(game) != null || ProgramOf(game, exePath) != null;

    /// <returns>False when the game couldn't be started (the reason is logged).</returns>
    public static bool Launch(DetectedGame? game, string exePath = "")
    {
        try
        {
            if (SteamId(game) is string appId)
            {
                // Steam starts it with its own launch options, cloud saves and overlay
                Process.Start(new ProcessStartInfo($"steam://rungameid/{appId}") { UseShellExecute = true })?.Dispose();
                Log.Write($"Launched {game!.Name} through Steam");
                return true;
            }
            if (ProgramOf(game, exePath) is string program)
            {
                Process.Start(new ProcessStartInfo(program)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(program) ?? "",
                })?.Dispose();
                Log.Write($"Launched {Path.GetFileName(program)}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Could not launch {game?.Name ?? exePath}: {ex.Message}");
        }
        return false;
    }

    static string? SteamId(DetectedGame? game) =>
        game is { Source: "Steam", SteamAppId: string id } && id.All(char.IsAsciiDigit) ? id : null;

    static string? ProgramOf(DetectedGame? game, string exePath)
    {
        if (exePath.Length > 0 && File.Exists(exePath)) return exePath;
        return game != null && GameDetector.MainExe(game) is string main && File.Exists(main) ? main : null;
    }
}
