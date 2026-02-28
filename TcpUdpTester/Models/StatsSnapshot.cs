namespace TcpUdpTester.Models;

public sealed record StatsSnapshot(
    long TxBytes,
    long RxBytes,
    long TxCount,
    long RxCount,
    double TxBps,
    double RxBps,
    long ErrorCount
);
