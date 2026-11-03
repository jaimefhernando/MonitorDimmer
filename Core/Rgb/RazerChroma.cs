using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shadeplay.Core.Rgb;

/// <summary>
/// Razer Synapse's Chroma SDK REST API. While a session is open its effects replace the user's
/// own; closing the session hands the lighting back to Synapse.
/// </summary>
internal sealed class RazerChroma : IDisposable
{
    const string BaseUrl = "http://localhost:54235/razer/chromasdk";
    static readonly string[] Devices = { "keyboard", "mouse", "mousepad", "headset", "keypad", "chromalink" };

    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    Timer? heartbeat;
    string? sessionUri;

    /// <summary>Chroma SDK version, or null when Synapse's SDK is not running.</summary>
    public static async Task<string?> DetectAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var doc = JsonDocument.Parse(await http.GetStringAsync(BaseUrl));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : "unknown";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <param name="bgr">Color as 0x00BBGGRR; null turns the lights off.</param>
    public async Task ApplyAsync(int? bgr)
    {
        if (sessionUri == null && !await OpenSessionAsync()) return;

        object body = bgr is int color
            ? new { effect = "CHROMA_STATIC", param = new { color } }
            : new { effect = "CHROMA_NONE" };
        foreach (string device in Devices)
        {
            try
            {
                using var r = await http.PutAsJsonAsync($"{sessionUri}/{device}", body);
                string reply = await r.Content.ReadAsStringAsync();
                // {"result":0} is success; devices that are not connected answer with an error code
                if (!reply.Contains("\"result\":0"))
                    Log.Write($"Razer Chroma {device}: {reply.Trim()}");
            }
            catch (Exception ex)
            {
                Log.Write($"Razer Chroma {device}: {ex.Message}");
            }
        }
        Log.Write(bgr is int c ? $"Razer Chroma: static color 0x{c:X6} (BGR)" : "Razer Chroma: lights off");
    }

    public async Task ReleaseAsync()
    {
        heartbeat?.Dispose();
        heartbeat = null;
        string? uri = sessionUri;
        sessionUri = null;
        if (uri == null) return;
        try
        {
            using var r = await http.DeleteAsync(uri);
            Log.Write("Razer Chroma: lighting handed back to Synapse");
        }
        catch (HttpRequestException)
        {
            // Synapse closes the connection instead of answering once the session has ended
            Log.Write("Razer Chroma: lighting handed back to Synapse");
        }
        catch (Exception ex)
        {
            Log.Write("Razer Chroma release: " + ex.Message);
        }
    }

    async Task<bool> OpenSessionAsync()
    {
        var app = new
        {
            title = "Shadeplay",
            description = "Dims RGB lighting while other monitors are dimmed",
            author = new { name = "Shadeplay", contact = "local" },
            device_supported = Devices,
            category = "application",
        };
        try
        {
            using var response = await http.PostAsJsonAsync(BaseUrl, app);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("uri", out var uri))
            {
                Log.Write("Razer Chroma: no session returned");
                return false;
            }
            sessionUri = uri.GetString();
            // The session ends unless it hears from us at least every 15 seconds
            heartbeat = new Timer(_ => _ = Heartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            // Synapse ignores effects sent right after the session opens
            await Task.Delay(1200);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Razer Chroma not available: " + ex.Message);
            return false;
        }
    }

    async Task Heartbeat()
    {
        string? uri = sessionUri;
        if (uri == null) return;
        try
        {
            using var r = await http.PutAsync(uri + "/heartbeat", null);
        }
        catch (Exception)
        {
            // Next beat tries again
        }
    }

    public void Dispose()
    {
        heartbeat?.Dispose();
        http.Dispose();
    }
}
