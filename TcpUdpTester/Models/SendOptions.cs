namespace TcpUdpTester.Models;

public sealed class SendOptions
{
    public bool RepeatEnabled { get; set; }
    public int RepeatIntervalMs { get; set; } = 1000;
    public int RepeatCount { get; set; } = 1;
    public int BurstCount { get; set; } = 1;
    public bool SplitEnabled { get; set; }
    public int SplitFixedSize { get; set; } = 64;
    public bool SplitRandom { get; set; }
    public int SplitRandomMaxSize { get; set; } = 128;
    public int InterChunkDelayMs { get; set; }
}
