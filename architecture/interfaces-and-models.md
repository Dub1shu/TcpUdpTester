# インターフェース・モデル定義

## INetService (Core/INetService.cs)

ViewModelとNetServiceの境界となるインターフェース。

```csharp
public interface INetService
{
    // --- Reactive Streams ---
    IObservable<LogEntry>      LogStream;    // TX/RX 1件ごとに発火
    IObservable<StatsSnapshot> StatsStream;  // 1秒ごとの通信統計
    IObservable<StateSnapshot> StateStream;  // 接続状態の変化

    // --- TCP Client ---
    Task TcpClientConnectAsync(string host, int port, ChunkMode chunkMode);
    Task TcpClientDisconnectAsync();

    // --- TCP Server ---
    Task TcpServerStartAsync(string bindIp, int port, ChunkMode chunkMode);
    Task TcpServerStopAsync();

    // --- UDP ---
    Task UdpStartAsync(int localPort, string remoteHost, int remotePort);
    Task UdpStopAsync();

    // --- Send ---
    Task SendAsync(SendRequest request);

    // --- Query ---
    IReadOnlyList<string> GetActiveSessions();  // 接続中セッションID一覧
}
```

---

## IChunker (Core/Chunkers/IChunker.cs)

受信バイト列を意味のある単位に分割するインターフェース。

```csharp
public interface IChunker
{
    IEnumerable<byte[]> Push(ReadOnlySpan<byte> data);  // バイトを供給 → 0個以上のチャンクを返す
    IEnumerable<byte[]> Flush();                         // バッファを全部吐き出す (接続終了時)
    void Reset();                                         // 内部バッファをクリア
}
```

**注意**: `ReadOnlySpan<byte>` はイテレータメソッド (`yield return`) で使用不可のため、
各実装では `data.ToArray()` してからリストに追加し、完成したチャンクを返す。

---

## Models

### LogEntry (immutable record)

```csharp
public sealed record LogEntry(
    DateTimeOffset Timestamp,   // UTC イベント時刻
    Protocol Protocol,          // TCP or UDP
    Direction Direction,        // TX or RX
    string SessionId,           // セッション識別子 (GUID 8文字)
    string Remote,              // リモートアドレス "host:port"
    int Length,                 // データバイト数
    byte[] Data                 // 実データ
);
```

### StatsSnapshot (immutable record)

```csharp
public sealed record StatsSnapshot(
    long TxBytes,     // 累積送信バイト数
    long RxBytes,     // 累積受信バイト数
    long TxCount,     // 累積送信件数
    long RxCount,     // 累積受信件数
    double TxBps,     // 直近1秒の送信 bps
    double RxBps,     // 直近1秒の受信 bps
    long ErrorCount   // 累積エラー数
);
```

### StateSnapshot (immutable record)

```csharp
public sealed record StateSnapshot(
    string Mode,              // "TCP Client" / "TCP Server" / "UDP"
    string ConnectionState,   // "Connecting" / "Connected" / "Disconnected" / "Error: ..." etc.
    string SessionId,         // セッションID (サーバー側は接続ごとに異なる)
    string RemoteEndpoint     // "host:port" または "Local:port Remote:host:port"
);
```

### SendOptions

```csharp
public sealed class SendOptions
{
    bool RepeatEnabled;       // リピート送信有効
    int  RepeatCount;         // リピート回数 (default 1)
    int  RepeatIntervalMs;    // リピート間隔 ms (default 1000)
    bool SplitEnabled;        // 分割送信有効
    int  SplitFixedSize;      // 固定分割サイズ bytes (default 64)
    bool SplitRandom;         // ランダム分割モード (チャンクサイズをランダム化)
    int  SplitRandomMaxSize;  // ランダム分割の最大サイズ (default 128)
    int  InterChunkDelayMs;   // チャンク間ディレイ ms
}
```

**注意**: `SendMode.Random`・`SeqSuffix`・`LoadTest` は `SendViewModel` の上位ロジックで処理され、
`SendOptions` には含まれない。`NetService.SendAsync()` には分割後の1パケット分データが渡される。

### SendRequest

```csharp
public sealed record SendRequest(
    Protocol Protocol,    // 送信プロトコル
    string TargetId,      // 送信先セッションID (空文字 = デフォルト)
    byte[] Data,          // 送信データ (分割前の全体)
    SendOptions Options   // 送信オプション
);
```

---

## SeqChecker (Core/SeqChecker.cs)

受信データ末尾の ASCII 10進連番を検査し、欠落を検出するユーティリティクラス。

```csharp
public sealed class SeqChecker
{
    // sessionKey → 直前の連番 (ConcurrentDictionary)
    SeqGapResult? Check(string sessionKey, byte[] data, int digitCount);
    void ResetSession(string sessionKey);
    void Reset();  // 全セッションクリア
}

public sealed record SeqGapResult(long LastSeq, long Expected, long Actual, long GapCount);
```

- `sessionKey` = `"{Protocol}:{SessionId}:{Remote}"`
- 初回受信時はベースラインを記録して `null` を返す
- ラップアラウンド境界: `modulo = 10^digitCount` (例: 4桁 → 10000)
- `GapCount = (actual - expected + modulo) % modulo`

---

## Enums

```csharp
public enum Protocol  { TCP, UDP }
public enum Direction { TX, RX, Gap }   // Gap = 連番欠落検出の擬似エントリ
public enum ChunkMode { Raw, Delimiter, FixedLength, TimeSlice, Line }
public enum SendMode  { Text, Hex, File, Random }   // SendViewModel 内で定義
```

---

## AppSettings (Core/AppSettings.cs)

設定永続化用 POCO。`SettingsService` が JSON に直列化する。

| カテゴリ | プロパティ |
|---------|-----------|
| Window | WindowLeft, WindowTop, WindowWidth(1280), WindowHeight(820), WindowMaximized |
| Tab | SelectedTabIndex |
| Logging | LogEnabled, LogFolder |
| TCP Client | TcpClientHost("127.0.0.1"), TcpClientPort("8080"), TcpClientChunkMode |
| TCP Server | TcpServerBindIp(""), TcpServerPort("8080"), TcpServerChunkMode |
| UDP | UdpLocalPort("9090"), UdpRemoteHost("127.0.0.1"), UdpRemotePort("9090") |
| Send | SendMode, SendTextInput, SendHexInput, SendFilePath |
| Repeat | RepeatEnabled, RepeatCount(1), RepeatIntervalMs(1000) |
| Split | SplitEnabled, SplitFixedSize(64), SplitRandom, SplitRandomMaxSize(128), InterChunkDelayMs |
| Random | RandomMinSize(1), RandomMaxSize(256) |
| SeqSuffix | SeqSuffixEnabled, SeqSuffixDigits(4) |
| LoadTest | LoadTestEnabled, LoadTestDurationSec(10), LoadTestTargetMbps(0=無制限) |
| SeqCheck | SeqCheckEnabled, SeqCheckDigits(4) |

設定ファイルパス: `%APPDATA%\NetTestConsole\settings.json`
