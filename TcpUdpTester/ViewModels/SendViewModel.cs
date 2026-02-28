using System.Globalization;
using System.IO;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public enum SendMode { Text, Hex, File, Random }

public sealed class SendViewModel : ViewModelBase
{
    private readonly INetService _net;
    private CancellationTokenSource? _sendCts;

    // --- Input ---
    private SendMode _sendMode  = SendMode.Text;
    private string   _textInput = "";
    private string   _hexInput  = "";
    private string   _filePath  = "";

    // --- Repeat ---
    private bool _repeatEnabled;
    private int  _repeatCount      = 1;
    private int  _repeatIntervalMs = 1000;

    // --- Split ---
    private bool _splitEnabled;
    private int  _splitFixedSize     = 64;
    private bool _splitRandom;
    private int  _splitRandomMaxSize = 128;
    private int  _interChunkDelayMs;

    // --- Random data ---
    private int _randomMinSize = 1;
    private int _randomMaxSize = 256;

    // --- Sequential suffix ---
    private bool _seqSuffixEnabled;
    private int  _seqSuffixDigits = 4;
    private long _seqCounter;

    // --- Load test ---
    private bool   _loadTestEnabled;
    private int    _loadTestDurationSec = 10;
    private double _loadTestTargetMbps;

    // --- Status ---
    private bool     _isSending;
    private string   _sendStatus = "";
    private Protocol _protocol   = Protocol.TCP;
    private string   _targetId   = "";

    public SendViewModel(INetService net)
    {
        _net = net;
        SendCommand            = new RelayCommand(async () => await SendAsync(),    () => !IsSending);
        StopCommand            = new RelayCommand(() => _sendCts?.Cancel(),          () => IsSending);
        BrowseFileCommand      = new RelayCommand(BrowseFile);
        ResetSeqCounterCommand = new RelayCommand(() => Interlocked.Exchange(ref _seqCounter, 0));
    }

    // --- Input properties ---
    public SendMode SendMode { get => _sendMode; set => Set(ref _sendMode, value); }
    public IReadOnlyList<SendMode> SendModes { get; } = Enum.GetValues<SendMode>().ToList();
    public string TextInput { get => _textInput; set => Set(ref _textInput, value); }
    public string HexInput  { get => _hexInput;  set => Set(ref _hexInput, value); }
    public string FilePath  { get => _filePath;  set => Set(ref _filePath, value); }

    // --- Repeat properties ---
    public bool RepeatEnabled    { get => _repeatEnabled;    set => Set(ref _repeatEnabled, value); }
    public int  RepeatCount      { get => _repeatCount;      set => Set(ref _repeatCount, value); }
    public int  RepeatIntervalMs { get => _repeatIntervalMs; set => Set(ref _repeatIntervalMs, value); }

    // --- Split properties ---
    public bool SplitEnabled      { get => _splitEnabled;      set => Set(ref _splitEnabled, value); }
    public int  SplitFixedSize    { get => _splitFixedSize;    set => Set(ref _splitFixedSize, value); }
    public bool SplitRandom       { get => _splitRandom;       set => Set(ref _splitRandom, value); }
    public int  SplitRandomMaxSize{ get => _splitRandomMaxSize;set => Set(ref _splitRandomMaxSize, value); }
    public int  InterChunkDelayMs { get => _interChunkDelayMs; set => Set(ref _interChunkDelayMs, value); }

    // --- Random data properties ---
    public int RandomMinSize { get => _randomMinSize; set => Set(ref _randomMinSize, value); }
    public int RandomMaxSize { get => _randomMaxSize; set => Set(ref _randomMaxSize, value); }

    // --- Sequential suffix properties ---
    public bool SeqSuffixEnabled { get => _seqSuffixEnabled; set => Set(ref _seqSuffixEnabled, value); }
    public int  SeqSuffixDigits  { get => _seqSuffixDigits;  set => Set(ref _seqSuffixDigits, value); }

    // --- Load test properties ---
    public bool   LoadTestEnabled     { get => _loadTestEnabled;     set => Set(ref _loadTestEnabled, value); }
    public int    LoadTestDurationSec { get => _loadTestDurationSec; set => Set(ref _loadTestDurationSec, value); }
    public double LoadTestTargetMbps  { get => _loadTestTargetMbps;  set => Set(ref _loadTestTargetMbps, value); }

    // --- Status properties ---
    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (Set(ref _isSending, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public string   SendStatus { get => _sendStatus; set => Set(ref _sendStatus, value); }
    public Protocol Protocol   { get => _protocol;   set => Set(ref _protocol, value); }
    public string   TargetId   { get => _targetId;   set => Set(ref _targetId, value); }

    public IReadOnlyList<Protocol> Protocols { get; } = Enum.GetValues<Protocol>().ToList();

    // --- Commands ---
    public RelayCommand SendCommand            { get; }
    public RelayCommand StopCommand            { get; }
    public RelayCommand BrowseFileCommand      { get; }
    public RelayCommand ResetSeqCounterCommand { get; }

    // ================================================================
    // Send entry point
    // ================================================================
    private async Task SendAsync()
    {
        if (IsSending) return;

        _sendCts = new CancellationTokenSource();
        IsSending = true;
        try
        {
            if (LoadTestEnabled)
                await RunLoadTestAsync(_sendCts.Token);
            else
                await RunNormalSendAsync(_sendCts.Token);
        }
        catch (OperationCanceledException)
        {
            SendStatus = "送信中断";
        }
        catch (Exception ex)
        {
            SendStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsSending = false;
            _sendCts?.Dispose();
            _sendCts = null;
        }
    }

    // ================================================================
    // Normal send (repeat by count)
    // ================================================================
    private async Task RunNormalSendAsync(CancellationToken ct)
    {
        bool needsPerIter = (SendMode == SendMode.Random) || SeqSuffixEnabled;
        if (!needsPerIter)
        {
            var data = BuildData();
            if (data is null || data.Length == 0) { SendStatus = "送信データがありません"; return; }
            SendStatus = "送信中...";
            await _net.SendAsync(new SendRequest(Protocol, TargetId, data, BuildFullOpts()));
            SendStatus = $"送信完了  {data.Length} bytes";
            return;
        }

        // Per-iteration: Random data (new each time) or SeqSuffix (increments each time)
        var opts  = BuildFullOpts();
        int count = opts.RepeatEnabled ? Math.Max(1, opts.RepeatCount) : 1;
        var singleOpts = BuildSingleOpts();

        byte[]? baseData = SendMode != SendMode.Random ? BuildData() : null;
        if (SendMode != SendMode.Random && (baseData is null || baseData.Length == 0))
        {
            SendStatus = "送信データがありません"; return;
        }

        SendStatus = "送信中...";
        int totalBytes = 0;
        for (int r = 0; r < count; r++)
        {
            ct.ThrowIfCancellationRequested();
            var data = BuildIterData(baseData);
            await _net.SendAsync(new SendRequest(Protocol, TargetId, data, singleOpts));
            totalBytes += data.Length;
            if (r < count - 1 && opts.RepeatIntervalMs > 0)
                await Task.Delay(opts.RepeatIntervalMs, ct);
        }
        SendStatus = $"送信完了  {totalBytes} bytes";
    }

    // ================================================================
    // Load test send (repeat by time, optional rate limit)
    // ================================================================
    private async Task RunLoadTestAsync(CancellationToken ct)
    {
        var singleOpts = BuildSingleOpts();
        byte[]? baseData = SendMode != SendMode.Random ? BuildData() : null;
        if (SendMode != SendMode.Random && (baseData is null || baseData.Length == 0))
        {
            SendStatus = "送信データがありません"; return;
        }

        double targetBytesPerSec = LoadTestTargetMbps > 0
            ? LoadTestTargetMbps * 1_000_000.0 / 8.0
            : 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long totalBytes   = 0;
        long packetCount  = 0;
        long lastUpdateMs = 0;

        while (!ct.IsCancellationRequested)
        {
            // Duration check
            if (LoadTestDurationSec > 0 && sw.Elapsed.TotalSeconds >= LoadTestDurationSec)
                break;

            // Rate limiting: if we're ahead of target, wait
            if (targetBytesPerSec > 0 && totalBytes > 0)
            {
                double expectedBytes = sw.Elapsed.TotalSeconds * targetBytesPerSec;
                double excessBytes   = totalBytes - expectedBytes;
                if (excessBytes > 0)
                {
                    int waitMs = (int)(excessBytes / targetBytesPerSec * 1000);
                    if (waitMs > 0)
                        await Task.Delay(waitMs, ct);
                }
            }

            var data = BuildIterData(baseData);
            await _net.SendAsync(new SendRequest(Protocol, TargetId, data, singleOpts));
            totalBytes  += data.Length;
            packetCount++;

            // Update status ~every 200ms
            long elapsedMs = sw.ElapsedMilliseconds;
            if (elapsedMs - lastUpdateMs >= 200)
            {
                lastUpdateMs = elapsedMs;
                double kbps = elapsedMs > 0 ? totalBytes * 8.0 / elapsedMs : 0;
                SendStatus = $"負荷送信中... {packetCount} pkts / {totalBytes:N0} B / {kbps:N0} kbps";
            }
        }

        double totalSec = sw.Elapsed.TotalSeconds;
        double avgKbps  = totalSec > 0 ? totalBytes * 8.0 / totalSec / 1000 : 0;
        SendStatus = $"完了  {packetCount:N0} pkts / {totalBytes:N0} B / 平均 {avgKbps:N0} kbps";
    }

    // ================================================================
    // Helpers
    // ================================================================
    private byte[] BuildIterData(byte[]? baseData)
    {
        var data = SendMode == SendMode.Random ? GenerateRandomData() : baseData!;
        if (SeqSuffixEnabled)
        {
            long seqNum = Interlocked.Increment(ref _seqCounter);
            var suffix = System.Text.Encoding.ASCII.GetBytes(
                seqNum.ToString().PadLeft(SeqSuffixDigits, '0'));
            data = [.. data, .. suffix];
        }
        return data;
    }

    private SendOptions BuildFullOpts() => new()
    {
        RepeatEnabled     = RepeatEnabled,
        RepeatCount       = RepeatCount,
        RepeatIntervalMs  = RepeatIntervalMs,
        SplitEnabled      = SplitEnabled,
        SplitFixedSize    = SplitFixedSize,
        SplitRandom       = SplitRandom,
        SplitRandomMaxSize= SplitRandomMaxSize,
        InterChunkDelayMs = InterChunkDelayMs,
    };

    private SendOptions BuildSingleOpts() => new()
    {
        SplitEnabled      = SplitEnabled,
        SplitFixedSize    = SplitFixedSize,
        SplitRandom       = SplitRandom,
        SplitRandomMaxSize= SplitRandomMaxSize,
        InterChunkDelayMs = InterChunkDelayMs,
    };

    private byte[] GenerateRandomData()
    {
        int min  = Math.Max(1, RandomMinSize);
        int max  = Math.Max(min, RandomMaxSize);
        int size = min == max ? min : Random.Shared.Next(min, max + 1);
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        return data;
    }

    private byte[]? BuildData() => SendMode switch
    {
        SendMode.Text   => string.IsNullOrEmpty(TextInput)
            ? null : System.Text.Encoding.UTF8.GetBytes(TextInput),
        SendMode.Hex    => ParseHex(HexInput),
        SendMode.File   => File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null,
        SendMode.Random => GenerateRandomData(),
        _               => null
    };

    private static byte[]? ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var tokens = hex.Split([' ', '\t', '\n', '\r', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<byte>();
        foreach (var t in tokens)
            if (byte.TryParse(t, NumberStyles.HexNumber, null, out byte b))
                result.Add(b);
        return result.Count > 0 ? [.. result] : null;
    }

    private void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "All Files|*.*" };
        if (dlg.ShowDialog() == true) FilePath = dlg.FileName;
    }
}
