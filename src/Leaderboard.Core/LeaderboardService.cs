namespace Leaderboard.Core;

/// <summary>
/// Coordinates the score dictionary and the sorted index. Only customers with score &gt; 0 appear in the index.
/// Uses a reader-writer lock: reads may run concurrently; writes are exclusive.
/// </summary>
public sealed class LeaderboardService : ILeaderboardStore
{
    private readonly ISortedRankIndex _index;
    private readonly Dictionary<long, decimal> _scores = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public LeaderboardService(ISortedRankIndex index) => _index = index;

    public int ActiveBoardCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _index.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public Task<decimal> UpdateScoreAsync(long customerId, decimal delta, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId), "CustomerId must be a positive int64.");
        if (delta is < -1000m or > 1000m)
            throw new ArgumentOutOfRangeException(nameof(delta), "Delta must be in [-1000, +1000].");

        _lock.EnterWriteLock();
        try
        {
            _scores.TryGetValue(customerId, out var oldScore);
            var newScore = oldScore + delta;
            _scores[customerId] = newScore;

            var oldKey = new RankKey(oldScore, customerId);
            var newKey = new RankKey(newScore, customerId);

            // Remove old board position if the customer was on the board
            if (oldScore > 0)
                _index.Remove(in oldKey);

            // Insert at the new position if still on the board
            if (newScore > 0)
                _index.Insert(in newKey);

            return Task.FromResult(newScore);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public Task<IReadOnlyList<RankedCustomer>> GetByRankRangeAsync(int start, int end, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (start < 1 || end < start)
            throw new ArgumentOutOfRangeException(nameof(start), "Invalid rank range (start >= 1 and end >= start).");

        _lock.EnterReadLock();
        try
        {
            var list = new List<RankedCustomer>(Math.Min(64, end - start + 1));
            _index.FillRangeByRank(start, end, list);
            return Task.FromResult<IReadOnlyList<RankedCustomer>>(list);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task<IReadOnlyList<RankedCustomer>?> GetNeighborhoodAsync(long customerId, int higher, int lower, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (higher < 0 || lower < 0) throw new ArgumentOutOfRangeException(nameof(higher), "high and low must be non-negative.");

        _lock.EnterReadLock();
        try
        {
            if (!_scores.TryGetValue(customerId, out var score) || score <= 0)
                return Task.FromResult<IReadOnlyList<RankedCustomer>?>(null);

            var key = new RankKey(score, customerId);
            var buf = new List<RankedCustomer>(higher + lower + 1);
            if (!_index.TryFillNeighborhood(in key, higher, lower, buf))
                return Task.FromResult<IReadOnlyList<RankedCustomer>?>(null);

            return Task.FromResult<IReadOnlyList<RankedCustomer>?>(buf);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}
