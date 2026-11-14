using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Kurotsuki.Core.Backup;

/// <summary>
/// What the QR code carries: where the desktop app listens on the local network and the one-time token that unlocks the
/// backup. <c>kurotsuki://transfer?h=192.168.1.5,10.0.0.3&amp;p=48213&amp;t=&lt;32 hex chars&gt;</c> (see the Java docs/backup-format.md).
/// </summary>
public sealed partial record TransferLink(IReadOnlyList<string> Hosts, int Port, string Token)
{
    public const string Scheme = "kurotsuki";

    public string ToUri() => $"{Scheme}://transfer?h={string.Join(",", Hosts)}&p={Port}&t={Uri.EscapeDataString(Token)}";

    public string UrlFor(string host) => $"http://{host}:{Port}/backup?t={Token}";

    /// <summary>Null for anything that is not a valid link (wrong scheme, bad token, public address…).</summary>
    public static TransferLink? Parse(string? text)
    {
        if (text is null || !text.StartsWith(Scheme + "://transfer?", StringComparison.Ordinal))
        {
            return null;
        }
        string? hosts = null, token = null;
        int? port = null;
        foreach (var pair in text[(text.IndexOf('?') + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            switch (pair[..eq])
            {
                case "h": hosts = value; break;
                case "p": if (!int.TryParse(value, out var p)) return null; port = p; break;
                case "t": token = value; break;
            }
        }
        if (hosts is null || port is not (>= 1 and <= 65535) || token is null || !TokenPattern().IsMatch(token))
        {
            return null;
        }
        var list = hosts.Split(',').ToList();
        return list.All(IsPrivateAddress) && list.Count > 0 ? new TransferLink(list, port.Value, token) : null;
    }

    /// <summary>Only addresses of the local network may be used: a QR must never make an app contact the internet.</summary>
    public static bool IsPrivateAddress(string? host)
    {
        var m = Ipv4().Match(host ?? "");
        if (!m.Success)
        {
            return false;
        }
        var o = Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
        if (o.Any(x => x > 255))
        {
            return false;
        }
        return o[0] == 10 || (o[0] == 172 && o[1] is >= 16 and <= 31) || (o[0] == 192 && o[1] == 168)
            || (o[0] == 169 && o[1] == 254) || o[0] == 127;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex TokenPattern();
    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$")] private static partial Regex Ipv4();
}

/// <summary>
/// A short-lived HTTP server on the local network that hands the backup to the phone that scanned the QR code. It serves
/// the backup once, only to a request carrying the random token, and stops after the first successful transfer, after too
/// many wrong tokens, or when the time is up. Hand-rolled on <see cref="TcpListener"/> so it needs no admin rights on Windows.
/// </summary>
public sealed class TransferServer : IDisposable
{
    public enum State { Waiting, Sent, Expired, Stopped }

    public const int PreferredPort = 47835;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    private const int MaxBadAttempts = 5;
    private const int MaxRequestBytes = 8192;

    private readonly TcpListener _listener;
    private readonly Func<string> _backup;
    private readonly Action<State> _onState;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private int _badAttempts;

    public string Token { get; }
    public int Port { get; }
    public State Current { get; private set; } = State.Waiting;

    /// <param name="backup">Produces the backup JSON when the phone asks, so it is as fresh as possible.</param>
    /// <param name="onState">Called (on a server thread) when the state changes.</param>
    public TransferServer(Func<string> backup, TimeSpan lifetime, Action<State> onState)
    {
        _backup = backup;
        _onState = onState;
        Token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _listener = Bind(out var port);
        Port = port;
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _ = Task.Delay(lifetime, _stop.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Finish(State.Expired);
            }
        });
    }

    /// <summary>A fixed port, so a firewall rule can allow it once; a free one only if another program has it.</summary>
    private static TcpListener Bind(out int port)
    {
        foreach (var candidate in new[] { PreferredPort, 0 })
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, candidate);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return new TcpListener(IPAddress.Any, port);
            }
            catch (SocketException) when (candidate != 0)
            {
            }
        }
        throw new IOException("Could not open a port for the transfer");
    }

    public TransferLink Link()
    {
        var hosts = LocalAddresses();
        return new TransferLink(hosts.Count > 0 ? hosts : ["127.0.0.1"], Port, Token);
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Handle(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 5000;
                var stream = client.GetStream();
                var request = await ReadRequest(stream);
                var ok = Current == State.Waiting && request.StartsWith("GET /backup?t=", StringComparison.Ordinal)
                    && CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(request["GET /backup?t=".Length..].Split(' ')[0]), Encoding.UTF8.GetBytes(Token));
                if (!ok)
                {
                    bool tooMany;
                    lock (_gate)
                    {
                        tooMany = ++_badAttempts >= MaxBadAttempts;
                    }
                    await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
                    if (tooMany)
                    {
                        Finish(State.Stopped);
                    }
                    return;
                }
                var body = Encoding.UTF8.GetBytes(_backup());
                var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\n"
                    + $"Cache-Control: no-store\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                return; // a client that vanished does not count as a transfer
            }
        }
        Finish(State.Sent);
    }

    private static async Task<string> ReadRequest(NetworkStream stream)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0)
            {
                break;
            }
            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n"))
            {
                break;
            }
        }
        var text = Encoding.ASCII.GetString(buffer, 0, length);
        return text[..Math.Max(0, text.IndexOf("\r\n", StringComparison.Ordinal) is var i and >= 0 ? i : text.Length)];
    }

    private void Finish(State result)
    {
        lock (_gate)
        {
            if (Current != State.Waiting)
            {
                return;
            }
            Current = result;
        }
        _stop.Cancel();
        _listener.Stop();
        _onState(result);
    }

    /// <summary>Stops the server without a transfer (the user closed the window).</summary>
    public void Dispose() => Finish(State.Stopped);

    /// <summary>Private IPv4 addresses of this computer's active interfaces, 192.168.x.x first.</summary>
    public static List<string> LocalAddresses()
    {
        var found = new List<string>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork
                        && TransferLink.IsPrivateAddress(address.Address.ToString())
                        && !address.Address.ToString().StartsWith("127."))
                    {
                        found.Add(address.Address.ToString());
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            return [];
        }
        return [.. found.OrderBy(a => a.StartsWith("192.168.") ? 0 : 1)];
    }
}
