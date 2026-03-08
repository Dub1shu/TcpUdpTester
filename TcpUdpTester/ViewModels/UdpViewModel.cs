using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class UdpViewModel : ViewModelBase
{
    private readonly INetService _net;
    private string _localPort  = "9090";
    private string _remoteHost = "127.0.0.1";
    private string _remotePort = "9090";
    private string _status     = "Stopped";
    private bool   _isActive;
    private string _recvBufSize = "0";
    private string _sendBufSize = "0";

    public UdpViewModel(INetService net)
    {
        _net = net;
        StartCommand = new RelayCommand(async () => await StartAsync(), () => !_isActive);
        StopCommand  = new RelayCommand(async () => await StopAsync(),  () => _isActive);
    }

    public string LocalPort  { get => _localPort;  set => Set(ref _localPort, value); }
    public string RemoteHost { get => _remoteHost; set => Set(ref _remoteHost, value); }
    public string RemotePort { get => _remotePort; set => Set(ref _remotePort, value); }
    public string Status     { get => _status;     set => Set(ref _status, value); }
    public string RecvBufSize { get => _recvBufSize; set => Set(ref _recvBufSize, value); }
    public string SendBufSize { get => _sendBufSize; set => Set(ref _sendBufSize, value); }
    public bool IsActive
    {
        get => _isActive;
        set
        {
            Set(ref _isActive, value);
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand  { get; }

    private async Task StartAsync()
    {
        if (!int.TryParse(LocalPort, out int local)) return;
        int.TryParse(RemotePort, out int remote);
        int.TryParse(RecvBufSize, out int rcv);
        int.TryParse(SendBufSize, out int snd);
        await _net.UdpStartAsync(local, RemoteHost, remote, new Models.SocketOptions(rcv, snd));
    }

    private async Task StopAsync()
    {
        await _net.UdpStopAsync();
        IsActive = false;
        Status = "Stopped";
    }

    public void UpdateState(StateSnapshot state)
    {
        Status = state.ConnectionState;
        IsActive = state.ConnectionState == "Active";
    }
}
