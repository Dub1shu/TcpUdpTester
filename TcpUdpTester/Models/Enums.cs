namespace TcpUdpTester.Models;

public enum Protocol { TCP, UDP, UART }

public enum Direction { TX, RX, Gap, Event }

public enum ChunkMode { Raw, Delimiter, FixedLength, TimeSlice, Line }

public enum SendMode { Text, Hex, File, Random }
