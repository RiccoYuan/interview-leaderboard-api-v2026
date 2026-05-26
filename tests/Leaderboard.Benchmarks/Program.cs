using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Leaderboard.Core;

namespace Leaderboard.Benchmarks;

[MemoryDiagnoser]
public class IndexComparisonBenchmarks
{
    private LeaderboardService _skipList = null!;
    private LeaderboardService _bPlusTree = null!;
    private LeaderboardService _redBlackTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _skipList = new LeaderboardService(new SkipListSortedRankIndex());
        _bPlusTree = new LeaderboardService(new BPlusTreeSortedRankIndex());
        _redBlackTree = new LeaderboardService(new RedBlackTreeSortedRankIndex());

        const int n = 10_000;
        for (var i = 1; i <= n; i++)
        {
            var delta = (decimal)(i % 2001 - 1000);
            _skipList.UpdateScoreAsync(1_000_000 + i, delta).GetAwaiter().GetResult();
            _bPlusTree.UpdateScoreAsync(1_000_000 + i, delta).GetAwaiter().GetResult();
            _redBlackTree.UpdateScoreAsync(1_000_000 + i, delta).GetAwaiter().GetResult();
        }
    }

    [Benchmark(Baseline = true)]
    public void SkipList_RangeTop50() =>
        _skipList.GetByRankRangeAsync(1, 50).GetAwaiter().GetResult();

    [Benchmark]
    public void BPlusTree_RangeTop50() =>
        _bPlusTree.GetByRankRangeAsync(1, 50).GetAwaiter().GetResult();

    [Benchmark]
    public void SkipList_UpdateSingle() =>
        _skipList.UpdateScoreAsync(1_000_001, 1m).GetAwaiter().GetResult();

    [Benchmark]
    public void BPlusTree_UpdateSingle() =>
        _bPlusTree.UpdateScoreAsync(1_000_001, 1m).GetAwaiter().GetResult();

    [Benchmark]
    public void SkipList_Neighborhood() =>
        _skipList.GetNeighborhoodAsync(1_000_500, 2, 3).GetAwaiter().GetResult();

    [Benchmark]
    public void BPlusTree_Neighborhood() =>
        _bPlusTree.GetNeighborhoodAsync(1_000_500, 2, 3).GetAwaiter().GetResult();

    [Benchmark]
    public void RedBlackTree_RangeTop50() =>
        _redBlackTree.GetByRankRangeAsync(1, 50).GetAwaiter().GetResult();

    [Benchmark]
    public void RedBlackTree_UpdateSingle() =>
        _redBlackTree.UpdateScoreAsync(1_000_001, 1m).GetAwaiter().GetResult();

    [Benchmark]
    public void RedBlackTree_Neighborhood() =>
        _redBlackTree.GetNeighborhoodAsync(1_000_500, 2, 3).GetAwaiter().GetResult();
}

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkRunner.Run(typeof(IndexComparisonBenchmarks).Assembly, args: args);
}
