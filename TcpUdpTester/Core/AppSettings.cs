using TcpUdpTester.Models;
using TcpUdpTester.ViewModels;

namespace TcpUdpTester.Core;

public sealed class AppSettings
{
    // Window
    public double WindowLeft      { get; set; } = double.NaN;
    public double WindowTop       { get; set; } = double.NaN;
    public double WindowWidth     { get; set; } = 1280;
    public double WindowHeight    { get; set; } = 820;
    public bool   WindowMaximized { get; set; }

    // Tab selection
    public int SelectedTabIndex { get; set; }

    // Logging
    public bool   LogEnabled { get; set; }
    public string LogFolder  { get; set; } = "";

    // TCP Client
    public string    TcpClientHost      { get; set; } = "127.0.0.1";
    public string    TcpClientPort      { get; set; } = "8080";
    public ChunkMode TcpClientChunkMode { get; set; } = ChunkMode.Raw;

    // TCP Server
    public string    TcpServerBindIp    { get; set; } = "";
    public string    TcpServerPort      { get; set; } = "8080";
    public ChunkMode TcpServerChunkMode { get; set; } = ChunkMode.Raw;

    // UDP
    public string UdpLocalPort  { get; set; } = "9090";
    public string UdpRemoteHost { get; set; } = "127.0.0.1";
    public string UdpRemotePort { get; set; } = "9090";

    // Send panel
    public SendMode SendMode           { get; set; } = SendMode.Text;
    public string   SendTextInput      { get; set; } = "";
    public string   SendHexInput       { get; set; } = "";
    public string   SendFilePath       { get; set; } = "";
    public bool     RepeatEnabled      { get; set; }
    public int      RepeatCount        { get; set; } = 1;
    public int      RepeatIntervalMs   { get; set; } = 1000;
    public bool     SplitEnabled       { get; set; }
    public int      SplitFixedSize     { get; set; } = 64;
    public bool     SplitRandom        { get; set; }
    public int      SplitRandomMaxSize { get; set; } = 128;
    public int      InterChunkDelayMs  { get; set; }
    public int      RandomMinSize      { get; set; } = 1;
    public int      RandomMaxSize      { get; set; } = 256;
    public bool     SeqSuffixEnabled   { get; set; }
    public int      SeqSuffixDigits    { get; set; } = 4;
    public bool     LoadTestEnabled    { get; set; }
    public int      LoadTestDurationSec{ get; set; } = 10;
    public double   LoadTestTargetMbps { get; set; }

    // Receive sequence check
    public bool SeqCheckEnabled { get; set; }
    public int  SeqCheckDigits  { get; set; } = 4;
}
