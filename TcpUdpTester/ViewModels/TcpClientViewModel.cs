using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class TcpClientViewModel : ViewModelBase
{
    private readonly INetService _net;
    private string _host = "127.0.0.1";
    private string _port = "8080";
    private string _status = "Disconnected";
    private bool _isConnected;
    private ChunkMode _chunkMode = ChunkMode.Raw;
    private string _recvBufSize = "0";
    private string _sendBufSize = "0";

    public TcpClientViewModel(INetService net)
    {
        _net = net;
        ConnectCommand    = new RelayCommand(async () => await ConnectAsync(),    () => !_isConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => _isConnected);
    }

    public string Host { get => _host; set => Set(ref _host, value); }
    public string Port { get => _port; set => Set(ref _port, value); }
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            Set(ref _isConnected, value);
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
        }
    }
    public ChunkMode ChunkMode { get => _chunkMode; set => Set(ref _chunkMode, value); }
    public IReadOnlyList<ChunkMode> ChunkModes { get; } = Enum.GetValues<ChunkMode>().ToList();
    public string RecvBufSize { get => _recvBufSize; set => Set(ref _recvBufSize, value); }
    public string SendBufSize { get => _sendBufSize; set => Set(ref _sendBufSize, value); }

    public RelayCommand ConnectCommand    { get; }
    public RelayCommand DisconnectCommand { get; }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(Port, out int port)) return;
        int.TryParse(RecvBufSize, out int rcv);
        int.TryParse(SendBufSize, out int snd);
        Status = "Connecting...";
        await _net.TcpClientConnectAsync(Host, port, ChunkMode, new Models.SocketOptions(rcv, snd));
    }

    private async Task DisconnectAsync()
    {
        await _net.TcpClientDisconnectAsync();
        IsConnected = false;
        Status = "Disconnected";
    }

    public void UpdateState(StateSnapshot state)
    {
        Status = state.ConnectionState;
        IsConnected = state.ConnectionState == "Connected";
    }
}
