using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using TcpUdpTester.Core.Chunkers;
using TcpUdpTester.Models;

namespace TcpUdpTester.Core;

public sealed class NetService : INetService, IDisposable
{
    // --- Reactive streams ---
    private readonly Subject<LogEntry> _logSubject = new();
    private readonly Subject<StatsSnapshot> _statsSubject = new();
    private readonly Subject<StateSnapshot> _stateSubject = new();

    public IObservable<LogEntry> LogStream => _logSubject;
    public IObservable<StatsSnapshot> StatsStream => _statsSubject;
    public IObservable<StateSnapshot> StateStream => _stateSubject;

    // --- Statistics ---
    private long _txBytes, _rxBytes, _txCount, _rxCount, _errorCount;
    private long _txBytesLast, _rxBytesLast;
    private DateTimeOffset _lastStatsTime = DateTimeOffset.UtcNow;
    private readonly Timer _statsTimer;

    // --- TCP Client ---
    private TcpClient? _tcpClient;
    private CancellationTokenSource? _tcpClientCts;
    private string _tcpClientSessionId = "";
    private string _tcpClientRemote = "";

    // --- TCP Server ---
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _tcpServerCts;
    private readonly ConcurrentDictionary<string, (TcpClient Client, CancellationTokenSource Cts)> _serverSessions = new();

    // --- UDP ---
    private UdpClient? _udpClient;
    private CancellationTokenSource? _udpCts;
    private string _udpRemoteHost = "";
    private int _udpRemotePort;

    // --- UART ---
    private SerialPort? _serialPort;
    private CancellationTokenSource? _uartCts;
    private string _uartPortName = "";

    public NetService()
    {
        _statsTimer = new Timer(_ => PublishStats(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    // ============================================================
    // Stats
    // ============================================================
    private void PublishStats()
    {
        var now = DateTimeOffset.UtcNow;
        double elapsed = (now - _lastStatsTime).TotalSeconds;
        long curTx = Interlocked.Read(ref _txBytes);
        long curRx = Interlocked.Read(ref _rxBytes);
        double txBps = elapsed > 0 ? (curTx - _txBytesLast) * 8.0 / elapsed : 0;
        double rxBps = elapsed > 0 ? (curRx - _rxBytesLast) * 8.0 / elapsed : 0;
        _txBytesLast = curTx;
        _rxBytesLast = curRx;
        _lastStatsTime = now;

        _statsSubject.OnNext(new StatsSnapshot(
            curTx, curRx,
            Interlocked.Read(ref _txCount),
            Interlocked.Read(ref _rxCount),
            txBps, rxBps,
            Interlocked.Read(ref _errorCount)));
    }

    // ============================================================
    // TCP Client
    // ============================================================
    public async Task TcpClientConnectAsync(string host, int port, ChunkMode chunkMode = ChunkMode.Raw, SocketOptions? socketOpts = null)
    {
        await TcpClientDisconnectAsync();

        _tcpClientSessionId = GenerateSessionId();
        _tcpClientRemote = $"{host}:{port}";
        _tcpClient = new TcpClient();
        if (socketOpts?.RecvBufSize > 0) _tcpClient.ReceiveBufferSize = socketOpts.RecvBufSize;
        if (socketOpts?.SendBufSize > 0) _tcpClient.SendBufferSize    = socketOpts.SendBufSize;

        _stateSubject.OnNext(new StateSnapshot("TCP Client", "Connecting", _tcpClientSessionId, _tcpClientRemote));
        try
        {
            await _tcpClient.ConnectAsync(host, port);
            _stateSubject.OnNext(new StateSnapshot("TCP Client", "Connected", _tcpClientSessionId, _tcpClientRemote));
            EmitEvent(Protocol.TCP, _tcpClientSessionId, _tcpClientRemote, $"[接続]");

            _tcpClientCts = new CancellationTokenSource();
            _ = Task.Run(() => TcpReceiveLoopAsync(
                _tcpClient, _tcpClientSessionId, _tcpClientRemote,
                CreateChunker(chunkMode), socketOpts?.RecvBufSize ?? 0, _tcpClientCts.Token));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _stateSubject.OnNext(new StateSnapshot("TCP Client", $"Error: {ex.Message}", _tcpClientSessionId, _tcpClientRemote));
            EmitEvent(Protocol.TCP, _tcpClientSessionId, _tcpClientRemote, $"[エラー] {ex.Message}");
            _tcpClient.Dispose();
            _tcpClient = null;
            _tcpClientSessionId = "";
            _tcpClientRemote = "";
        }
    }

    public async Task TcpClientDisconnectAsync()
    {
        var sessionId = _tcpClientSessionId;
        var remote = _tcpClientRemote;
        _tcpClientCts?.Cancel();
        _tcpClient?.Close();
        _tcpClient?.Dispose();
        _tcpClient = null;
        _tcpClientCts = null;
        _tcpClientSessionId = "";
        _tcpClientRemote = "";
        if (!string.IsNullOrEmpty(sessionId))
            EmitEvent(Protocol.TCP, sessionId, remote, "[切断]");
        _stateSubject.OnNext(new StateSnapshot("TCP Client", "Disconnected", "", ""));
        await Task.CompletedTask;
    }

    // ============================================================
    // TCP Server
    // ============================================================
    public async Task TcpServerStartAsync(string bindIp, int port, ChunkMode chunkMode = ChunkMode.Raw, SocketOptions? socketOpts = null)
    {
        await TcpServerStopAsync();

        var ip = string.IsNullOrWhiteSpace(bindIp) ? IPAddress.Any : IPAddress.Parse(bindIp);
        _tcpListener = new TcpListener(ip, port);
        _tcpListener.Start();
        _tcpServerCts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(chunkMode, socketOpts, _tcpServerCts.Token));

        var bindDisplay = string.IsNullOrWhiteSpace(bindIp) ? "0.0.0.0" : bindIp;
        var listenAddr = $"{bindDisplay}:{port}";
        _stateSubject.OnNext(new StateSnapshot("TCP Server", "Listening", "", listenAddr));
        EmitEvent(Protocol.TCP, "", listenAddr, $"[サーバー起動] {listenAddr}");
    }

    public async Task TcpServerStopAsync()
    {
        _tcpServerCts?.Cancel();
        _tcpListener?.Stop();
        _tcpListener = null;

        foreach (var (_, (client, cts)) in _serverSessions)
        {
            cts.Cancel();
            client.Close();
            client.Dispose();
        }
        _serverSessions.Clear();

        _stateSubject.OnNext(new StateSnapshot("TCP Server", "Stopped", "", ""));
        EmitEvent(Protocol.TCP, "", "", "[サーバー停止]");
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(ChunkMode chunkMode, SocketOptions? socketOpts, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                if (socketOpts?.RecvBufSize > 0) client.ReceiveBufferSize = socketOpts.RecvBufSize;
                if (socketOpts?.SendBufSize > 0) client.SendBufferSize    = socketOpts.SendBufSize;
                var sessionId = GenerateSessionId();
                var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _serverSessions[sessionId] = (client, cts);
                _stateSubject.OnNext(new StateSnapshot("TCP Server", $"Client connected", sessionId, remote));
                EmitEvent(Protocol.TCP, sessionId, remote, $"[クライアント接続]");
                _ = Task.Run(() => TcpReceiveLoopAsync(client, sessionId, remote, CreateChunker(chunkMode), socketOpts?.RecvBufSize ?? 0, cts.Token));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _errorCount);
                    EmitEvent(Protocol.TCP, "", "", $"[エラー] Accept失敗: {ex.Message}");
                }
            }
        }
    }

    private async Task TcpReceiveLoopAsync(
        TcpClient client, string sessionId, string remote, IChunker chunker, int recvBufSize, CancellationToken ct)
    {
        bool remoteDisconnected = false;
        try
        {
            var stream = client.GetStream();
            var buf = new byte[recvBufSize > 0 ? recvBufSize : 65536];
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                if (read == 0) { remoteDisconnected = true; break; }
                foreach (var chunk in chunker.Push(buf.AsSpan(0, read)))
                    EmitRx(Protocol.TCP, sessionId, remote, chunk);
            }
            foreach (var chunk in chunker.Flush())
                EmitRx(Protocol.TCP, sessionId, remote, chunk);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            EmitEvent(Protocol.TCP, sessionId, remote, $"[エラー] {ex.Message}");
        }
        finally
        {
            if (_serverSessions.TryRemove(sessionId, out var s))
            {
                s.Client.Dispose();
                _stateSubject.OnNext(new StateSnapshot("TCP Server", "Client disconnected", sessionId, remote));
                EmitEvent(Protocol.TCP, sessionId, remote, remoteDisconnected ? "[クライアント切断] 接続が閉じられました" : "[クライアント切断]");
            }
            else if (remoteDisconnected)
            {
                EmitEvent(Protocol.TCP, sessionId, remote, "[切断] リモートが接続を切断しました");
            }
        }
    }

    // ============================================================
    // UDP
    // ============================================================
    public async Task UdpStartAsync(int localPort, string remoteHost, int remotePort, SocketOptions? socketOpts = null)
    {
        await UdpStopAsync();
        _udpRemoteHost = remoteHost;
        _udpRemotePort = remotePort;
        _udpClient = new UdpClient(localPort);
        if (socketOpts?.RecvBufSize > 0) _udpClient.Client.ReceiveBufferSize = socketOpts.RecvBufSize;
        if (socketOpts?.SendBufSize > 0) _udpClient.Client.SendBufferSize    = socketOpts.SendBufSize;
        _udpCts = new CancellationTokenSource();
        _ = Task.Run(() => UdpReceiveLoopAsync(_udpCts.Token));
        var udpInfo = $"Local:{localPort} Remote:{remoteHost}:{remotePort}";
        _stateSubject.OnNext(new StateSnapshot("UDP", "Active", "", udpInfo));
        EmitEvent(Protocol.UDP, "", $":{localPort}", $"[UDP 開始] {udpInfo}");
        await Task.CompletedTask;
    }

    public async Task UdpStopAsync()
    {
        _udpCts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _stateSubject.OnNext(new StateSnapshot("UDP", "Stopped", "", ""));
        EmitEvent(Protocol.UDP, "", "", "[UDP 停止]");
        await Task.CompletedTask;
    }

    private async Task UdpReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpClient!.ReceiveAsync(ct);
                var remote = result.RemoteEndPoint.ToString();
                EmitRx(Protocol.UDP, "udp", remote, result.Buffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            EmitEvent(Protocol.UDP, "", "", $"[エラー] {ex.Message}");
        }
    }

    // ============================================================
    // UART
    // ============================================================
    public async Task UartOpenAsync(string portName, UartOptions? opts = null, ChunkMode chunkMode = ChunkMode.Raw)
    {
        await UartCloseAsync();
        opts ??= new UartOptions();
        _uartPortName = portName;

        _stateSubject.OnNext(new StateSnapshot("UART", "Opening", portName, portName));
        try
        {
            var port = new SerialPort(portName, opts.BaudRate, opts.Parity, opts.DataBits, opts.StopBits)
            {
                Handshake = opts.Handshake,
                ReadTimeout  = SerialPort.InfiniteTimeout,
                WriteTimeout = SerialPort.InfiniteTimeout,
            };
            port.Open();
            _serialPort = port;
            _uartCts = new CancellationTokenSource();
            _stateSubject.OnNext(new StateSnapshot("UART", "Opened", portName, portName));
            EmitEvent(Protocol.UART, portName, portName, $"[開始] {portName} {opts.BaudRate}bps");
            _ = Task.Run(() => UartReceiveLoopAsync(port, portName, CreateChunker(chunkMode), _uartCts.Token));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _stateSubject.OnNext(new StateSnapshot("UART", $"Error: {ex.Message}", portName, portName));
            EmitEvent(Protocol.UART, portName, portName, $"[エラー] {ex.Message}");
            _serialPort = null;
            _uartPortName = "";
        }
    }

    public async Task UartCloseAsync()
    {
        var portName = _uartPortName;
        _uartCts?.Cancel();
        try { _serialPort?.Close(); } catch { }
        _serialPort?.Dispose();
        _serialPort = null;
        _uartCts = null;
        _uartPortName = "";
        if (!string.IsNullOrEmpty(portName))
            EmitEvent(Protocol.UART, portName, portName, "[停止]");
        _stateSubject.OnNext(new StateSnapshot("UART", "Closed", "", ""));
        await Task.CompletedTask;
    }

    public IReadOnlyList<string> GetSerialPorts() => SerialPort.GetPortNames();

    private async Task UartReceiveLoopAsync(SerialPort port, string portName, IChunker chunker, CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await port.BaseStream.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                if (read == 0) break;
                foreach (var chunk in chunker.Push(buf.AsSpan(0, read)))
                    EmitRx(Protocol.UART, portName, portName, chunk);
            }
            foreach (var chunk in chunker.Flush())
                EmitRx(Protocol.UART, portName, portName, chunk);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            EmitEvent(Protocol.UART, portName, portName, $"[受信エラー] {ex.Message}");
            _stateSubject.OnNext(new StateSnapshot("UART", $"Error: {ex.Message}", portName, portName));
        }
    }

    // ============================================================
    // Send
    // ============================================================
    public async Task SendAsync(SendRequest request)
    {
        var chunks = request.Options.SplitEnabled
            ? SplitData(request.Data, request.Options)
            : (IEnumerable<byte[]>)[request.Data];

        int repeatCount = request.Options.RepeatEnabled ? Math.Max(1, request.Options.RepeatCount) : 1;

        for (int r = 0; r < repeatCount; r++)
        {
            for (int burst = 0; burst < Math.Max(1, request.Options.BurstCount); burst++)
            {
                foreach (var chunk in chunks)
                {
                    await SendChunkAsync(request.Protocol, request.TargetId, chunk);
                    if (request.Options.InterChunkDelayMs > 0)
                        await Task.Delay(request.Options.InterChunkDelayMs);
                }
            }
            if (r < repeatCount - 1 && request.Options.RepeatIntervalMs > 0)
                await Task.Delay(request.Options.RepeatIntervalMs);
        }
    }

    private static IEnumerable<byte[]> SplitData(byte[] data, SendOptions opts)
    {
        var rng = new Random();
        int offset = 0;
        while (offset < data.Length)
        {
            int size = opts.SplitRandom
                ? rng.Next(1, Math.Max(2, opts.SplitRandomMaxSize))
                : opts.SplitFixedSize;
            size = Math.Min(size, data.Length - offset);
            yield return data.AsSpan(offset, size).ToArray();
            offset += size;
        }
    }

    private async Task SendChunkAsync(Protocol protocol, string targetId, byte[] data)
    {
        try
        {
            if (protocol == Protocol.TCP)
            {
                NetworkStream? stream = null;
                string remote = "";

                if (string.IsNullOrEmpty(targetId) || targetId == _tcpClientSessionId)
                {
                    stream = _tcpClient?.GetStream();
                    remote = _tcpClientRemote;
                }
                else if (_serverSessions.TryGetValue(targetId, out var session))
                {
                    stream = session.Client.GetStream();
                    remote = session.Client.Client.RemoteEndPoint?.ToString() ?? targetId;
                }

                if (stream != null)
                {
                    await stream.WriteAsync(data);
                    EmitTx(Protocol.TCP, targetId, remote, data);
                }
            }
            else if (protocol == Protocol.UDP && _udpClient != null)
            {
                if (!string.IsNullOrEmpty(_udpRemoteHost) && _udpRemotePort > 0)
                {
                    await _udpClient.SendAsync(data, _udpRemoteHost, _udpRemotePort);
                    EmitTx(Protocol.UDP, "udp", $"{_udpRemoteHost}:{_udpRemotePort}", data);
                }
            }
            else if (protocol == Protocol.UART && _serialPort?.IsOpen == true)
            {
                await _serialPort.BaseStream.WriteAsync(data);
                EmitTx(Protocol.UART, _uartPortName, _uartPortName, data);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            EmitEvent(protocol, targetId, "", $"[送信エラー] {ex.Message}");
        }
    }

    // ============================================================
    // Helpers
    // ============================================================
    private void EmitRx(Protocol protocol, string sessionId, string remote, byte[] data)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, protocol, Direction.RX, sessionId, remote, data.Length, data);
        _logSubject.OnNext(entry);
        Interlocked.Add(ref _rxBytes, data.Length);
        Interlocked.Increment(ref _rxCount);
    }

    private void EmitTx(Protocol protocol, string sessionId, string remote, byte[] data)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, protocol, Direction.TX, sessionId, remote, data.Length, data);
        _logSubject.OnNext(entry);
        Interlocked.Add(ref _txBytes, data.Length);
        Interlocked.Increment(ref _txCount);
    }

    private void EmitEvent(Protocol protocol, string sessionId, string remote, string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        var entry = new LogEntry(DateTimeOffset.UtcNow, protocol, Direction.Event, sessionId, remote, 0, data);
        _logSubject.OnNext(entry);
    }

    public IReadOnlyList<string> GetActiveSessions()
    {
        var list = new List<string>();
        if (_tcpClient?.Connected == true) list.Add(_tcpClientSessionId);
        list.AddRange(_serverSessions.Keys);
        if (_serialPort?.IsOpen == true) list.Add(_uartPortName);
        return list;
    }

    private static IChunker CreateChunker(ChunkMode mode) => mode switch
    {
        ChunkMode.FixedLength => new FixedLengthChunker(256),
        ChunkMode.Delimiter   => new DelimiterChunker([0x0A]),
        ChunkMode.TimeSlice   => new TimeSliceChunker(50),
        ChunkMode.Line        => new LineChunker(),
        _                     => new RawChunker()
    };

    private static string GenerateSessionId() => Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        _statsTimer.Dispose();
        _tcpClientCts?.Cancel();
        _tcpClient?.Dispose();
        _tcpServerCts?.Cancel();
        _tcpListener?.Stop();
        foreach (var (_, (client, cts)) in _serverSessions) { cts.Cancel(); client.Dispose(); }
        _udpCts?.Cancel();
        _udpClient?.Dispose();
        _uartCts?.Cancel();
        try { _serialPort?.Close(); } catch { }
        _serialPort?.Dispose();
        _logSubject.Dispose();
        _statsSubject.Dispose();
        _stateSubject.Dispose();
    }
}
