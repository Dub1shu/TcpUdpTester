using System.Collections.ObjectModel;
using System.IO.Ports;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class UartViewModel : ViewModelBase
{
    private readonly INetService _net;

    private string  _portName   = "";
    private string  _baudRate   = "115200";
    private int     _dataBits   = 8;
    private string  _parity     = "None";
    private string  _stopBits   = "1";
    private string  _handshake  = "None";
    private ChunkMode _chunkMode = ChunkMode.Raw;
    private string  _status     = "Closed";
    private bool    _isActive;

    public UartViewModel(INetService net)
    {
        _net = net;
        AvailablePorts = [];
        OpenCommand    = new RelayCommand(async () => await OpenAsync(),    () => !_isActive);
        CloseCommand   = new RelayCommand(async () => await CloseAsync(),   () =>  _isActive);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        RefreshPorts();
    }

    // --- Collections ---
    public ObservableCollection<string> AvailablePorts { get; }

    public static IReadOnlyList<string> BaudRates { get; } =
        ["4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600"];

    public static IReadOnlyList<int> DataBitsList { get; } = [7, 8];

    public static IReadOnlyList<string> ParityList { get; } =
        ["None", "Even", "Odd", "Mark", "Space"];

    public static IReadOnlyList<string> StopBitsList { get; } = ["1", "1.5", "2"];

    public static IReadOnlyList<string> HandshakeList { get; } =
        ["None", "RTS/CTS", "XON/XOFF", "RTS/CTS + XON/XOFF"];

    public IReadOnlyList<ChunkMode> ChunkModes { get; } = Enum.GetValues<ChunkMode>().ToList();

    // --- Properties ---
    public string    PortName   { get => _portName;   set => Set(ref _portName, value); }
    public string    BaudRate   { get => _baudRate;   set => Set(ref _baudRate, value); }
    public int       DataBits   { get => _dataBits;   set => Set(ref _dataBits, value); }
    public string    Parity     { get => _parity;     set => Set(ref _parity, value); }
    public string    StopBits   { get => _stopBits;   set => Set(ref _stopBits, value); }
    public string    Handshake  { get => _handshake;  set => Set(ref _handshake, value); }
    public ChunkMode ChunkMode  { get => _chunkMode;  set => Set(ref _chunkMode, value); }
    public string    Status     { get => _status;     set => Set(ref _status, value); }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            Set(ref _isActive, value);
            OpenCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
        }
    }

    // --- Commands ---
    public RelayCommand OpenCommand         { get; }
    public RelayCommand CloseCommand        { get; }
    public RelayCommand RefreshPortsCommand { get; }

    // --- Methods ---
    private void RefreshPorts()
    {
        var selected = PortName;
        AvailablePorts.Clear();
        foreach (var p in _net.GetSerialPorts())
            AvailablePorts.Add(p);

        // 選択を復元 or 先頭を選択
        if (AvailablePorts.Contains(selected))
            PortName = selected;
        else if (AvailablePorts.Count > 0)
            PortName = AvailablePorts[0];
    }

    private async Task OpenAsync()
    {
        if (string.IsNullOrEmpty(PortName)) return;
        int.TryParse(BaudRate, out int baud);
        var opts = new UartOptions(
            BaudRate:  baud > 0 ? baud : 9600,
            DataBits:  DataBits,
            Parity:    ParseParity(Parity),
            StopBits:  ParseStopBits(StopBits),
            Handshake: ParseHandshake(Handshake));
        await _net.UartOpenAsync(PortName, opts, ChunkMode);
    }

    private async Task CloseAsync()
    {
        await _net.UartCloseAsync();
        IsActive = false;
        Status = "Closed";
    }

    public void UpdateState(StateSnapshot state)
    {
        Status   = state.ConnectionState;
        IsActive = state.ConnectionState == "Opened";
    }

    // --- Parse helpers ---
    private static System.IO.Ports.Parity ParseParity(string s) => s switch
    {
        "Even"  => System.IO.Ports.Parity.Even,
        "Odd"   => System.IO.Ports.Parity.Odd,
        "Mark"  => System.IO.Ports.Parity.Mark,
        "Space" => System.IO.Ports.Parity.Space,
        _       => System.IO.Ports.Parity.None,
    };

    private static System.IO.Ports.StopBits ParseStopBits(string s) => s switch
    {
        "1.5" => System.IO.Ports.StopBits.OnePointFive,
        "2"   => System.IO.Ports.StopBits.Two,
        _     => System.IO.Ports.StopBits.One,
    };

    private static System.IO.Ports.Handshake ParseHandshake(string s) => s switch
    {
        "RTS/CTS"              => System.IO.Ports.Handshake.RequestToSend,
        "XON/XOFF"             => System.IO.Ports.Handshake.XOnXOff,
        "RTS/CTS + XON/XOFF"  => System.IO.Ports.Handshake.RequestToSendXOnXOff,
        _                      => System.IO.Ports.Handshake.None,
    };
}
