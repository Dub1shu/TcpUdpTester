namespace TcpUdpTester.Core.Chunkers;

/// <summary>一定時間ごとに受信バイトをまとめて1チャンクとして出力する (典型値: 50-100ms)</summary>
public sealed class TimeSliceChunker : IChunker
{
    private readonly int _sliceMs;
    private readonly List<byte> _buffer = [];
    private DateTimeOffset _sliceStart = DateTimeOffset.UtcNow;

    public TimeSliceChunker(int sliceMs = 50) => _sliceMs = sliceMs;

    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var now = DateTimeOffset.UtcNow;
        if ((now - _sliceStart).TotalMilliseconds >= _sliceMs && _buffer.Count > 0)
        {
            var result = _buffer.ToArray();
            _buffer.Clear();
            _sliceStart = now;
            _buffer.AddRange(bytes);
            return [result];
        }
        _buffer.AddRange(bytes);
        return [];
    }

    public IEnumerable<byte[]> Flush()
    {
        if (_buffer.Count > 0)
        {
            var result = _buffer.ToArray();
            _buffer.Clear();
            yield return result;
        }
    }

    public void Reset()
    {
        _buffer.Clear();
        _sliceStart = DateTimeOffset.UtcNow;
    }
}
