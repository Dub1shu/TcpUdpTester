using System.Globalization;
using System.IO;
using TcpUdpTester.Core;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public enum SendMode { Text, Hex, File }

public sealed class SendViewModel : ViewModelBase
{
    private readonly INetService _net;
    private SendMode _sendMode  = SendMode.Text;
    private string   _textInput = "";
    private string   _hexInput  = "";
    private string   _filePath  = "";
    private bool     _repeatEnabled;
    private int      _repeatCount      = 1;
    private int      _repeatIntervalMs = 1000;
    private bool     _splitEnabled;
    private int      _splitFixedSize   = 64;
    private bool     _splitRandom;
    private int      _splitRandomMaxSize = 128;
    private int      _interChunkDelayMs;
    private string   _sendStatus = "";
    private Protocol _protocol   = Protocol.TCP;
    private string   _targetId   = "";

    public SendViewModel(INetService net)
    {
        _net = net;
        SendCommand       = new RelayCommand(async () => await SendAsync());
        BrowseFileCommand = new RelayCommand(BrowseFile);
    }

    public SendMode SendMode { get => _sendMode; set => Set(ref _sendMode, value); }
    public IReadOnlyList<SendMode> SendModes { get; } = Enum.GetValues<SendMode>().ToList();

    public string TextInput { get => _textInput; set => Set(ref _textInput, value); }
    public string HexInput  { get => _hexInput;  set => Set(ref _hexInput, value); }
    public string FilePath  { get => _filePath;  set => Set(ref _filePath, value); }

    public bool RepeatEnabled    { get => _repeatEnabled;    set => Set(ref _repeatEnabled, value); }
    public int  RepeatCount      { get => _repeatCount;      set => Set(ref _repeatCount, value); }
    public int  RepeatIntervalMs { get => _repeatIntervalMs; set => Set(ref _repeatIntervalMs, value); }

    public bool SplitEnabled      { get => _splitEnabled;      set => Set(ref _splitEnabled, value); }
    public int  SplitFixedSize    { get => _splitFixedSize;    set => Set(ref _splitFixedSize, value); }
    public bool SplitRandom       { get => _splitRandom;       set => Set(ref _splitRandom, value); }
    public int  SplitRandomMaxSize{ get => _splitRandomMaxSize;set => Set(ref _splitRandomMaxSize, value); }
    public int  InterChunkDelayMs { get => _interChunkDelayMs; set => Set(ref _interChunkDelayMs, value); }

    public string   SendStatus { get => _sendStatus; set => Set(ref _sendStatus, value); }
    public Protocol Protocol   { get => _protocol;   set => Set(ref _protocol, value); }
    public string   TargetId   { get => _targetId;   set => Set(ref _targetId, value); }

    public IReadOnlyList<Protocol> Protocols { get; } = Enum.GetValues<Protocol>().ToList();

    public RelayCommand SendCommand       { get; }
    public RelayCommand BrowseFileCommand { get; }

    private async Task SendAsync()
    {
        try
        {
            var data = BuildData();
            if (data is null || data.Length == 0)
            {
                SendStatus = "送信データがありません";
                return;
            }

            var opts = new SendOptions
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

            var request = new SendRequest(Protocol, TargetId, data, opts);
            SendStatus = "送信中...";
            await _net.SendAsync(request);
            SendStatus = $"送信完了  {data.Length} bytes";
        }
        catch (Exception ex)
        {
            SendStatus = $"Error: {ex.Message}";
        }
    }

    private byte[]? BuildData() => SendMode switch
    {
        SendMode.Text => string.IsNullOrEmpty(TextInput)
            ? null : System.Text.Encoding.UTF8.GetBytes(TextInput),
        SendMode.Hex  => ParseHex(HexInput),
        SendMode.File => File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null,
        _             => null
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
