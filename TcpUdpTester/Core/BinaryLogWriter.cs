using System.Buffers.Binary;
using System.IO;
using System.Threading.Channels;
using TcpUdpTester.Models;

namespace TcpUdpTester.Core;

/// <summary>
/// ログエントリを .ntlg バイナリ形式でディスクに書き込む。
/// Channel を使い非同期・シーケンシャルに書き込む。
/// </summary>
public sealed class BinaryLogWriter : IDisposable
{
    private readonly string _folder;
    private FileStream? _fileStream;
    private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    // ファイルヘッダ定数
    private static ReadOnlySpan<byte> Magic => "NTLG"u8;
    private const byte FileVersion = 1;
    private const byte FileHeaderSize = 32;

    public bool IsEnabled { get; set; }

    public BinaryLogWriter(string folder)
    {
        _folder = folder;
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void Enqueue(LogEntry entry)
    {
        if (IsEnabled) _channel.Writer.TryWrite(entry);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_cts.Token))
                await WriteEntryAsync(entry);
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteEntryAsync(LogEntry entry)
    {
        try
        {
            if (_fileStream == null)
            {
                Directory.CreateDirectory(_folder);
                var path = Path.Combine(_folder,
                    $"ntlog_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.ntlg");
                _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, true);
                await WriteFileHeaderAsync();
            }

            var sessionBytes = System.Text.Encoding.UTF8.GetBytes(entry.SessionId);
            var remoteBytes  = System.Text.Encoding.UTF8.GetBytes(entry.Remote);
            var data         = entry.Data;

            // Record header layout (fixed 28 bytes):
            // RecordSize(4) | TimestampUnixUs(8) | Protocol(1) | Direction(1)
            // SessionIdLen(2) | RemoteLen(2) | DataLen(4) | ChunkFlags(2) | CRC32(4)
            const int fixedHeaderLen = 4 + 8 + 1 + 1 + 2 + 2 + 4 + 2 + 4;
            int totalRecord = fixedHeaderLen + sessionBytes.Length + remoteBytes.Length + data.Length;

            var hdr = new byte[fixedHeaderLen];
            int off = 0;
            BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(off), totalRecord);    off += 4;
            BinaryPrimitives.WriteInt64LittleEndian(hdr.AsSpan(off),
                entry.Timestamp.ToUnixTimeMilliseconds() * 1000L);                    off += 8;
            hdr[off++] = (byte)entry.Protocol;
            hdr[off++] = (byte)entry.Direction;
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(off), (ushort)sessionBytes.Length); off += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(off), (ushort)remoteBytes.Length);  off += 2;
            BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(off), data.Length);                  off += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(off), 0);                           off += 2; // ChunkFlags
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(off),
                ComputeCrc32([.. sessionBytes, .. remoteBytes, .. data]));

            await _fileStream.WriteAsync(hdr);
            await _fileStream.WriteAsync(sessionBytes);
            await _fileStream.WriteAsync(remoteBytes);
            await _fileStream.WriteAsync(data);
        }
        catch
        {
            // ログ書き込みエラーはサイレントに無視
        }
    }

    private async Task WriteFileHeaderAsync()
    {
        var hdr = new byte[FileHeaderSize]; // 32 bytes, zero-initialized
        Magic.CopyTo(hdr);
        hdr[4] = FileVersion;
        hdr[5] = FileHeaderSize;
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(6), 0); // Flags
        BinaryPrimitives.WriteInt64LittleEndian(hdr.AsSpan(8),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L); // CreatedUnixUs
        // bytes 16-31: Reserved (already zero)
        await _fileStream!.WriteAsync(hdr);
    }

    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFF_FFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320 : crc >> 1;
        }
        return crc ^ 0xFFFF_FFFF;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _fileStream?.Dispose();
        _cts.Dispose();
    }
}
