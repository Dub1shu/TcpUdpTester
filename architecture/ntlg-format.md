# .ntlg バイナリログ形式仕様

## 概要

NetTestConsole 独自のバイナリログフォーマット。
拡張子: `.ntlg`
保存先: `%USERPROFILE%\Documents\NetTestConsole\Logs\ntlog_YYYYMMDD_HHmmss.ntlg`

ファイルは起動後に最初のログエントリが届いた時点で生成される（事前には作らない）。

---

## ファイルヘッダ (32 bytes 固定)

| オフセット | サイズ | フィールド | 値 |
|-----------|--------|-----------|-----|
| 0 | 4 | Magic | `4E 54 4C 47` ("NTLG") |
| 4 | 1 | FileVersion | `01` |
| 5 | 1 | FileHeaderSize | `20` (32 = 0x20) |
| 6 | 2 | Flags | `00 00` (現在未使用) |
| 8 | 8 | CreatedUnixUs | 作成時刻 (UTC, Unix epoch マイクロ秒, LE) |
| 16 | 16 | Reserved | 全ゼロ |

エンディアン: すべて **リトルエンディアン**

---

## レコード (可変長、ファイルヘッダの直後から連続して格納)

### レコードヘッダ (28 bytes 固定)

| オフセット | サイズ | フィールド | 説明 |
|-----------|--------|-----------|------|
| 0 | 4 | RecordSize | このレコード全体のバイト数 (ヘッダ + 可変部) |
| 4 | 8 | TimestampUnixUs | イベント時刻 (UTC, Unix epoch マイクロ秒, LE) |
| 12 | 1 | Protocol | `0` = TCP, `1` = UDP |
| 13 | 1 | Direction | `0` = TX, `1` = RX |
| 14 | 2 | SessionIdLen | SessionId の UTF-8 バイト数 |
| 16 | 2 | RemoteLen | Remote の UTF-8 バイト数 |
| 18 | 4 | DataLen | ペイロードのバイト数 |
| 22 | 2 | ChunkFlags | `00 00` (現在未使用) |
| 24 | 4 | CRC32 | 可変部 (SessionId + Remote + Data) の CRC32 |

### レコード可変部 (RecordSize - 28 bytes)

```
[SessionId bytes (SessionIdLen)] [Remote bytes (RemoteLen)] [Data bytes (DataLen)]
```

### CRC32 アルゴリズム

標準 CRC-32 (多項式 0xEDB88320, 反射ビット順):
```
uint crc = 0xFFFFFFFF;
foreach (byte b in data) {
    crc ^= b;
    for (int i = 0; i < 8; i++)
        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
}
return crc ^ 0xFFFFFFFF;
```
対象データ: `SessionId bytes || Remote bytes || Data bytes` を結合したバイト列

---

## 実装メモ

- `BinaryLogWriter` は `Channel<LogEntry>` (UnboundedChannel, SingleReader) でキューイングし、
  専用バックグラウンドタスク (`WriteLoopAsync`) が逐次的に書き込む。
- ファイルは `FileShare.Read` で開くため、別プロセスからリアルタイム閲覧が可能。
- ログ書き込みエラーはキャッチして無視（アプリの動作を妨げない）。
- `IsEnabled = false` の間は `Enqueue()` が即座に捨てる。
- ファイルストリームは `BinaryLogWriter` ライフタイム中に1ファイルのみ使用（ローテーションなし）。

---

## ファイル読み込み手順 (将来の読み取りツール実装向け)

1. ファイル先頭4バイトが `NTLG` かを確認 (Magic チェック)
2. オフセット4の `FileVersion` を確認 (現在 `1` のみ)
3. オフセット5の `FileHeaderSize` バイト分スキップ (ヘッダを読み飛ばす)
4. ループ:
   a. 28 バイト読み込み (レコードヘッダ)
   b. `RecordSize - 28` バイト読み込み (可変部)
   c. CRC32 を検証
   d. SessionIdLen, RemoteLen, DataLen に従ってフィールドを切り出す
5. EOF に達したらループ終了
