using System.Collections.ObjectModel;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class TcpServerViewModel : ViewModelBase
{
    private readonly INetService _net;
    private string _bindIp = "";
    private string _port = "8080";
    private string _status = "Stopped";
    private bool _isListening;
    private ChunkMode _chunkMode = ChunkMode.Raw;
    private string? _selectedSession;
    private string _recvBufSize = "0";
    private string _sendBufSize = "0";

    public TcpServerViewModel(INetService net)
    {
        _net = net;
        StartCommand = new RelayCommand(async () => await StartAsync(), () => !_isListening);
        StopCommand  = new RelayCommand(async () => await StopAsync(),  () => _isListening);
    }

    public string BindIp { get => _bindIp; set => Set(ref _bindIp, value); }
    public string Port   { get => _port;   set => Set(ref _port, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsListening
    {
        get => _isListening;
        set
        {
            Set(ref _isListening, value);
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }
    public ChunkMode ChunkMode { get => _chunkMode; set => Set(ref _chunkMode, value); }
    public IReadOnlyList<ChunkMode> ChunkModes { get; } = Enum.GetValues<ChunkMode>().ToList();
    public string RecvBufSize { get => _recvBufSize; set => Set(ref _recvBufSize, value); }
    public string SendBufSize { get => _sendBufSize; set => Set(ref _sendBufSize, value); }

    public ObservableCollection<string> Sessions { get; } = [];
    public string? SelectedSession { get => _selectedSession; set => Set(ref _selectedSession, value); }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand  { get; }

    private async Task StartAsync()
    {
        if (!int.TryParse(Port, out int port)) return;
        int.TryParse(RecvBufSize, out int rcv);
        int.TryParse(SendBufSize, out int snd);
        await _net.TcpServerStartAsync(BindIp, port, ChunkMode, new Models.SocketOptions(rcv, snd));
    }

    private async Task StopAsync()
    {
        await _net.TcpServerStopAsync();
        IsListening = false;
        Status = "Stopped";
        Sessions.Clear();
    }

    public void UpdateState(StateSnapshot state)
    {
        Status = state.ConnectionState;
        if (state.ConnectionState == "Listening")
            IsListening = true;

        // セッションリストを更新
        var active = _net.GetActiveSessions();
        Sessions.Clear();
        foreach (var s in active) Sessions.Add(s);

        if (Sessions.Count > 0 && string.IsNullOrEmpty(SelectedSession))
            SelectedSession = Sessions[0];
    }
}
