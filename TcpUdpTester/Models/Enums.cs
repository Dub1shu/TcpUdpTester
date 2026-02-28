namespace TcpUdpTester.Models;

public enum Protocol { TCP, UDP }

public enum Direction { TX, RX }

public enum ChunkMode { Raw, Delimiter, FixedLength, TimeSlice, Line }
