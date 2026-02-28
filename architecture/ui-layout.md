# UI レイアウト

## ウィンドウ全体構造 (DockPanel)

```
┌─────────────────────────────────────────────────────────┐
│  Menu Bar  [接続][ツール][ヘルプ]                        │ DockPanel.Dock=Top
├─────────────────────────────────────────────────────────┤
│  Tool Bar  (将来拡張用スペース)                          │ DockPanel.Dock=Top
├──────────────────────┬──────────────────────────────────┤
│                      │  Traffic Monitor                 │
│  Tab Control         │  ┌ Filter Bar ────────────────┐ │
│  (280px 固定幅)      │  │ [キーワード] [TX][RX][Clear]│ │
│  ┌─────────────────┐ │  └──────────────────────────────┘│
│  │ TCP Client タブ │ │  DataGrid (FilteredLog)          │ Main Grid Row0
│  │  Host / Port    │ │  Time | Dir | Proto | Remote |  │
│  │  ChunkMode      │ │  Len | ASCII preview             │
│  │  [Connect]      │ │                                  │
│  └─────────────────┘ │  ── GridSplitter ──              │
│  ┌─────────────────┐ │  HexDump Panel (130px)           │
│  │ TCP Server タブ │ │  (SelectedEntry の Data を表示)  │
│  │  BindIP / Port  │ │                                  │
│  │  ChunkMode      │ │                                  │
│  │  [Start/Stop]   │ │                                  │
│  └─────────────────┘ │                                  │
│  ┌─────────────────┐ │                                  │
│  │  UDP タブ       │ │                                  │
│  │  LocalPort      │ │                                  │
│  │  RemoteHost:Port│ │                                  │
│  │  [Start/Stop]   │ │                                  │
│  └─────────────────┘ │                                  │
├──────────────────────┴──────────────────────────────────┤
│  ── GridSplitter (水平) ──                               │ Main Grid Row1
├─────────────────────────────────────────────────────────┤
│  Send Panel (195px)                                     │ Main Grid Row2
│  SendMode: [Text ▼]  [テキスト入力 / HEX / ファイル]   │
│  Repeat: [ON/OFF] Count: [1] Interval: [1000] ms       │
│  Split:  [ON/OFF] Size:  [64] Random: [ON/OFF]         │
│  InterChunkDelay: [0] ms    [Send]  SendStatus         │
├─────────────────────────────────────────────────────────┤
│  Status Bar  [状態テキスト] [TX/RX stats] [Error cnt]  │ DockPanel.Dock=Bottom
└─────────────────────────────────────────────────────────┘
```

## バインディング対応表

| UI 要素 | バインド先 (DataContext = MainViewModel) |
|---------|----------------------------------------|
| TabControl.SelectedIndex | `SelectedTabIndex` (setter でSendVm.Protocolも自動切替) |
| TCP Client 各フィールド | `TcpClientVm.*` |
| TCP Server 各フィールド | `TcpServerVm.*` |
| UDP 各フィールド | `UdpVm.*` |
| 送信パネル | `SendVm.*` |
| DataGrid.ItemsSource | `FilteredLog` |
| DataGrid.SelectedItem | `SelectedEntry` |
| HexDump | `SelectedEntry.HexDump` |
| フィルタキーワード | `FilterText` |
| TX/RXチェック | `FilterTx`, `FilterRx` |
| StatusBar 左テキスト | `StateText` |
| StatusBar 統計 | `TxBytes`, `RxBytes`, `TxBps`, `RxBps`, `TxCount`, `RxCount`, `ErrorCount` |
| ログ記録 ON/OFF | `LogEnabled` |
| ログフォルダ | `LogFolder` |

## コードビハインドの役割 (MainWindow.xaml.cs)

- 起動時: `SettingsService.Load()` → `ApplySettings()` / ウィンドウ位置復元
- `FilteredLog.CollectionChanged` を購読して新規エントリを DataGrid 最下行へ自動スクロール
- 終了時: `CaptureSettings()` → `SettingsService.Save()` / `_viewModel.Dispose()`
- メニュー [終了] / [バージョン情報] のクリックハンドラ

ウィンドウ最大化から終了した場合は `RestoreBounds` から正規サイズを保存する。

## SendModeToVisibilityConverter

送信パネルで選択中の `SendMode` (Text / Hex / File) に応じて、
対応する入力エリアのみを `Visible` にし、他を `Collapsed` にするコンバーター。
`IValueConverter` 実装。`ConverterParameter` に `SendMode` 名を文字列で渡す。
