namespace TcpUdpTester.Core.Chunkers;

/// <summary>Nバイト単位でデータを分割する</summary>
public sealed class FixedLengthChunker : IChunker
{
    private readonly int _size;
    private readonly List<byte> _buffer = [];

    public FixedLengthChunker(int size) => _size = size;

    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        return Extract();
    }

    private IEnumerable<byte[]> Extract()
    {
        while (_buffer.Count >= _size)
        {
            yield return [.. _buffer.Take(_size)];
            _buffer.RemoveRange(0, _size);
        }
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

    public void Reset() => _buffer.Clear();
}
