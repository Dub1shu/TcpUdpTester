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

**入力形式**

| モード | 説明 |
|--------|------|
| Text | UTF-8 テキスト入力 |
| Hex | `AA BB CC` 形式の HEX 入力 |
| File | バイナリファイルをそのまま送信 |
| Random | 指定サイズ範囲 (Min〜Max bytes) のランダムデータを毎回生成して送信 |

**送信オプション**

- **リピート送信**: 回数とインターバル (ms) を指定して繰り返し送信
- **分割送信**: 固定サイズまたはランダムサイズでデータを分割して送信、チャンク間遅延設定あり
- **連番サフィックス**: 送信データの末尾に N 桁ゼロパディング 10進連番を付与。受信側の連番検査と組み合わせて欠落を検出できる
- **負荷テスト**: 時間ベース (秒) でデータを送り続けるモード。目標 Mbps を指定してレート制限も可能

### トラフィックモニター

- TX / RX の全通信ログをリアルタイム表示 (最大 10,000 件)
- キーワード / 方向 (TX / RX / Gap) でリアルタイムフィルタリング
- 選択エントリのバイナリを HEX ダンプ表示
- フィルタ済みログを CSV にエクスポート

### 連番欠落検査

受信データ末尾の N 桁 ASCII 10進連番を監視し、欠落を自動検出します。

- **有効化**: 受信パネルの「連番検査」チェックをオン → 桁数を指定
- **欠落検出時**: トラフィックログに `[連番欠落] 期待=NNNN 実際=MMMM 欠落数=K` という Gap エントリを挿入し、欠落カウンタをインクリメント
- **セッション別管理**: TCP Server の複数接続を含め、セッションごとに独立して連番を追跡
- **ラップアラウンド対応**: 4桁なら 9999 → 0000 を正常と判定
- **クリア**: トラフィックログのクリアと同時にカウンタとシーケンス状態がリセットされる

### バイナリログ (`.ntlg`)

ログ記録を有効にすると、全通信を独自バイナリ形式 `.ntlg` でディスクに保存します。

- 保存先: `Documents\NetTestConsole\Logs\ntlog_YYYYMMDD_HHmmss.ntlg`
- ファイルヘッダ: マジック `NTLG` + バージョン + 作成タイムスタンプ (UTC μs)
- レコードヘッダ: タイムスタンプ / プロトコル / 方向 / セッション ID / リモート / データ長 / CRC32

## プロジェクト構成

```
TcpUdpTester/
├── Models/
│   ├── Enums.cs               Protocol, Direction (TX/RX/Gap), ChunkMode, SendMode
│   ├── LogEntry.cs            1通信エントリのモデル
│   ├── SendOptions.cs         リピート / 分割オプション
│   ├── SendRequest.cs         送信リクエスト
│   ├── StatsSnapshot.cs       通信統計スナップショット
│   └── StateSnapshot.cs       接続状態スナップショット
├── Core/
│   ├── Chunkers/              IChunker と各実装
│   ├── INetService.cs         サービスインターフェース
│   ├── NetService.cs          TCP/UDP の非同期実装
│   ├── SeqChecker.cs          受信連番欠落検査
│   └── BinaryLogWriter.cs     .ntlg バイナリログ書き込み
├── ViewModels/
│   ├── MainViewModel.cs       ルート ViewModel・連番検査統合
│   ├── TcpClientViewModel.cs
│   ├── TcpServerViewModel.cs
│   ├── UdpViewModel.cs
│   ├── SendViewModel.cs       Random / SeqSuffix / LoadTest モード
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
