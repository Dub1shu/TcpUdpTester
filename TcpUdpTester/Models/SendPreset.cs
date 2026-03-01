namespace TcpUdpTester.Models;

public sealed class SendPreset
{
    public string Name { get; set; } = "";

    // Input
    public SendMode SendMode  { get; set; } = SendMode.Text;
    public string   TextInput { get; set; } = "";
    public string   HexInput  { get; set; } = "";
    public string   FilePath  { get; set; } = "";
    public Protocol Protocol  { get; set; } = Protocol.TCP;
    public string   TargetId  { get; set; } = "";

    // Repeat
    public bool RepeatEnabled    { get; set; }
    public int  RepeatCount      { get; set; } = 1;
    public int  RepeatIntervalMs { get; set; } = 1000;

    // Split
    public bool SplitEnabled      { get; set; }
    public int  SplitFixedSize    { get; set; } = 64;
    public bool SplitRandom       { get; set; }
    public int  SplitRandomMaxSize{ get; set; } = 128;
    public int  InterChunkDelayMs { get; set; }

    // Random data
    public int RandomMinSize { get; set; } = 1;
    public int RandomMaxSize { get; set; } = 256;

    // Sequential suffix
    public bool SeqSuffixEnabled { get; set; }
    public int  SeqSuffixDigits  { get; set; } = 4;

    // Load test
    public bool   LoadTestEnabled     { get; set; }
    public int    LoadTestDurationSec { get; set; } = 10;
    public double LoadTestTargetMbps  { get; set; }

    public override string ToString() => Name;
}
