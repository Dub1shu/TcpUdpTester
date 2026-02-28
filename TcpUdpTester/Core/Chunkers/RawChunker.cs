namespace TcpUdpTester.Core.Chunkers;

/// <summary>入力データをそのまま1チャンクとして返す</summary>
public sealed class RawChunker : IChunker
{
    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data)
    {
        // ReadOnlySpan はイテレータメソッドで使用不可のため即座に配列化して返す
        return [data.ToArray()];
    }

    public IEnumerable<byte[]> Flush() => [];

    public void Reset() { }
}
