# NetTestConsole

TCP / UDP 通信のテストと監視を行う Windows デスクトップアプリケーションです。

## 動作環境

- Windows 10 / 11 (x64)
- .NET 8 ランタイム (Windows)

## ビルド方法

```bash
dotnet build TcpUdpTester/TcpUdpTester.csproj -c Release
```

出力: `TcpUdpTester/bin/Release/net8.0-windows/NetTestConsole.exe`

## 主な機能

### 接続モード (タブ切替)

| タブ | 説明 |
|------|------|
| TCP Client | 指定ホスト:ポートへ接続 |
| TCP Server | 指定 IP:ポートでリッスン、複数クライアントを受付 |
| UDP | ローカルポートをバインドし、リモートへ送受信 |

### チャンクモード

受信データを分割するルールを選択できます。

| モード | 説明 |
|--------|------|
| Raw | 受信バッファをそのまま1エントリとして扱う |
| Delimiter | 指定バイト列で区切る |
| FixedLength | 固定バイト数で区切る |
| TimeSlice | 指定時間内に届いたデータをまとめる |
| Line | 改行 (`\n`) で区切る |

### 送信パネル

- **入力形式**: テキスト (UTF-8) / HEX (`AA BB CC` 形式) / ファイル
- **リピート送信**: 回数とインターバル (ms) を指定して繰り返し送信
- **分割送信**: 固定サイズまたはランダムサイズでデータを分割して送信、チャンク間遅延設定あり

### トラフィックモニター

- TX / RX の全通信ログをリアルタイム表示 (最大 10,000 件)
- キーワード / 方向 (TX/RX) でリアルタイムフィルタリング
- 選択エントリのバイナリを HEX ダンプ表示
- フィルタ済みログを CSV にエクスポート

### バイナリログ (`.ntlg`)

ログ記録を有効にすると、全通信を独自バイナリ形式 `.ntlg` でディスクに保存します。

- 保存先: `Documents\NetTestConsole\Logs\ntlog_YYYYMMDD_HHmmss.ntlg`
- ファイルヘッダ: マジック `NTLG` + バージョン + 作成タイムスタンプ (UTC μs)
- レコードヘッダ: タイムスタンプ / プロトコル / 方向 / セッション ID / リモート / データ長 / CRC32

## プロジェクト構成

```
TcpUdpTester/
├── Models/
│   ├── Enums.cs               Protocol, Direction, ChunkMode
│   ├── LogEntry.cs            1通信エントリのモデル
│   ├── SendOptions.cs         リピート / 分割オプション
│   ├── SendRequest.cs         送信リクエスト
│   ├── StatsSnapshot.cs       通信統計スナップショット
│   └── StateSnapshot.cs       接続状態スナップショット
├── Core/
│   ├── Chunkers/              IChunker と各実装
│   ├── INetService.cs         サービスインターフェース
│   ├── NetService.cs          TCP/UDP の非同期実装
│   └── BinaryLogWriter.cs     .ntlg バイナリログ書き込み
├── ViewModels/
│   ├── MainViewModel.cs       ルート ViewModel
│   ├── TcpClientViewModel.cs
│   ├── TcpServerViewModel.cs
│   ├── UdpViewModel.cs
│   ├── SendViewModel.cs
│   └── TrafficEntryViewModel.cs
├── Converters/
│   └── SendModeToVisibilityConverter.cs
├── App.xaml
└── MainWindow.xaml
```

## 依存パッケージ

| パッケージ | バージョン |
|-----------|-----------|
| System.Reactive | 6.0.1 |
