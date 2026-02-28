namespace TcpUdpTester.Models;

public sealed record SendRequest(
    Protocol Protocol,
    string TargetId,
    byte[] Data,
    SendOptions Options
);
