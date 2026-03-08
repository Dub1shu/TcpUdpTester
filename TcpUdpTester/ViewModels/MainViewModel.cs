using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly INetService _net;
    private readonly BinaryLogWriter _logWriter;
    private readonly IDisposable _logSub, _statsSub, _stateSub;
    private readonly SeqChecker _seqChecker = new();
    private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
    private readonly DispatcherTimer _flushTimer;

    private string _stateText  = "Ready";
    private long   _txBytes, _rxBytes, _txCount, _rxCount, _errorCount;
    private double _txBps, _rxBps;
    private bool   _logEnabled;
    private string _logFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "NetTestConsole", "Logs");
    private string _filterText = "";
    private bool   _filterTx    = true;
    private bool   _filterRx    = true;
    private bool   _filterGap   = true;
    private bool   _filterEvent = true;
    private TrafficEntryViewModel? _selectedEntry;
    private int    _selectedTabIndex;
    private bool   _seqCheckEnabled;
    private int    _seqCheckDigits = 4;
    private long   _seqGapCount;
    private bool   _callbackEnabled;

    public MainViewModel()
    {
        _net = new NetService();
        _logWriter = new BinaryLogWriter(_logFolder);

        TcpClientVm = new TcpClientViewModel(_net);
        TcpServerVm = new TcpServerViewModel(_net);
        UdpVm       = new UdpViewModel(_net);
        SendVm      = new SendViewModel(_net);

        _logSub   = _net.LogStream.Subscribe(OnLogEntry);
        _statsSub = _net.StatsStream.Subscribe(OnStats);
        _stateSub = _net.StateStream.Subscribe(OnState);

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _flushTimer.Tick += FlushLogBuffer;
        _flushTimer.Start();

        // TCP Server のセッション選択を SendVm.TargetId に自動連携
        TcpServerVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TcpServerViewModel.SelectedSession) && SelectedTabIndex == 1)
                SendVm.TargetId = TcpServerVm.SelectedSession ?? "";
        };

        ClearCommand  = new RelayCommand(ClearTraffic);
        ExportCommand = new RelayCommand(ExportLogs);
    }

    // --- Child ViewModels ---
    public TcpClientViewModel TcpClientVm { get; }
    public TcpServerViewModel TcpServerVm { get; }
    public UdpViewModel       UdpVm       { get; }
    public SendViewModel      SendVm      { get; }

    // --- Traffic Log ---
    public ObservableCollection<TrafficEntryViewModel> TrafficLog   { get; } = [];
    public ObservableCollection<TrafficEntryViewModel> FilteredLog  { get; } = [];
    public TrafficEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => Set(ref _selectedEntry, value);
    }

    // --- State / Stats ---
    public string StateText  { get => _stateText;   set => Set(ref _stateText, value); }
    public long   TxBytes    { get => _txBytes;      set => Set(ref _txBytes, value); }
    public long   RxBytes    { get => _rxBytes;      set => Set(ref _rxBytes, value); }
    public long   TxCount    { get => _txCount;      set => Set(ref _txCount, value); }
    public long   RxCount    { get => _rxCount;      set => Set(ref _rxCount, value); }
    public double TxBps      { get => _txBps;        set => Set(ref _txBps, value); }
    public double RxBps      { get => _rxBps;        set => Set(ref _rxBps, value); }
    public long   ErrorCount { get => _errorCount;   set => Set(ref _errorCount, value); }

    // --- Logging ---
    public bool LogEnabled
    {
        get => _logEnabled;
        set { Set(ref _logEnabled, value); _logWriter.IsEnabled = value; }
    }
    public string LogFolder
    {
        get => _logFolder;
        set => Set(ref _logFolder, value);
    }

    // --- Filter ---
    public string FilterText
    {
        get => _filterText;
        set { Set(ref _filterText, value); ApplyFilter(); }
    }
    public bool FilterTx
    {
        get => _filterTx;
        set { Set(ref _filterTx, value); ApplyFilter(); }
    }
    public bool FilterRx
    {
        get => _filterRx;
        set { Set(ref _filterRx, value); ApplyFilter(); }
    }
    public bool FilterGap
    {
        get => _filterGap;
        set { Set(ref _filterGap, value); ApplyFilter(); }
    }
    public bool FilterEvent
    {
        get => _filterEvent;
        set { Set(ref _filterEvent, value); ApplyFilter(); }
    }

    // --- Tab ---
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            Set(ref _selectedTabIndex, value);
            // タブに応じてSendVMのプロトコルを自動切替
            SendVm.Protocol = value == 2 ? Protocol.UDP : Protocol.TCP;
            // TCP Server タブ選択時はセッション選択を TargetId に反映、それ以外はクリア
            SendVm.TargetId = value == 1 ? (TcpServerVm.SelectedSession ?? "") : "";
        }
    }

    // --- Seq Check ---
    public bool IsSeqCheckEnabled
    {
        get => _seqCheckEnabled;
        set { Set(ref _seqCheckEnabled, value); if (!value) _seqChecker.Reset(); }
    }
    public int SeqCheckDigits
    {
        get => _seqCheckDigits;
        set => Set(ref _seqCheckDigits, Math.Clamp(value, 1, 10));
    }
    public long SeqGapCount
    {
        get => _seqGapCount;
        set => Set(ref _seqGapCount, value);
    }

    // --- Callback ---
    public bool IsCallbackEnabled
    {
        get => _callbackEnabled;
        set => Set(ref _callbackEnabled, value);
    }

    // --- Commands ---
    public RelayCommand ClearCommand  { get; }
    public RelayCommand ExportCommand { get; }

    // ================================================================
    // Handlers
    // ================================================================
    private void OnLogEntry(LogEntry entry)
    {
        _logWriter.Enqueue(entry);

        if (IsCallbackEnabled && entry.Direction == Direction.RX)
        {
            var req = new SendRequest(entry.Protocol, entry.SessionId, entry.Data, new SendOptions());
            _ = _net.SendAsync(req);
        }

        _logBuffer.Enqueue(entry);
    }

    // DispatcherTimer (Background priority, 50ms) で一括フラッシュ
    private void FlushLogBuffer(object? sender, EventArgs e)
    {
        const int maxPerFlush = 500;
        int count = 0;
        while (count < maxPerFlush && _logBuffer.TryDequeue(out var entry))
        {
            AddToTraffic(new TrafficEntryViewModel(entry));

            // 受信データ連番検査
            if (IsSeqCheckEnabled && entry.Direction == Direction.RX)
            {
                var sessionKey = $"{entry.Protocol}:{entry.SessionId}:{entry.Remote}";
                var result = _seqChecker.Check(sessionKey, entry.Data, SeqCheckDigits);
                if (result != null)
                {
                    SeqGapCount++;
                    var fmt = $"D{SeqCheckDigits}";
                    var msg = $"[連番欠落] 期待={result.Expected.ToString(fmt)}" +
                              $" 実際={result.Actual.ToString(fmt)}" +
                              $" 欠落数={result.GapCount}";
                    var gapEntry = new LogEntry(
                        DateTimeOffset.Now, entry.Protocol, Direction.Gap,
                        entry.SessionId, entry.Remote,
                        msg.Length, Encoding.UTF8.GetBytes(msg));
                    AddToTraffic(new TrafficEntryViewModel(gapEntry));
                }
            }
            count++;
        }
    }

    private void AddToTraffic(TrafficEntryViewModel vm)
    {
        TrafficLog.Add(vm);
        if (TrafficLog.Count > 10_000) TrafficLog.RemoveAt(0);

        if (Matches(vm))
        {
            FilteredLog.Add(vm);
            if (FilteredLog.Count > 10_000) FilteredLog.RemoveAt(0);
        }
    }

    private void OnStats(StatsSnapshot s)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TxBytes = s.TxBytes; RxBytes = s.RxBytes;
            TxCount = s.TxCount; RxCount = s.RxCount;
            TxBps   = s.TxBps;   RxBps   = s.RxBps;
            ErrorCount = s.ErrorCount;
        });
    }

    private void OnState(StateSnapshot state)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            StateText = $"[{state.Mode}] {state.ConnectionState}";
            switch (state.Mode)
            {
                case "TCP Client": TcpClientVm.UpdateState(state); break;
                case "TCP Server": TcpServerVm.UpdateState(state); break;
                case "UDP":        UdpVm.UpdateState(state);       break;
            }
        });
    }

    private bool Matches(TrafficEntryViewModel vm)
    {
        if (!FilterGap   && vm.DirectionEnum == Direction.Gap)   return false;
        if (!FilterTx    && vm.DirectionEnum == Direction.TX)    return false;
        if (!FilterRx    && vm.DirectionEnum == Direction.RX)    return false;
        if (!FilterEvent && vm.DirectionEnum == Direction.Event) return false;
        // Gap/Event エントリはテキストフィルタを適用しない
        if (vm.DirectionEnum != Direction.Gap && vm.DirectionEnum != Direction.Event &&
            !string.IsNullOrEmpty(FilterText) &&
            !vm.Remote.Contains(FilterText, StringComparison.OrdinalIgnoreCase) &&
            !vm.Session.Contains(FilterText, StringComparison.OrdinalIgnoreCase) &&
            !vm.AsciiView.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void ApplyFilter()
    {
        FilteredLog.Clear();
        foreach (var vm in TrafficLog)
            if (Matches(vm)) FilteredLog.Add(vm);
    }

    private void ClearTraffic()
    {
        TrafficLog.Clear();
        FilteredLog.Clear();
        SelectedEntry = null;
        SeqGapCount = 0;
        _seqChecker.Reset();
    }

    private void ExportLogs()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files|*.csv|Text Files|*.txt|All Files|*.*",
            DefaultExt = "csv",
            FileName = $"traffic_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        using var writer = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
        writer.WriteLine("Time,Direction,Protocol,Session,Remote,Length,ASCII");
        foreach (var e in FilteredLog)
            writer.WriteLine($"\"{e.Time}\",{e.Direction},{e.Protocol},{e.Session},\"{e.Remote}\",{e.Length},\"{e.AsciiView.Replace("\"", "\"\"")}\"");
    }

    // ================================================================
    // Settings persistence
    // ================================================================
    public void ApplySettings(Core.AppSettings s)
    {
        SelectedTabIndex = s.SelectedTabIndex;
        LogEnabled       = s.LogEnabled;
        if (!string.IsNullOrEmpty(s.LogFolder)) LogFolder = s.LogFolder;

        TcpClientVm.Host        = s.TcpClientHost;
        TcpClientVm.Port        = s.TcpClientPort;
        TcpClientVm.ChunkMode   = s.TcpClientChunkMode;
        TcpClientVm.RecvBufSize = s.TcpClientRecvBuf;
        TcpClientVm.SendBufSize = s.TcpClientSendBuf;

        TcpServerVm.BindIp      = s.TcpServerBindIp;
        TcpServerVm.Port        = s.TcpServerPort;
        TcpServerVm.ChunkMode   = s.TcpServerChunkMode;
        TcpServerVm.RecvBufSize = s.TcpServerRecvBuf;
        TcpServerVm.SendBufSize = s.TcpServerSendBuf;

        UdpVm.LocalPort   = s.UdpLocalPort;
        UdpVm.RemoteHost  = s.UdpRemoteHost;
        UdpVm.RemotePort  = s.UdpRemotePort;
        UdpVm.RecvBufSize = s.UdpRecvBuf;
        UdpVm.SendBufSize = s.UdpSendBuf;

        SendVm.SendMode          = s.SendMode;
        SendVm.TextInput         = s.SendTextInput;
        SendVm.HexInput          = s.SendHexInput;
        SendVm.FilePath          = s.SendFilePath;
        SendVm.RepeatEnabled     = s.RepeatEnabled;
        SendVm.RepeatCount       = s.RepeatCount;
        SendVm.RepeatIntervalMs  = s.RepeatIntervalMs;
        SendVm.SplitEnabled      = s.SplitEnabled;
        SendVm.SplitFixedSize    = s.SplitFixedSize;
        SendVm.SplitRandom       = s.SplitRandom;
        SendVm.SplitRandomMaxSize= s.SplitRandomMaxSize;
        SendVm.InterChunkDelayMs = s.InterChunkDelayMs;
        SendVm.RandomMinSize      = s.RandomMinSize;
        SendVm.RandomMaxSize      = s.RandomMaxSize;
        SendVm.SeqSuffixEnabled   = s.SeqSuffixEnabled;
        SendVm.SeqSuffixDigits    = s.SeqSuffixDigits;
        SendVm.LoadTestEnabled    = s.LoadTestEnabled;
        SendVm.LoadTestDurationSec= s.LoadTestDurationSec;
        SendVm.LoadTestTargetMbps = s.LoadTestTargetMbps;

        IsSeqCheckEnabled  = s.SeqCheckEnabled;
        SeqCheckDigits     = s.SeqCheckDigits;
        IsCallbackEnabled  = s.CallbackEnabled;

        SendVm.Presets.Clear();
        foreach (var p in s.SendPresets)
            SendVm.Presets.Add(p);
    }

    public Core.AppSettings CaptureSettings() => new()
    {
        SelectedTabIndex   = SelectedTabIndex,
        LogEnabled         = LogEnabled,
        LogFolder          = LogFolder,

        TcpClientHost      = TcpClientVm.Host,
        TcpClientPort      = TcpClientVm.Port,
        TcpClientChunkMode = TcpClientVm.ChunkMode,
        TcpClientRecvBuf   = TcpClientVm.RecvBufSize,
        TcpClientSendBuf   = TcpClientVm.SendBufSize,

        TcpServerBindIp    = TcpServerVm.BindIp,
        TcpServerPort      = TcpServerVm.Port,
        TcpServerChunkMode = TcpServerVm.ChunkMode,
        TcpServerRecvBuf   = TcpServerVm.RecvBufSize,
        TcpServerSendBuf   = TcpServerVm.SendBufSize,

        UdpLocalPort  = UdpVm.LocalPort,
        UdpRemoteHost = UdpVm.RemoteHost,
        UdpRemotePort = UdpVm.RemotePort,
        UdpRecvBuf    = UdpVm.RecvBufSize,
        UdpSendBuf    = UdpVm.SendBufSize,

        SendMode           = SendVm.SendMode,
        SendTextInput      = SendVm.TextInput,
        SendHexInput       = SendVm.HexInput,
        SendFilePath       = SendVm.FilePath,
        RepeatEnabled      = SendVm.RepeatEnabled,
        RepeatCount        = SendVm.RepeatCount,
        RepeatIntervalMs   = SendVm.RepeatIntervalMs,
        SplitEnabled       = SendVm.SplitEnabled,
        SplitFixedSize     = SendVm.SplitFixedSize,
        SplitRandom        = SendVm.SplitRandom,
        SplitRandomMaxSize = SendVm.SplitRandomMaxSize,
        InterChunkDelayMs  = SendVm.InterChunkDelayMs,
        RandomMinSize       = SendVm.RandomMinSize,
        RandomMaxSize       = SendVm.RandomMaxSize,
        SeqSuffixEnabled    = SendVm.SeqSuffixEnabled,
        SeqSuffixDigits     = SendVm.SeqSuffixDigits,
        LoadTestEnabled     = SendVm.LoadTestEnabled,
        LoadTestDurationSec = SendVm.LoadTestDurationSec,
        LoadTestTargetMbps  = SendVm.LoadTestTargetMbps,
        SeqCheckEnabled     = IsSeqCheckEnabled,
        SeqCheckDigits      = SeqCheckDigits,
        CallbackEnabled     = IsCallbackEnabled,
        SendPresets         = [.. SendVm.Presets],
    };

    public void Dispose()
    {
        _flushTimer.Stop();
        _logSub.Dispose();
        _statsSub.Dispose();
        _stateSub.Dispose();
        (_net as IDisposable)?.Dispose();
        _logWriter.Dispose();
    }
}
