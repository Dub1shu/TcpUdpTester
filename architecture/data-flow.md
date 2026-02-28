# データフロー

## 受信 (RX) パイプライン

```
ネットワーク
  │  bytes
  ▼
TcpReceiveLoopAsync / UdpReceiveLoopAsync   (バックグラウンドTask)
  │  ReadOnlySpan<byte>
  ▼
IChunker.Push()                             (チャンク分割)
  │  byte[][] (0個以上のチャンク)
  ▼
EmitRx()
  │  LogEntry (immutable record)
  ▼
Subject<LogEntry>._logSubject.OnNext()      (Rx Subject)
  │
  ├──► BinaryLogWriter.Enqueue()            (ディスクへ非同期書き込み)
  │      └─ Channel<LogEntry>.Writer.TryWrite()
  │           └─ WriteLoopAsync() → WriteEntryAsync() → FileStream
  │
  └──► MainViewModel.OnLogEntry()           (UIスレッドへ Dispatch)
         └─ Dispatcher.InvokeAsync()
              └─ TrafficLog.Add(vm)
                 FilteredLog.Add(vm) ← Matches() でフィルタリング
```

## 送信 (TX) パイプライン

```
SendViewModel (UIスレッド)
  │  ユーザーが [Send] ボタン押下
  ▼
LoadTestEnabled?
  ├─ YES → RunLoadTestAsync()
  │         │  時間ベースループ (LoadTestDurationSec 秒)
  │         │  LoadTestTargetMbps > 0 の場合レート制限あり
  │         │  200ms ごとに SendStatus に速度表示
  │         └─→ BuildIterData() per iteration
  │
  └─ NO  → RunNormalSendAsync()
            │  RepeatEnabled の場合 RepeatCount 回ループ
            └─→ BuildIterData() per iteration

BuildIterData()
  │  SendMode == Random → GenerateRandomData()
  │                        (Random.Shared, [RandomMinSize, RandomMaxSize] bytes)
  │  SendMode == Text/Hex/File → BuildData() で変換済みバイト列を流用
  │  SeqSuffixEnabled == true →
  │      Interlocked.Increment(_seqCounter) で連番を取得
  │      末尾に SeqSuffixDigits 桁ゼロパディング ASCII 10進数を付与
  ▼
INetService.SendAsync(SendRequest)
  ▼
NetService.SendAsync()
  │  SplitData() で byte[] を分割 (SplitEnabled の場合)
  │  各チャンクに InterChunkDelayMs のディレイ
  ▼
SendChunkAsync()
  │  Protocol == TCP → NetworkStream.WriteAsync()
  │                    セッションIDで tcpClient or serverSessions から選択
  │  Protocol == UDP → UdpClient.SendAsync()
  ▼
EmitTx()
  │  LogEntry (Direction=TX) を生成
  ▼
Subject<LogEntry>._logSubject.OnNext()      (RXと同じパイプラインへ)
```

## 統計 (Stats) パイプライン

```
NetService コンストラクタ
  └─ Timer (1秒周期)
       └─ PublishStats()
            │  Interlocked.Read で _txBytes/_rxBytes/_txCount/_rxCount/_errorCount 取得
            │  bps = (現在値 - 前回値) / 経過秒 × 8
            ▼
          StatsSnapshot (immutable record)
            ▼
          _statsSubject.OnNext()
            ▼
          MainViewModel.OnStats()
            └─ Dispatcher.InvokeAsync()
                 └─ TxBytes, RxBytes, TxBps, RxBps ... を更新
                      └─ WPF バインディングで StatusBar に反映
```

## 状態変化 (State) パイプライン

```
NetService の各操作メソッド (Connect/Disconnect/Accept/Error...)
  └─ _stateSubject.OnNext(new StateSnapshot(Mode, ConnectionState, SessionId, Remote))
       ▼
     MainViewModel.OnState()
       └─ Dispatcher.InvokeAsync()
            ├─ StateText = "[TCP Client] Connected" など StatusBar テキスト更新
            └─ ルーティング:
                 "TCP Client" → TcpClientVm.UpdateState()
                 "TCP Server" → TcpServerVm.UpdateState()
                 "UDP"        → UdpVm.UpdateState()
```

## チャンクモード別の動作

| モード | Push() の動作 | Flush() の動作 |
|--------|--------------|---------------|
| Raw | 受け取ったバッファをそのまま1個返す | 何も返さない |
| FixedLength | N バイト溜まったら返す、余りはバッファに保持 | 残りをすべて返す |
| Delimiter | 区切りバイト列を探し、見つかるたびに返す | 残りをすべて返す |
| Line | `\n` (0x0A) を区切りとして返す | 残りをすべて返す |
| TimeSlice | N ミリ秒経過後に溜まった全データを返す | 残りをすべて返す |

**実装注意**: `ReadOnlySpan<byte>` はイテレータメソッド (`yield return`) で使用不可。
各 Chunker では `ToArray()` で変換してから返すこと。

## 連番欠落検査パイプライン

```
OnLogEntry() (Dispatcher.InvokeAsync 内、UIスレッド)
  │  entry.Direction == RX かつ IsSeqCheckEnabled == true
  ▼
SeqChecker.Check(sessionKey, entry.Data, SeqCheckDigits)
  │  sessionKey = "{Protocol}:{SessionId}:{Remote}"
  │  末尾 SeqCheckDigits バイトを ASCII 10進数としてパース
  │  初回受信 → null (検査なし、ベースライン記録)
  │  正常連続 → null
  │  欠落検出 → SeqGapResult(LastSeq, Expected, Actual, GapCount)
  ▼
SeqGapResult != null の場合
  │  SeqGapCount++
  │  Direction=Gap の擬似 LogEntry を生成
  │    Data = "[連番欠落] 期待=NNNN 実際=MMMM 欠落数=K" (UTF-8)
  ▼
AddToTraffic(new TrafficEntryViewModel(gapEntry))
  └─ TrafficLog・FilteredLog に挿入 (Matches() で FilterGap チェック)
```

ラップアラウンドは `modulo = 10^digitCount` で処理する。
例: 4桁 → 9999 の次は 0000 を期待する。

## フィルタリング

```
TrafficLog (全件、最大10,000)
  └─ MainViewModel.Matches(vm)
       ├─ FilterGap=false かつ Direction==Gap → false
       ├─ FilterTx=false  かつ Direction==TX  → false
       ├─ FilterRx=false  かつ Direction==RX  → false
       └─ Direction!=Gap かつ FilterText が Remote/Session/AsciiView に含まれない → false
          (Gap エントリはテキストフィルタ対象外 — 欠落警告は常に表示)
  └─ FilteredLog (フィルタ済み、最大10,000)
       └─ DataGrid に表示
```

`FilterText` / `FilterTx` / `FilterRx` / `FilterGap` のいずれかが変更されると `ApplyFilter()` が呼ばれ、
`TrafficLog` を全走査して `FilteredLog` を再構築する。
