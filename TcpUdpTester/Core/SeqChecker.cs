using System.Collections.Concurrent;

namespace TcpUdpTester.Core;

/// <summary>受信データ末尾の ASCII 10進連番を検査し、欠落を検出する。</summary>
public sealed class SeqChecker
{
    private readonly ConcurrentDictionary<string, long> _lastSeq = new();

    /// <summary>
    /// データを検査する。連番欠落を検出した場合は SeqGapResult を返す。
    /// 初回受信・正常連続の場合は null を返す。
    /// </summary>
    public SeqGapResult? Check(string sessionKey, byte[] data, int digitCount)
    {
        if (data.Length < digitCount) return null;

        var seqBytes = data.AsSpan(data.Length - digitCount, digitCount);
        if (!TryParseAsciiDecimal(seqBytes, out long actual)) return null;

        // 10^digitCount がラップアラウンド境界 (例: 4桁 → 10000)
        long modulo = 1;
        for (int i = 0; i < digitCount; i++) modulo *= 10;

        if (!_lastSeq.TryGetValue(sessionKey, out long last))
        {
            _lastSeq[sessionKey] = actual;
            return null; // 初回は検査しない
        }

        long expected = (last + 1) % modulo;
        _lastSeq[sessionKey] = actual;

        if (actual == expected) return null; // 正常

        long gapCount = (actual - expected + modulo) % modulo;
        return new SeqGapResult(last, expected, actual, gapCount);
    }

    /// <summary>指定セッションのシーケンス状態をリセットする。</summary>
    public void ResetSession(string sessionKey) => _lastSeq.TryRemove(sessionKey, out _);

    /// <summary>全セッションのシーケンス状態をリセットする。</summary>
    public void Reset() => _lastSeq.Clear();

    private static bool TryParseAsciiDecimal(ReadOnlySpan<byte> bytes, out long value)
    {
        value = 0;
        foreach (var b in bytes)
        {
            if (b < (byte)'0' || b > (byte)'9') return false;
            value = value * 10 + (b - '0');
        }
        return true;
    }
}

/// <summary>連番欠落の検出結果。</summary>
public sealed record SeqGapResult(long LastSeq, long Expected, long Actual, long GapCount);
