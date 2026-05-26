namespace Leaderboard.Core;

/// <summary>Application-facing leaderboard API; implementations must be thread-safe.</summary>
public interface ILeaderboardStore
{
    /// <summary>Number of customers currently on the board (score &gt; 0).</summary>
    int ActiveBoardCount { get; }

    /// <summary>POST /customer/{id}/score/{delta} — returns the score after applying the delta.</summary>
    Task<decimal> UpdateScoreAsync(long customerId, decimal delta, CancellationToken cancellationToken = default);

    /// <summary>GET /leaderboard?start=&amp;end=</summary>
    Task<IReadOnlyList<RankedCustomer>> GetByRankRangeAsync(int start, int end, CancellationToken cancellationToken = default);

    /// <summary>GET /leaderboard/{id}?high=&amp;low=</summary>
    Task<IReadOnlyList<RankedCustomer>?> GetNeighborhoodAsync(long customerId, int higher, int lower, CancellationToken cancellationToken = default);
}
