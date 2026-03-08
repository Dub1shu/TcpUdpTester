using TcpUdpTester.Models;

namespace TcpUdpTester.Core;

public interface INetService
{
    IObservable<LogEntry> LogStream { get; }
    IObservable<StatsSnapshot> StatsStream { get; }
    IObservable<StateSnapshot> StateStream { get; }

    Task TcpClientConnectAsync(string host, int port, ChunkMode chunkMode = ChunkMode.Raw, SocketOptions? socketOpts = null);
    Task TcpClientDisconnectAsync();

    Task TcpServerStartAsync(string bindIp, int port, ChunkMode chunkMode = ChunkMode.Raw, SocketOptions? socketOpts = null);
    Task TcpServerStopAsync();

    Task UdpStartAsync(int localPort, string remoteHost, int remotePort, SocketOptions? socketOpts = null);
    Task UdpStopAsync();

    Task SendAsync(SendRequest request);

    IReadOnlyList<string> GetActiveSessions();
}
