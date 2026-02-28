using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly INetService _net;
    private readonly BinaryLogWriter _logWriter;
    private readonly IDisposable _logSub, _statsSub, _stateSub;

    private string _stateText  = "Ready";
    private long   _txBytes, _rxBytes, _txCount, _rxCount, _errorCount;
    private double _txBps, _rxBps;
    private bool   _logEnabled;
    private string _logFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "NetTestConsole", "Logs");
    private string _filterText = "";
    private bool   _filterTx   = true;
    private bool   _filterRx   = true;
    private TrafficEntryViewModel? _selectedEntry;
    private int    _selectedTabIndex;

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

    // --- Tab ---
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            Set(ref _selectedTabIndex, value);
            // タブに応じてSendVMのプロトコルを自動切替
            SendVm.Protocol = value == 2 ? Protocol.UDP : Protocol.TCP;
        }
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
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var vm = new TrafficEntryViewModel(entry);
            TrafficLog.Add(vm);
            if (TrafficLog.Count > 10_000) TrafficLog.RemoveAt(0);

            if (Matches(vm))
            {
                FilteredLog.Add(vm);
                if (FilteredLog.Count > 10_000) FilteredLog.RemoveAt(0);
            }
        });
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
        if (!FilterTx && vm.DirectionEnum == Direction.TX) return false;
        if (!FilterRx && vm.DirectionEnum == Direction.RX) return false;
        if (!string.IsNullOrEmpty(FilterText) &&
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

    public void Dispose()
    {
        _logSub.Dispose();
        _statsSub.Dispose();
        _stateSub.Dispose();
        (_net as IDisposable)?.Dispose();
        _logWriter.Dispose();
    }
}
