using System.Diagnostics;
using Leaderboard.Core;

namespace Leaderboard.Tests;

/// <summary>Stress-style concurrent tests: validates ordering invariants during and after mixed load.</summary>
public class ConcurrentFuzzTests
{
    public static IEnumerable<object[]> Indices => new object[][]
    {
        new object[] { new SkipListSortedRankIndex() },
        new object[] { new BPlusTreeSortedRankIndex() },
        new object[] { new RedBlackTreeSortedRankIndex() }
    };

    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Concurrent_mixed_load_keeps_sorted_leaderboard(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        var sw = Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(4);

        var writers = Enumerable.Range(0, 6).Select(w => Task.Run(async () =>
        {
            var rnd = new Random(unchecked(w * 397 ^ Environment.TickCount));
            while (sw.Elapsed < duration)
            {
                var id = rnd.Next(1, 800);
                var d = (decimal)rnd.Next(-150, 151);
                await store.UpdateScoreAsync(id, d);
            }
        })).ToArray();

        var readers = Enumerable.Range(0, 12).Select(w => Task.Run(async () =>
        {
            var rnd = new Random(unchecked((w + 99) * 7919 ^ Environment.TickCount));
            while (sw.Elapsed < duration)
            {
                var n = store.ActiveBoardCount;
                if (n == 0)
                {
                    await Task.Yield();
                    continue;
                }

                var start = rnd.Next(1, Math.Min(n, 200) + 1);
                var end = rnd.Next(start, Math.Min(start + 80, n) + 1);
                var rows = await store.GetByRankRangeAsync(start, end);
                Assert.InRange(rows.Count, 0, end - start + 1);
                for (var i = 1; i < rows.Count; i++)
                {
                    var a = rows[i - 1];
                    var b = rows[i];
                    Assert.True(a.Score > b.Score || (a.Score == b.Score && a.CustomerId < b.CustomerId));
                }

                var cid = rnd.Next(1, 800);
                _ = await store.GetNeighborhoodAsync(cid, rnd.Next(0, 4), rnd.Next(0, 4));
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));
        await AssertTotalOrderAsync(store);
    }

    /// <summary>Stress test with high concurrency: many writers (100+) competing.</summary>
    [Theory]
    [MemberData(nameof(Indices))]
    public async Task High_concurrency_write_pressure(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        const int writerCount = 100;
        const int operationsPerWriter = 100;
        const int customerRange = 500;

        var tasks = Enumerable.Range(0, writerCount).Select(async w =>
        {
            var rnd = new Random(unchecked(w * 7919 ^ Environment.TickCount));
            for (var i = 0; i < operationsPerWriter; i++)
            {
                var id = rnd.Next(1, customerRange + 1);
                var delta = (decimal)rnd.Next(-500, 501);
                await store.UpdateScoreAsync(id, delta);
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        await AssertTotalOrderAsync(store);
    }

    /// <summary>Write-then-read consistency: after each write, immediately read and verify.</summary>
    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Write_then_read_consistency(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        const int iterations = 1000;
        var rnd = new Random(42);

        for (var i = 0; i < iterations; i++)
        {
            var id = rnd.Next(1, 200);
            var delta = (decimal)rnd.Next(-200, 201);
            var newScore = await store.UpdateScoreAsync(id, delta);

            // Immediately verify: the customer should be findable with correct score
            var neighborhood = await store.GetNeighborhoodAsync(id, 0, 0);
            if (newScore > 0)
            {
                Assert.NotNull(neighborhood);
                Assert.Single(neighborhood);
                Assert.Equal(id, neighborhood[0].CustomerId);
                Assert.Equal(newScore, neighborhood[0].Score);
            }
            else
            {
                // If score <= 0, customer should not be on board
                Assert.Null(neighborhood);
            }
        }
    }

    /// <summary>Extended duration test (10s) to catch subtle race conditions.</summary>
    [Theory]
    [MemberData(nameof(Indices))]
    public async Task Extended_duration_race_detection(ISortedRankIndex index)
    {
        var store = new LeaderboardService(index);
        var duration = TimeSpan.FromSeconds(10);
        var sw = Stopwatch.StartNew();

        // More aggressive: 20 writers, 40 readers
        var writers = Enumerable.Range(0, 20).Select(w => Task.Run(async () =>
        {
            var rnd = new Random(unchecked(w * 9973 ^ Environment.TickCount));
            while (sw.Elapsed < duration)
            {
                var id = rnd.Next(1, 1000);
                var delta = (decimal)rnd.Next(-300, 301);
                await store.UpdateScoreAsync(id, delta);
                await Task.Yield(); // Force more interleaving
            }
        })).ToArray();

        var readers = Enumerable.Range(0, 40).Select(r => Task.Run(async () =>
        {
            var rnd = new Random(unchecked((r + 1000) * 99991 ^ Environment.TickCount));
            while (sw.Elapsed < duration)
            {
                var n = store.ActiveBoardCount;
                if (n == 0)
                {
                    await Task.Yield();
                    continue;
                }

                var start = rnd.Next(1, Math.Min(n, 100) + 1);
                var end = rnd.Next(start, Math.Min(start + 50, n) + 1);
                var rows = await store.GetByRankRangeAsync(start, end);

                // Verify ordering consistency
                for (var i = 1; i < rows.Count; i++)
                {
                    var a = rows[i - 1];
                    var b = rows[i];
                    Assert.True(a.Score > b.Score || (a.Score == b.Score && a.CustomerId < b.CustomerId),
                        $"Ordering violation: rank {a.Rank} (score {a.Score}, id {a.CustomerId}) should be better than rank {b.Rank} (score {b.Score}, id {b.CustomerId})");
                }

                var cid = rnd.Next(1, 1000);
                _ = await store.GetNeighborhoodAsync(cid, rnd.Next(0, 3), rnd.Next(0, 3));
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));
        await AssertTotalOrderAsync(store);
    }

    private static async Task AssertTotalOrderAsync(ILeaderboardStore store)
    {
        var c = store.ActiveBoardCount;
        if (c == 0)
            return;

        var rows = await store.GetByRankRangeAsync(1, c);
        Assert.Equal(c, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(i + 1, rows[i].Rank);
            if (i == 0)
                continue;
            var a = rows[i - 1];
            var b = rows[i];
            Assert.True(a.Score > b.Score || (a.Score == b.Score && a.CustomerId < b.CustomerId));
        }
    }
}
