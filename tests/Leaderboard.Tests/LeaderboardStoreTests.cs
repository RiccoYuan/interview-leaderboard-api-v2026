using Leaderboard.Core;

namespace Leaderboard.Tests;

public class LeaderboardStoreTests
{
    public static IEnumerable<object[]> Indices => new object[][]
    {
        new object[] { new SkipListSortedRankIndex() },
        new object[] { new BPlusTreeSortedRankIndex() },
        new object[] { new RedBlackTreeSortedRankIndex() }
    };

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Assignment_sample_order_and_ranks(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        await store.UpdateScoreAsync(15514665, 124);
        await store.UpdateScoreAsync(81546541, 113);
        await store.UpdateScoreAsync(1745431, 100);
        await store.UpdateScoreAsync(76786448, 100);
        await store.UpdateScoreAsync(254814111, 96);
        await store.UpdateScoreAsync(53274324, 95);
        await store.UpdateScoreAsync(6144320, 93);
        await store.UpdateScoreAsync(8009471, 93);
        await store.UpdateScoreAsync(11028481, 93);
        await store.UpdateScoreAsync(38819, 92);

        var top = await store.GetByRankRangeAsync(1, 10);
        Assert.Equal(10, top.Count);
        Assert.Equal(15514665, top[0].CustomerId);
        Assert.Equal(124, top[0].Score);
        Assert.Equal(1, top[0].Rank);
        Assert.Equal(81546541, top[1].CustomerId);
        Assert.Equal(1745431, top[2].CustomerId);
        Assert.Equal(76786448, top[3].CustomerId);
        Assert.Equal(38819, top[^1].CustomerId);
        Assert.Equal(10, top[^1].Rank);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Update_decrease_and_reinsert(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        Assert.Equal(80, await store.UpdateScoreAsync(3333333, 80));
        Assert.Equal(60, await store.UpdateScoreAsync(3333333, -20));
        var one = await store.GetByRankRangeAsync(1, 1);
        Assert.Single(one);
        Assert.Equal(3333333, one[0].CustomerId);
        Assert.Equal(60, one[0].Score);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Score_zero_removes_from_board(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        await store.UpdateScoreAsync(1, 10);
        Assert.Single(await store.GetByRankRangeAsync(1, 1));
        Assert.Equal(-5, await store.UpdateScoreAsync(1, -15));
        Assert.Empty(await store.GetByRankRangeAsync(1, 1));
        Assert.Null(await store.GetNeighborhoodAsync(1, 0, 0));
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Neighborhood_matches_rank_window(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        for (var i = 1; i <= 10; i++)
            await store.UpdateScoreAsync(i, i * 10);

        var n = await store.GetNeighborhoodAsync(5, 2, 3);
        Assert.NotNull(n);
        Assert.Equal(6, n!.Count);
        // Scores 10..100, customer 5 => score 50 => rank 6; high=2 => ranks 4..5; low=3 => ranks 7..9
        Assert.Equal(7, n[0].CustomerId);
        Assert.Equal(6, n[1].CustomerId);
        Assert.Equal(5, n[2].CustomerId);
        Assert.Equal(4, n[3].CustomerId);
        Assert.Equal(3, n[4].CustomerId);
        Assert.Equal(2, n[5].CustomerId);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Random_interleaved_updates_match_bruteforce_rank(ISortedRankIndex index)
    {
        var svc = new LeaderboardService(index);
        var rng = new Random(42);
        var dict = new Dictionary<long, decimal>();
        const int customers = 200;
        const int steps = 2000;

        for (var t = 0; t < steps; t++)
        {
            var id = rng.Next(1, customers + 1);
            var delta = (decimal)(rng.Next(-500, 501));
            dict.TryGetValue(id, out var old);
            var newScore = old + delta;
            dict[id] = newScore;
            await svc.UpdateScoreAsync(id, delta);

            var board = dict.Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select((kv, i) => (kv.Key, kv.Value, Rank: i + 1))
                .ToList();

            foreach (var row in board)
            {
                var nb = await svc.GetNeighborhoodAsync(row.Item1, 0, 0);
                Assert.NotNull(nb);
                Assert.Equal(row.Rank, nb![0].Rank);
                Assert.Equal(row.Item2, nb[0].Score);
            }
        }
    }
}
