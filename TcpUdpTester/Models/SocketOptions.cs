namespace TcpUdpTester.Models;

/// <summary>ソケットバッファサイズオプション。0 は OS 既定。</summary>
public record SocketOptions(int RecvBufSize = 0, int SendBufSize = 0);
