using System.Net.WebSockets;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shadeplay.Interop;

namespace Shadeplay.Platform;

/// <summary>Minimal OBS WebSocket v5 client: connects locally and polls stream/record state.</summary>
internal sealed class ObsSync : IDisposable
{
    readonly AppSettings settings;
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    volatile bool active;
    string status = "OBS disconnected";
    ClientWebSocket? currentSocket;

    public ObsSync(AppSettings settings)
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
            if (!settings.ObsEnabled)
            {
                SetState(false, "OBS sync disabled");
                await Task.Delay(1500, token).ContinueWith(_ => { }, TaskScheduler.Default);
                continue;
            }

            try
            {
                using var socket = new ClientWebSocket();
                currentSocket = socket;
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                string host = settings.ObsHost.Trim();
                if (host.Length == 0) host = "127.0.0.1";
                await socket.ConnectAsync(new Uri($"ws://{host}:{settings.ObsPort}"), token);

                using var hello = await ReceiveJson(socket, token);
                string? challenge = hello.RootElement.GetProperty("d").TryGetProperty("authentication", out var auth)
                    ? auth.GetProperty("challenge").GetString() : null;
                string? salt = auth.ValueKind == JsonValueKind.Object && auth.TryGetProperty("salt", out var saltEl)
                    ? saltEl.GetString() : null;
                string? response = challenge != null && salt != null ? Authenticate(settings.ObsPassword, salt, challenge) : null;
                object identify = response == null
                    ? new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0 } }
                    : new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 0, authentication = response } };
                await SendJson(socket, identify, token);
                using (var identified = await ReceiveUntilOp(socket, 2, token)) { }

                SetState(false, "OBS connected");
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    bool streaming = await GetActive(socket, "GetStreamStatus", "outputActive", token);
                    bool recording = await GetActive(socket, "GetRecordStatus", "outputActive", token);
                    bool nowActive = streaming || recording;
                    SetState(nowActive, nowActive ? (streaming && recording ? "OBS streaming and recording" : streaming ? "OBS streaming" : "OBS recording") : "OBS connected");
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SetState(false, "OBS unavailable");
                Log.Write($"OBS sync: {ex.GetType().Name}: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(8), token); }
                catch (OperationCanceledException) { break; }
            }
            finally { currentSocket = null; }
        }
    }

    public void Reconnect() => currentSocket?.Abort();

    static async Task<bool> GetActive(ClientWebSocket socket, string requestType, string property, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        await SendJson(socket, new { op = 6, d = new { requestType, requestId = id } }, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var message = await ReceiveJson(socket, timeout.Token);
            var root = message.RootElement;
            if (root.GetProperty("op").GetInt32() != 7) continue;
            var data = root.GetProperty("d");
            if (data.GetProperty("requestId").GetString() != id) continue;
            if (!data.GetProperty("requestStatus").GetProperty("result").GetBoolean()) return false;
            var payload = data.GetProperty("responseData");
            return payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }

    static string Authenticate(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    static Task SendJson(ClientWebSocket socket, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    static async Task<JsonDocument> ReceiveJson(ClientWebSocket socket, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var part = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(part, token);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("OBS closed the connection");
            buffer.Write(part, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.ToArray());
    }

    static async Task<JsonDocument> ReceiveUntilOp(ClientWebSocket socket, int operation, CancellationToken token)
    {
        while (true)
        {
            var message = await ReceiveJson(socket, token);
            if (message.RootElement.TryGetProperty("op", out var op) && op.GetInt32() == operation) return message;
            message.Dispose();
        }
    }

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

/// <summary>Protects the OBS password with Windows DPAPI for the current user and machine.</summary>
internal static class DpapiSecret
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    const int UiForbidden = 1;

    public static string Protect(string text)
    {
        if (text.Length == 0) return "";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var input = MakeBlob(bytes);
        try
        {
            if (!CryptProtectData(ref input, "Shadeplay OBS password", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var encrypted = new byte[output.Length]; Marshal.Copy(output.Data, encrypted, 0, output.Length); return Convert.ToBase64String(encrypted); }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }

    public static string Unprotect(string text)
    {
        if (text.Length == 0) return "";
        byte[] bytes;
        try { bytes = Convert.FromBase64String(text); } catch { return ""; }
        var input = MakeBlob(bytes);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var output)) return "";
            try { var clear = new byte[output.Length]; Marshal.Copy(output.Data, clear, 0, output.Length); return Encoding.UTF8.GetString(clear); }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }

    static Blob MakeBlob(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new Blob { Length = bytes.Length, Data = data };
    }
}
