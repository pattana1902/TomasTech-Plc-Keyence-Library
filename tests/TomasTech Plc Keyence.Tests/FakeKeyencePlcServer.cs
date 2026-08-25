using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TomasTech.Plc.Keyence.Tests;

/// <summary>
/// Minimal loopback TCP server that speaks just enough of the Keyence Upper Link (ASCII) protocol
/// to test <c>KeyenceTcpClient</c> end to end without real PLC hardware — every command it receives
/// is recorded (so tests can assert exactly what was sent on the wire) and answered via a
/// caller-supplied responder.
/// </summary>
sealed class FakeKeyencePlcServer : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly List<string> _receivedCommands = new();
    readonly Func<string, string> _responder;
    readonly CancellationTokenSource _cts = new();
    Task? _acceptLoop;

    public int Port { get; }
    public IReadOnlyList<string> ReceivedCommands => _receivedCommands;

    public FakeKeyencePlcServer(Func<string, string> responder)
    {
        _responder = responder;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Start() => _acceptLoop = AcceptLoopAsync(_cts.Token);

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                await HandleClientAsync(client, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        var buffer = new byte[1024];

        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }

            if (read == 0) return;

            var command = Encoding.ASCII.GetString(buffer, 0, read).TrimEnd('\r', '\n');
            lock (_receivedCommands) _receivedCommands.Add(command);

            var reply = _responder(command);
            var replyBytes = Encoding.ASCII.GetBytes(reply + "\r");
            await stream.WriteAsync(replyBytes, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch { /* best-effort shutdown */ }
        }
        _cts.Dispose();
    }
}
