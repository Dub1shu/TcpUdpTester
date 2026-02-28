namespace TcpUdpTester.Core.Chunkers;

/// <summary>行末(CRLF or LF)でデータを分割するDelimiterChunkerのラッパー</summary>
public sealed class LineChunker : IChunker
{
    public enum LineEnding { CRLF, LF }

    private readonly DelimiterChunker _inner;

    public LineChunker(LineEnding ending = LineEnding.CRLF)
    {
        var delimiter = ending == LineEnding.CRLF
            ? new byte[] { 0x0D, 0x0A }
            : new byte[] { 0x0A };
        _inner = new DelimiterChunker(delimiter);
    }

    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data) => _inner.Push(data);
    public IEnumerable<byte[]> Flush() => _inner.Flush();
    public void Reset() => _inner.Reset();
}
