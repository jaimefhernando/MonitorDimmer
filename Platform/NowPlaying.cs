using Windows.Media.Control;

namespace Shadeplay.Platform;

/// <summary>
/// The track or video playing right now, from the same Windows media controls the volume
/// flyout shows (Spotify, browsers, media players). Read on demand, never stored or logged.
/// </summary>
internal sealed class NowPlaying
{
    static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(4);

    GlobalSystemMediaTransportControlsSessionManager? manager;
    DateTime lastCheck = DateTime.MinValue;
    volatile string text = "";
    int busy;
    bool loggedFailure;

    /// <summary>"Title · Artist", or empty when nothing is playing.</summary>
    public string Text => text;

    /// <summary>Starts a background check unless one ran recently; the result shows up in <see cref="Text"/>.</summary>
    public void Refresh()
    {
        if (DateTime.UtcNow - lastCheck < CheckEvery || Interlocked.Exchange(ref busy, 1) == 1) return;
        lastCheck = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session == null
                    || session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    text = "";
                    return;
                }
                var media = await session.TryGetMediaPropertiesAsync();
                string title = media.Title?.Trim() ?? "";
                string artist = media.Artist?.Trim() ?? "";
                text = title.Length == 0 ? "" : artist.Length > 0 ? $"{title} · {artist}" : title;
            }
            catch (Exception ex)
            {
                text = "";
                if (!loggedFailure) Log.Write("Now playing unavailable: " + ex.Message);
                loggedFailure = true;
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        });
    }

    public void Clear() => text = "";
}
