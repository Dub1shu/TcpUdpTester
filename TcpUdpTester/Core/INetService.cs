using TcpUdpTester.Models;

namespace TcpUdpTester.Core;

public interface INetService
{
    IObservable<LogEntry> LogStream { get; }
    IObservable<StatsSnapshot> StatsStream { get; }
    IObservable<StateSnapshot> StateStream { get; }

    Task TcpClientConnectAsync(string host, int port, ChunkMode chunkMode = ChunkMode.Raw);
    Task TcpClientDisconnectAsync();

    Task TcpServerStartAsync(string bindIp, int port, ChunkMode chunkMode = ChunkMode.Raw);
    Task TcpServerStopAsync();

    Task UdpStartAsync(int localPort, string remoteHost, int remotePort);
    Task UdpStopAsync();

    Task SendAsync(SendRequest request);

    IReadOnlyList<string> GetActiveSessions();
}
