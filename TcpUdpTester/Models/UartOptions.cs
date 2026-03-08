using System.IO.Ports;

namespace TcpUdpTester.Models;

public record UartOptions(
    int BaudRate = 9600,
    int DataBits = 8,
    Parity Parity = Parity.None,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None
);
