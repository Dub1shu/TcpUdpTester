using System.Text;
using TcpUdpTester.Models;

namespace TcpUdpTester.ViewModels;

public sealed class TrafficEntryViewModel
{
    private readonly LogEntry _entry;

    public TrafficEntryViewModel(LogEntry entry) => _entry = entry;

    public string Time      => _entry.Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");
    public string Direction => _entry.Direction switch
    {
        Models.Direction.Gap   => "GAP",
        Models.Direction.Event => "EVT",
        _                      => _entry.Direction.ToString()
    };
    public string Protocol  => _entry.Protocol.ToString();
    public string Session   => _entry.SessionId;
    public string Remote    => _entry.Remote;
    public int    Length    => _entry.Length;

    /// <summary>16進+ASCII の複数行ダンプ文字列</summary>
    public string HexDump => _entry.Direction is Models.Direction.Gap or Models.Direction.Event
        ? Encoding.UTF8.GetString(_entry.Data)
        : BuildHexDump(_entry.Data);

    /// <summary>1行ASCII表示 (表示不可文字は '.'。GAP/EVTエントリはUTF-8デコード)</summary>
    public string AsciiView => _entry.Direction is Models.Direction.Gap or Models.Direction.Event
        ? Encoding.UTF8.GetString(_entry.Data)
        : BuildAscii(_entry.Data);

    public Direction DirectionEnum => _entry.Direction;

    private static string BuildHexDump(byte[] data)
    {
        if (data.Length == 0) return "(empty)";
        const int bytesPerLine = 16;
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += bytesPerLine)
        {
            sb.Append($"{i:X4}  ");
            int end = Math.Min(i + bytesPerLine, data.Length);
            for (int j = i; j < end; j++) sb.Append($"{data[j]:X2} ");
            for (int j = end; j < i + bytesPerLine; j++) sb.Append("   ");
            sb.Append("  ");
            for (int j = i; j < end; j++)
                sb.Append(data[j] is >= 0x20 and < 0x7F ? (char)data[j] : '.');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildAscii(byte[] data)
    {
        if (data.Length == 0) return "";
        const int maxLen = 200;
        var chars = new char[Math.Min(data.Length, maxLen)];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = data[i] is >= 0x20 and < 0x7F ? (char)data[i] : '.';
        return data.Length > maxLen ? new string(chars) + "…" : new string(chars);
    }
}
