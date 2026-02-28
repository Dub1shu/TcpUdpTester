namespace TcpUdpTester.Core.Chunkers;

public interface IChunker
{
    IEnumerable<byte[]> Push(ReadOnlySpan<byte> data);
    IEnumerable<byte[]> Flush();
    void Reset();
}
