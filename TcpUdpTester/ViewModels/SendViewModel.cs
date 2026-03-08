using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

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
    private bool _repeatInfinite;
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
    private bool   _loadTestInfinite;
    private int    _loadTestDurationSec = 10;
    private double _loadTestTargetMbps;

    // --- Preset ---
    private SendPreset? _selectedPreset;
    private string      _newPresetName = "";

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
        SavePresetCommand      = new RelayCommand(SavePreset);
        DeletePresetCommand    = new RelayCommand(DeletePreset, () => _selectedPreset is not null);
    }

    // --- Preset properties ---
    public ObservableCollection<SendPreset> Presets { get; } = new();

    public SendPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (Set(ref _selectedPreset, value) && value is not null)
                ApplyPreset(value);
            DeletePresetCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set => Set(ref _newPresetName, value);
    }

    // --- Input properties ---
    public SendMode SendMode { get => _sendMode; set => Set(ref _sendMode, value); }
    public IReadOnlyList<SendMode> SendModes { get; } = Enum.GetValues<SendMode>().ToList();
    public string TextInput { get => _textInput; set => Set(ref _textInput, value); }
    public string HexInput  { get => _hexInput;  set => Set(ref _hexInput, value); }
    public string FilePath  { get => _filePath;  set => Set(ref _filePath, value); }

    // --- Repeat properties ---
    public bool RepeatEnabled
    {
        get => _repeatEnabled;
        set { if (Set(ref _repeatEnabled, value)) OnPropertyChanged(nameof(RepeatCountEnabled)); }
    }
    public bool RepeatInfinite
    {
        get => _repeatInfinite;
        set { if (Set(ref _repeatInfinite, value)) OnPropertyChanged(nameof(RepeatCountEnabled)); }
    }
    public bool RepeatCountEnabled => RepeatEnabled && !RepeatInfinite;
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
    public bool LoadTestEnabled
    {
        get => _loadTestEnabled;
        set { if (Set(ref _loadTestEnabled, value)) OnPropertyChanged(nameof(LoadTestDurationEnabled)); }
    }
    public bool LoadTestInfinite
    {
        get => _loadTestInfinite;
        set { if (Set(ref _loadTestInfinite, value)) OnPropertyChanged(nameof(LoadTestDurationEnabled)); }
    }
    public bool   LoadTestDurationEnabled => LoadTestEnabled && !LoadTestInfinite;
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
    public RelayCommand SavePresetCommand      { get; }
    public RelayCommand DeletePresetCommand    { get; }

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

        // --- Infinite loop mode ---
        if (RepeatEnabled && RepeatInfinite)
        {
            var singleOpts = BuildSingleOpts();
            byte[]? baseData = SendMode != SendMode.Random ? BuildData() : null;
            if (SendMode != SendMode.Random && (baseData is null || baseData.Length == 0))
            { SendStatus = "送信データがありません"; return; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long totalBytes  = 0;
            long packetCount = 0;
            long lastUpdateMs = 0;

            // UIスレッドを解放するため Task.Run でループをThreadPoolへ移す
            await Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var data = needsPerIter ? BuildIterData(baseData) : baseData!;
                    await _net.SendAsync(new SendRequest(Protocol, TargetId, data, singleOpts));
                    totalBytes  += data.Length;
                    packetCount++;

                    long elapsedMs = sw.ElapsedMilliseconds;
                    if (elapsedMs - lastUpdateMs >= 200)
                    {
                        lastUpdateMs = elapsedMs;
                        var status = $"繰り返し送信中... {packetCount} pkts / {totalBytes:N0} B";
                        Application.Current?.Dispatcher.InvokeAsync(() => SendStatus = status);
                    }

                    if (RepeatIntervalMs > 0)
                        await Task.Delay(RepeatIntervalMs, ct);
                }
            }, ct);
            return;
        }

        // --- Single or finite repeat ---
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
        var singleOpts2 = BuildSingleOpts();

        byte[]? baseData2 = SendMode != SendMode.Random ? BuildData() : null;
        if (SendMode != SendMode.Random && (baseData2 is null || baseData2.Length == 0))
        {
            SendStatus = "送信データがありません"; return;
        }

        SendStatus = "送信中...";
        int totalBytes2 = 0;
        await Task.Run(async () =>
        {
            for (int r = 0; r < count; r++)
            {
                ct.ThrowIfCancellationRequested();
                var data = BuildIterData(baseData2);
                await _net.SendAsync(new SendRequest(Protocol, TargetId, data, singleOpts2));
                totalBytes2 += data.Length;
                if (r < count - 1 && opts.RepeatIntervalMs > 0)
                    await Task.Delay(opts.RepeatIntervalMs, ct);
            }
        }, ct);
        SendStatus = $"送信完了  {totalBytes2} bytes";
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

        // UIスレッドを解放するため Task.Run でループをThreadPoolへ移す
        await Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                // Duration check
                if (!LoadTestInfinite && LoadTestDurationSec > 0 && sw.Elapsed.TotalSeconds >= LoadTestDurationSec)
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
                    var status = $"負荷送信中... {packetCount} pkts / {totalBytes:N0} B / {kbps:N0} kbps";
                    Application.Current?.Dispatcher.InvokeAsync(() => SendStatus = status);
                }
            }
        }, ct);

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

    // ================================================================
    // Preset management
    // ================================================================
    private void SavePreset()
    {
        var name = NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = CapturePreset(name);
        var existing = Presets.FirstOrDefault(p => p.Name == name);
        if (existing is not null)
        {
            var idx = Presets.IndexOf(existing);
            Presets[idx] = preset;
            _selectedPreset = preset;
            OnPropertyChanged(nameof(SelectedPreset));
        }
        else
        {
            Presets.Add(preset);
            _selectedPreset = preset;
            OnPropertyChanged(nameof(SelectedPreset));
        }
        DeletePresetCommand.RaiseCanExecuteChanged();
    }

    private void DeletePreset()
    {
        if (_selectedPreset is null) return;
        Presets.Remove(_selectedPreset);
        _selectedPreset = null;
        OnPropertyChanged(nameof(SelectedPreset));
        DeletePresetCommand.RaiseCanExecuteChanged();
    }

    private void ApplyPreset(SendPreset p)
    {
        SendMode          = p.SendMode;
        TextInput         = p.TextInput;
        HexInput          = p.HexInput;
        FilePath          = p.FilePath;
        Protocol          = p.Protocol;
        TargetId          = p.TargetId;
        RepeatEnabled     = p.RepeatEnabled;
        RepeatInfinite    = p.RepeatInfinite;
        RepeatCount       = p.RepeatCount;
        RepeatIntervalMs  = p.RepeatIntervalMs;
        SplitEnabled      = p.SplitEnabled;
        SplitFixedSize    = p.SplitFixedSize;
        SplitRandom       = p.SplitRandom;
        SplitRandomMaxSize= p.SplitRandomMaxSize;
        InterChunkDelayMs = p.InterChunkDelayMs;
        RandomMinSize     = p.RandomMinSize;
        RandomMaxSize     = p.RandomMaxSize;
        SeqSuffixEnabled  = p.SeqSuffixEnabled;
        SeqSuffixDigits   = p.SeqSuffixDigits;
        LoadTestEnabled     = p.LoadTestEnabled;
        LoadTestInfinite    = p.LoadTestInfinite;
        LoadTestDurationSec = p.LoadTestDurationSec;
        LoadTestTargetMbps  = p.LoadTestTargetMbps;
        NewPresetName     = p.Name;
    }

    private SendPreset CapturePreset(string name) => new()
    {
        Name              = name,
        SendMode          = SendMode,
        TextInput         = TextInput,
        HexInput          = HexInput,
        FilePath          = FilePath,
        Protocol          = Protocol,
        TargetId          = TargetId,
        RepeatEnabled     = RepeatEnabled,
        RepeatInfinite    = RepeatInfinite,
        RepeatCount       = RepeatCount,
        RepeatIntervalMs  = RepeatIntervalMs,
        SplitEnabled      = SplitEnabled,
        SplitFixedSize    = SplitFixedSize,
        SplitRandom       = SplitRandom,
        SplitRandomMaxSize= SplitRandomMaxSize,
        InterChunkDelayMs = InterChunkDelayMs,
        RandomMinSize     = RandomMinSize,
        RandomMaxSize     = RandomMaxSize,
        SeqSuffixEnabled  = SeqSuffixEnabled,
        SeqSuffixDigits   = SeqSuffixDigits,
        LoadTestEnabled     = LoadTestEnabled,
        LoadTestInfinite    = LoadTestInfinite,
        LoadTestDurationSec = LoadTestDurationSec,
        LoadTestTargetMbps  = LoadTestTargetMbps,
    };
}
