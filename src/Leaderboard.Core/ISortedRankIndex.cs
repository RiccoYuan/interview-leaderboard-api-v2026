namespace Leaderboard.Core;

/// <summary>
/// Ordered index of customers with score &gt; 0 only. Thread safety is provided by <see cref="LeaderboardService"/>.
/// </summary>
public interface ISortedRankIndex
{
    int Count { get; }

    void Insert(in RankKey key);

    void Remove(in RankKey key);

    /// <summary>1-based rank; returns false if the key is not present.</summary>
    bool TryGetRank(in RankKey key, out int rank);

    /// <summary>Fills <paramref name="buffer"/> with ranks in [<paramref name="startRank"/>, <paramref name="endRank"/>] inclusive.</summary>
    void FillRangeByRank(int startRank, int endRank, List<RankedCustomer> buffer);

    /// <summary>
    /// Fills <paramref name="buffer"/> around <paramref name="key"/>: <paramref name="higher"/> neighbors with a better rank
    /// (smaller rank number), <paramref name="lower"/> neighbors with a worse rank, including the customer.
    /// Matches the assignment: high = count toward better ranks.
    /// </summary>
    bool TryFillNeighborhood(in RankKey key, int higher, int lower, List<RankedCustomer> buffer);
}
