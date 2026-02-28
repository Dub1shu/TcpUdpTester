namespace TcpUdpTester.Core.Chunkers;

/// <summary>デリミタバイト列でデータを分割する。パケット境界をまたぐ部分一致にも対応</summary>
public sealed class DelimiterChunker : IChunker
{
    private readonly byte[] _delimiter;
    private readonly List<byte> _buffer = [];

    public DelimiterChunker(byte[] delimiter) => _delimiter = delimiter;

    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        return ExtractChunks();
    }

    private IEnumerable<byte[]> ExtractChunks()
    {
        while (true)
        {
            int idx = IndexOf(_buffer, _delimiter);
            if (idx < 0) yield break;
            yield return [.. _buffer.Take(idx)];
            _buffer.RemoveRange(0, idx + _delimiter.Length);
        }
    }

    private static int IndexOf(List<byte> source, byte[] pattern)
    {
        for (int i = 0; i <= source.Count - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j]) { found = false; break; }
            }
            if (found) return i;
        }
        return -1;
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
