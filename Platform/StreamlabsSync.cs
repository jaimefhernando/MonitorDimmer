using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Shadeplay.Platform;

/// <summary>Minimal Streamlabs Desktop client: polls stream/record state over the local slobs named pipe.</summary>
internal sealed class StreamlabsSync : IDisposable
{
    readonly AppSettings settings;
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    volatile bool active;
    string status = "Streamlabs disconnected";
    NamedPipeClientStream? currentPipe;

    public StreamlabsSync(AppSettings settings)
    {
        this.settings = settings;
        worker = Task.Run(() => RunAsync(stop.Token));
    }

    public bool Active => active;
    public string Status => status;
    public event Action? StateChanged;

    async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!settings.StreamlabsEnabled)
            {
                SetState(false, "Streamlabs sync disabled");
                await Task.Delay(1500, token).ContinueWith(_ => { }, TaskScheduler.Default);
                continue;
            }

            try
            {
                using var pipe = new NamedPipeClientStream(".", "slobs", PipeDirection.InOut, PipeOptions.Asynchronous);
                currentPipe = pipe;
                await pipe.ConnectAsync(5000, token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, true) { AutoFlush = true, NewLine = "\n" };

                SetState(false, "Streamlabs connected");
                int id = 1;
                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    string request = JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id,
                        method = "getModel",
                        @params = new { resource = "StreamingService" }
                    });
                    await writer.WriteLineAsync(request);

                    string? matched = null;
                    string? line;
                    while ((line = await reader.ReadLineAsync(token)) != null)
                    {
                        if (line.Length == 0) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            if (doc.RootElement.TryGetProperty("id", out var idEl) && IdMatches(idEl, id))
                            {
                                matched = line;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (matched == null) break;

                    using var resp = JsonDocument.Parse(matched);
                    var result = resp.RootElement.GetProperty("result");
                    string streaming = result.TryGetProperty("streamingStatus", out var ss) ? ss.GetString() ?? "offline" : "offline";
                    string recording = result.TryGetProperty("recordingStatus", out var rs) ? rs.GetString() ?? "offline" : "offline";
                    bool streamingActive = streaming != "offline";
                    bool recordingActive = recording != "offline" && recording != "wrote";
                    bool nowActive = streamingActive || recordingActive;
                    SetState(nowActive, nowActive
                        ? (streamingActive && recordingActive ? "Streamlabs streaming and recording"
                           : streamingActive ? "Streamlabs streaming" : "Streamlabs recording")
                        : "Streamlabs connected");

                    id++;
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SetState(false, "Streamlabs unavailable");
                Log.Write("Streamlabs sync: " + ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(8), token); }
                catch (OperationCanceledException) { break; }
            }
            finally { currentPipe = null; }
        }
    }

    static bool IdMatches(JsonElement el, int expected)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32() == expected;
        if (el.ValueKind == JsonValueKind.String) return int.TryParse(el.GetString(), out int v) && v == expected;
        return false;
    }

    public void Reconnect() => currentPipe?.Dispose();

    void SetState(bool next, string text)
    {
        if (active == next && status == text) return;
        active = next;
        status = text;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        stop.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        stop.Dispose();
    }
}
