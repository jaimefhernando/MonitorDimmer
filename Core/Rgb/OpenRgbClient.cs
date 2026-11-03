using System.IO;
using System.Net.Sockets;
using System.Text;

namespace MonitorDimmer.Core.Rgb;

/// <summary>
/// Loads a saved profile in OpenRGB through its SDK server (OpenRGB → SDK Server → Start).
/// OpenRGB covers ASUS Aura, Corsair, Logitech, MSI, Gigabyte and more.
/// </summary>
internal static class OpenRgbClient
{
    const uint SetClientName = 50;
    const uint RequestProtocolVersion = 40;
    const uint RequestLoadProfile = 152;
    const uint ClientProtocolVersion = 3;

    public static async Task<bool> LoadProfileAsync(int port, string profile)
    {
        if (profile.Trim().Length == 0) return false;
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await tcp.ConnectAsync("127.0.0.1", port, cts.Token);
            var stream = tcp.GetStream();

            await SendAsync(stream, SetClientName, Encoding.ASCII.GetBytes("Monitor Dimmer\0"));
            await SendAsync(stream, RequestProtocolVersion, BitConverter.GetBytes(ClientProtocolVersion));
            // Reply: 16-byte header + server version
            var reply = new byte[20];
            await stream.ReadExactlyAsync(reply, cts.Token);
            uint serverVersion = BitConverter.ToUInt32(reply, 16);
            if (serverVersion < 2)
            {
                Log.Write($"OpenRGB protocol {serverVersion} cannot load profiles; update OpenRGB");
                return false;
            }

            await SendAsync(stream, RequestLoadProfile, Encoding.UTF8.GetBytes(profile.Trim() + "\0"));
            await stream.FlushAsync();
            Log.Write($"OpenRGB: loaded profile '{profile.Trim()}'");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"OpenRGB on port {port}: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> IsReachableAsync(int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await tcp.ConnectAsync("127.0.0.1", port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static async Task SendAsync(Stream stream, uint packetId, byte[] data)
    {
        var packet = new byte[16 + data.Length];
        Encoding.ASCII.GetBytes("ORGB").CopyTo(packet, 0);
        BitConverter.GetBytes(0u).CopyTo(packet, 4); // device index
        BitConverter.GetBytes(packetId).CopyTo(packet, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(packet, 12);
        data.CopyTo(packet, 16);
        await stream.WriteAsync(packet);
    }
}
