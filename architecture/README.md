# アーキテクチャドキュメント

NetTestConsole の実装者向けアーキテクチャ参照資料。

## ドキュメント一覧

| ファイル | 内容 |
|---------|------|
| [overview.md](overview.md) | レイヤー構成・フォルダ責務・主要設計判断 |
| [data-flow.md](data-flow.md) | RX/TX/Stats/Stateの各データフロー・チャンクモード動作 |
| [interfaces-and-models.md](interfaces-and-models.md) | INetService・IChunker・全モデル型の定義と説明 |
| [ui-layout.md](ui-layout.md) | UIレイアウト構造・バインディング対応表 |
| [ntlg-format.md](ntlg-format.md) | .ntlg バイナリログファイルのバイトレベル仕様 |

## 読む順番 (推奨)

1. **overview.md** でレイヤー全体像を掴む
2. **interfaces-and-models.md** で型・契約を確認する
3. **data-flow.md** でデータの流れを理解する
4. **ui-layout.md** で UI と ViewModel のバインディングを確認する
5. **ntlg-format.md** はログ読み取りツールを作る際のみ参照

## クイックリファレンス

### 新しい ChunkMode を追加する

1. `TcpUdpTester/Core/Chunkers/` に `XxxChunker.cs` を作成 (`IChunker` 実装)
2. `Models/Enums.cs` の `ChunkMode` enum に値を追加
3. `NetService.CreateChunker()` の switch 式にケースを追加
4. UI の ChunkMode コンボボックスは enum を列挙するため自動反映される

### 新しい送信モード (SendMode) を追加する

1. `ViewModels/SendViewModel.cs` の `SendMode` enum に値を追加
2. `SendViewModel.BuildData()` の switch 式に変換ロジックを追加
3. XAML に対応する入力 UI を追加し、`SendModeToVisibilityConverter` でバインド

### ネットワーク統計を追加する

1. `Models/StatsSnapshot.cs` に新フィールドを追加
2. `NetService.PublishStats()` で値を計算して含める
3. `MainViewModel.OnStats()` で ViewModel プロパティに反映
4. StatusBar の XAML にバインドを追加
