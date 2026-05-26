namespace Leaderboard.Core;

/// <summary>
/// Indexable skip list: rank lookup and rank-range walks are O(log N) expected.
/// Span maintenance follows the Redis sorted-set skiplist in <c>t_zset.c</c>.
/// </summary>
public sealed class SkipListSortedRankIndex : ISortedRankIndex
{
    private const int MaxLevel = 32;
    private const double Probability = 0.5;

    private readonly Node _head;
    private readonly Random _random = new();

    /// <summary>Number of active levels (>= 1), same meaning as Redis <c>zsl->level</c>.</summary>
    private int _usedLevels = 1;

    private int _count;

    public SkipListSortedRankIndex()
    {
        _head = new Node(RankKey.Sentinel, MaxLevel);
    }

    public int Count => _count;

    public void Insert(in RankKey key)
    {
        var update = new Node?[MaxLevel];
        var rank = new int[MaxLevel];

        var x = _head;
        // Top-down search for predecessors; rank[i] = number of level-0 nodes skipped to reach update[i] from the head.
        for (var i = _usedLevels - 1; i >= 0; i--)
        {
            rank[i] = i == _usedLevels - 1 ? 0 : rank[i + 1];
            while (x.Next![i] != null && RankKey.Compare(x.Next[i]!.Key, key) < 0)
            {
                rank[i] += x.Span[i];
                x = x.Next[i]!;
            }
            update[i] = x;
        }

        var newLevel = RandomLevel();
        if (newLevel > _usedLevels)
        {
            for (var i = _usedLevels; i < newLevel; i++)
            {
                rank[i] = 0;
                update[i] = _head;
                _head.Span[i] = _count;
            }
            _usedLevels = newLevel;
        }

        var newNode = new Node(key, newLevel);
        // Link the new node and split spans so cumulative span along level 0 still matches rank offsets.
        for (var i = 0; i < newLevel; i++)
        {
            newNode.Next![i] = update[i]!.Next[i];
            update[i]!.Next[i] = newNode;

            newNode.Span[i] = update[i]!.Span[i] - (rank[0] - rank[i]);
            update[i]!.Span[i] = rank[0] - rank[i] + 1;
        }

        // Levels above the new node's height: predecessors skip one more bottom-level node.
        for (var i = newLevel; i < _usedLevels; i++)
            update[i]!.Span[i]++;

        _count++;
    }

    public void Remove(in RankKey key)
    {
        var update = new Node?[MaxLevel];
        var x = _head;
        for (var i = _usedLevels - 1; i >= 0; i--)
        {
            while (x.Next![i] != null && RankKey.Compare(x.Next[i]!.Key, key) < 0)
                x = x.Next[i]!;
            update[i] = x;
        }

        x = x.Next![0];
        if (x == null || x.Key.Score != key.Score || x.Key.CustomerId != key.CustomerId)
            return;

        for (var i = 0; i < _usedLevels; i++)
        {
            if (update[i]!.Next[i] == x)
            {
                update[i]!.Span[i] += x.Span[i] - 1;
                update[i]!.Next[i] = x.Next[i];
            }
            else
                update[i]!.Span[i]--;
        }

        while (_usedLevels > 1 && _head.Next![_usedLevels - 1] == null)
            _usedLevels--;

        _count--;
    }

    public bool TryGetRank(in RankKey key, out int rank)
    {
        rank = 0;
        var x = _head;
        for (var i = _usedLevels - 1; i >= 0; i--)
        {
            while (x.Next![i] != null && RankKey.Compare(x.Next[i]!.Key, key) < 0)
            {
                rank += x.Span[i];
                x = x.Next[i]!;
            }
        }

        var f = x.Next![0];
        if (f == null || f.Key.Score != key.Score || f.Key.CustomerId != key.CustomerId)
            return false;

        rank++;
        return true;
    }

    public void FillRangeByRank(int startRank, int endRank, List<RankedCustomer> buffer)
    {
        if (_count == 0 || startRank > _count)
            return;

        var clampedEnd = Math.Min(endRank, _count);
        var node = GetNodeByRank(startRank);
        if (node == null)
            return;

        var r = startRank;
        while (node != null && r <= clampedEnd)
        {
            buffer.Add(new RankedCustomer(node.Key.CustomerId, node.Key.Score, r));
            node = node.Next![0];
            r++;
        }
    }

    public bool TryFillNeighborhood(in RankKey key, int higher, int lower, List<RankedCustomer> buffer)
    {
        buffer.Clear();
        if (!TryGetRank(in key, out var myRank))
            return false;

        var startRank = Math.Max(1, myRank - higher);
        var endRank = Math.Min(_count, myRank + lower);
        FillRangeByRank(startRank, endRank, buffer);
        return true;
    }

    /// <summary>1-based rank lookup using spans.</summary>
    private Node? GetNodeByRank(int rank)
    {
        if (rank < 1 || rank > _count)
            return null;

        uint traversed = 0;
        var x = _head;
        // Greedy descent: never cross the target rank so we land on the predecessor of rank.
        for (var i = _usedLevels - 1; i >= 0; i--)
        {
            while (x.Next![i] != null && traversed + (uint)x.Span[i] < (uint)rank)
            {
                traversed += (uint)x.Span[i];
                x = x.Next[i]!;
            }
        }

        return x.Next![0];
    }

    /// <summary>Random height in [1, MaxLevel].</summary>
    private int RandomLevel()
    {
        var lvl = 1;
        while (_random.NextDouble() < Probability && lvl < MaxLevel)
            lvl++;
        return lvl;
    }

    private sealed class Node
    {
        public Node(in RankKey key, int levels)
        {
            Key = key;
            Next = new Node?[levels];
            Span = new int[levels];
        }

        public RankKey Key { get; }
        public Node?[] Next { get; }
        public int[] Span { get; }
    }
}
