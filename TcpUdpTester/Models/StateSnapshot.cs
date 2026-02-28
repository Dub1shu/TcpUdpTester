namespace TcpUdpTester.Models;

public sealed record StateSnapshot(
    string Mode,
    string ConnectionState,
    string SessionId,
    string RemoteEndpoint
);
