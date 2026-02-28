namespace TcpUdpTester.Models;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    Protocol Protocol,
    Direction Direction,
    string SessionId,
    string Remote,
    int Length,
    byte[] Data
);
