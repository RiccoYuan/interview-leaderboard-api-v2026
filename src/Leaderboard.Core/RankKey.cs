namespace Leaderboard.Core;

/// <summary>
/// Sort key on the board: higher score first; ties broken by smaller <see cref="CustomerId"/> (better rank).
/// </summary>
public readonly record struct RankKey(decimal Score, long CustomerId)
{
    /// <summary>Sentinel key that sorts before any real key; used only as the skip list head.</summary>
    public static RankKey Sentinel { get; } = new(decimal.MaxValue, long.MinValue);

    /// <summary>
    /// Total order left-to-right on the board: return value &lt; 0 means <paramref name="a"/> is left of <paramref name="b"/> (better rank).
    /// </summary>
    public static int Compare(in RankKey a, in RankKey b)
    {
        int c = decimal.Compare(b.Score, a.Score);
        if (c != 0) return c;
        return a.CustomerId.CompareTo(b.CustomerId);
    }
}
