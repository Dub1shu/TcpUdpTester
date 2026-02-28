# NetTestConsole アーキテクチャ概要

## 目的

Windows デスクトップ上で TCP/UDP 通信のテスト・監視を行うツール。
送受信データをリアルタイムに可視化し、バイナリログへの永続化も行う。

---

## 技術スタック

| 項目 | 内容 |
|------|------|
| フレームワーク | WPF / .NET 8 (Windows) |
| アーキテクチャパターン | MVVM |
| リアクティブストリーム | System.Reactive 6.0.1 (`IObservable<T>` / `Subject<T>`) |
| 出力バイナリ | `NetTestConsole.exe` |
| 設定永続化 | JSON (`%APPDATA%\NetTestConsole\settings.json`) |
| ログ永続化 | 独自バイナリ形式 `.ntlg` |

---

## レイヤー構成

```
┌──────────────────────────────────────────────────────┐
│  View Layer  (XAML)                                  │
│  MainWindow.xaml  ─ DataContext → MainViewModel      │
└──────────────────────┬───────────────────────────────┘
                       │ INotifyPropertyChanged / Command
┌──────────────────────▼───────────────────────────────┐
│  ViewModel Layer                                     │
│  MainViewModel                                       │
│  ├── TcpClientViewModel                              │
│  ├── TcpServerViewModel                              │
│  ├── UdpViewModel                                    │
│  ├── SendViewModel                                   │
│  └── TrafficEntryViewModel  (per-log-entry)          │
└──────────────────────┬───────────────────────────────┘
                       │ INetService (interface)
                       │ IObservable<LogEntry / StatsSnapshot / StateSnapshot>
┌──────────────────────▼───────────────────────────────┐
│  Core / Service Layer                                │
│  NetService  ─ TCP/UDP 非同期 I/O                    │
│  BinaryLogWriter  ─ .ntlg ディスク書き込み           │
│  Chunkers  ─ バイト列分割ロジック                    │
│  SettingsService  ─ JSON 設定読み書き                │
└──────────────────────┬───────────────────────────────┘
                       │ System.Net.Sockets
┌──────────────────────▼───────────────────────────────┐
│  OS / Network                                        │
│  TcpClient / TcpListener / UdpClient                 │
└──────────────────────────────────────────────────────┘
```

---

## フォルダ構成と責務

```
TcpUdpTester/
├── Models/             ── データモデル (純粋な POCO / record)
│   ├── Enums.cs            Protocol, Direction, ChunkMode
│   ├── LogEntry.cs         1通信エントリ (immutable record)
│   ├── SendOptions.cs      送信オプション (分割/リピート設定)
│   ├── SendRequest.cs      送信リクエスト
│   ├── StatsSnapshot.cs    通信統計 (immutable record)
│   └── StateSnapshot.cs    接続状態 (immutable record)
│
├── Core/               ── ビジネスロジック・サービス
│   ├── INetService.cs      サービスインターフェース (DI境界)
│   ├── NetService.cs       TCP/UDP I/O の実装
│   ├── BinaryLogWriter.cs  .ntlg ログ書き込み
│   ├── SeqChecker.cs       受信連番欠落検査
│   ├── AppSettings.cs      設定 POCO
│   ├── SettingsService.cs  JSON 設定永続化 (static)
│   └── Chunkers/
│       ├── IChunker.cs     チャンク分割インターフェース
│       ├── RawChunker.cs       受信バッファをそのまま返す
│       ├── FixedLengthChunker  N バイト固定で区切る
│       ├── DelimiterChunker    指定バイト列で区切る
│       ├── LineChunker         \n (0x0A) で区切る
│       └── TimeSliceChunker    N ミリ秒以内のデータをまとめる
│
├── ViewModels/         ── MVVM の VM 層
│   ├── ViewModelBase.cs    INotifyPropertyChanged 基底
│   ├── RelayCommand.cs     ICommand 実装
│   ├── MainViewModel.cs    ルート VM・ストリーム購読・フィルタ
│   ├── TcpClientViewModel  TCP Client タブ
│   ├── TcpServerViewModel  TCP Server タブ
│   ├── UdpViewModel        UDP タブ
│   ├── SendViewModel       送信パネル
│   └── TrafficEntryViewModel  ログ1件の表示用ラッパー
│
├── Converters/
│   └── SendModeToVisibilityConverter  SendMode → UI パネル表示切替 (Random パネル対応)
│
├── App.xaml / App.xaml.cs
├── MainWindow.xaml          UI レイアウト定義
└── MainWindow.xaml.cs       ウィンドウのコードビハインド
```

---

## 主要な設計判断

### 1. INetService でサービスを抽象化
ViewModelはすべて `INetService` を通じてネットワーク操作を行う。
テスト・差し替えを想定した DI 境界。ただし現状は `MainViewModel` が `new NetService()` で直接生成している。

### 2. Reactive Streams でイベント配信
`NetService` は 3 本の `Subject<T>` を持ち、IObservable として公開する。

| ストリーム | 型 | 用途 |
|-----------|-----|------|
| `LogStream` | `IObservable<LogEntry>` | TX/RX 1件ごとのログ |
| `StatsStream` | `IObservable<StatsSnapshot>` | 1秒ごとの通信統計 |
| `StateStream` | `IObservable<StateSnapshot>` | 接続状態の変化 |

### 3. UIスレッド更新は Dispatcher.InvokeAsync
サブスクリプションコールバックはバックグラウンドスレッドから呼ばれるため、
`Application.Current?.Dispatcher.InvokeAsync()` で UI スレッドに戻してから ObservableCollection を更新する。

### 4. BinaryLogWriter は Channel でシーケンシャル書き込み
`Channel<LogEntry>` (UnboundedChannel, SingleReader) を使い、ディスク I/O を専用バックグラウンドスレッドに分離する。
ログ書き込みエラーはサイレントに無視（テストツールとしてのロバスト性を優先）。

### 5. 設定は起動時ロード / 終了時セーブ
`MainWindow` コンストラクタで `SettingsService.Load()` → `ApplySettings()`
`Window_Closing` イベントで `CaptureSettings()` → `SettingsService.Save()`
永続化対象にはランダムモード、連番サフィックス、負荷テスト、連番検査の全パラメータを含む。

### 6. Random 送信モード
`SendMode.Random` を選択すると毎送信ごとに `Random.Shared` でランダムバイト列を生成する。
`RandomMinSize` / `RandomMaxSize` で生成サイズ範囲を指定。
リピート・負荷テスト・連番サフィックスと組み合わせ可能。

### 7. 連番サフィックス (SeqSuffix)
送信データの末尾に `SeqSuffixDigits` 桁のゼロパディング10進連番を付与する機能。
カウンタは `Interlocked.Increment` でアトミックに更新し、スレッドセーフを保証する。
受信側の `SeqChecker` と対になって使用することで連番欠落を検出できる。

### 8. 負荷テストモード (LoadTest)
カウントではなく時間 (`LoadTestDurationSec` 秒) を基準に送信を繰り返すモード。
`LoadTestTargetMbps > 0` のとき送信済みバイト数と経過時間から超過量を計算し
`Task.Delay` でレート制限を行う。ステータスバーに 200ms 周期で速度を表示する。

### 9. 連番欠落検査 (SeqChecker)
`Core/SeqChecker.cs` が受信データ末尾の N 桁 ASCII 10進連番を検査する。
セッションキー (`Protocol:SessionId:Remote`) ごとに最終連番を保持し、
期待値と不一致の場合は `SeqGapResult` (欠落数付き) を返す。
`MainViewModel` は欠落を検出すると `Direction.Gap` の擬似 `LogEntry` を生成して
トラフィックログに挿入し、`SeqGapCount` をインクリメントする。
