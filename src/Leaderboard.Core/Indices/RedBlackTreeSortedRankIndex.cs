namespace Leaderboard.Core;

/// <summary>
/// Left-leaning red-black BST with subtree sizes (order-statistic tree).
/// Worst-case O(log n) height; insertion/deletion adapted from Algs4 <c>RedBlackBST.java</c>.
/// </summary>
public sealed class RedBlackTreeSortedRankIndex : ISortedRankIndex
{
    private const bool Red = true;
    private const bool Black = false;

    private Node? _root;

    public int Count => GetSize(_root);

    public void Insert(in RankKey key)
    {
        _root = Put(_root, in key);
        if (_root is not null)
            _root.IsRed = Black;
    }

    public void Remove(in RankKey key)
    {
        if (!Contains(_root, in key))
            return;

        if (!IsRed(_root!.Left) && !IsRed(_root.Right))
            _root.IsRed = Red;

        _root = Delete(_root, in key);
        if (_root is not null)
            _root.IsRed = Black;
    }

    public bool TryGetRank(in RankKey key, out int rank)
    {
        var rankBeforeSubtree = 0;
        var current = _root;

        while (current is not null)
        {
            var compare = RankKey.Compare(in key, current.Key);
            if (compare < 0)
            {
                current = current.Left;
            }
            else if (compare > 0)
            {
                rankBeforeSubtree += GetSize(current.Left) + 1;
                current = current.Right;
            }
            else
            {
                rank = rankBeforeSubtree + GetSize(current.Left) + 1;
                return true;
            }
        }

        rank = 0;
        return false;
    }

    public void FillRangeByRank(int startRank, int endRank, List<RankedCustomer> buffer)
    {
        if (startRank > endRank || startRank > Count)
            return;

        var boundedEnd = Math.Min(endRank, Count);
        AddRangeByRank(_root, rankBeforeSubtree: 0, startRank, boundedEnd, buffer);
    }

    public bool TryFillNeighborhood(in RankKey key, int higher, int lower, List<RankedCustomer> buffer)
    {
        buffer.Clear();
        if (!TryGetRank(in key, out var myRank))
            return false;

        var startRank = Math.Max(1, myRank - higher);
        var endRank = Math.Min(Count, myRank + lower);
        FillRangeByRank(startRank, endRank, buffer);
        return true;
    }

    private static bool Contains(Node? h, in RankKey key)
    {
        while (h is not null)
        {
            var c = RankKey.Compare(in key, h.Key);
            if (c < 0)
                h = h.Left;
            else if (c > 0)
                h = h.Right;
            else
                return true;
        }

        return false;
    }

    private static Node? Put(Node? h, in RankKey key)
    {
        if (h is null)
            return new Node(key, Red);

        var cmp = RankKey.Compare(in key, h.Key);
        if (cmp < 0)
            h.Left = Put(h.Left, in key);
        else if (cmp > 0)
            h.Right = Put(h.Right, in key);
        else
            return h;

        if (IsRed(h.Right) && !IsRed(h.Left))
            h = RotateLeft(h);

        if (IsRed(h.Left) && h.Left is { Left: { IsRed: true } })
            h = RotateRight(h);

        if (IsRed(h.Left) && IsRed(h.Right))
            FlipColors(h);

        UpdateSize(h);
        return h;
    }

    private static Node? Delete(Node? h, in RankKey key)
    {
        if (RankKey.Compare(in key, h!.Key) < 0)
        {
            if (!IsRed(h.Left) && !IsRed(h.Left?.Left))
                h = MoveRedLeft(h);

            h.Left = Delete(h.Left, in key);
        }
        else
        {
            if (IsRed(h.Left))
                h = RotateRight(h);

            if (RankKey.Compare(in key, h.Key) == 0 && h.Right is null)
                return null;

            if (!IsRed(h.Right) && !IsRed(h.Right?.Left))
                h = MoveRedRight(h);

            if (RankKey.Compare(in key, h.Key) == 0)
            {
                var minRight = Min(h.Right!);
                h.Key = minRight.Key;
                h.Right = DeleteMin(h.Right);
            }
            else
            {
                h.Right = Delete(h.Right, in key);
            }
        }

        return Balance(h);
    }

    private static Node? DeleteMin(Node? h)
    {
        if (h!.Left is null)
            return null;

        if (!IsRed(h.Left) && !IsRed(h.Left.Left))
            h = MoveRedLeft(h);

        h.Left = DeleteMin(h.Left);
        return Balance(h);
    }

    private static Node Min(Node h)
    {
        while (h.Left is not null)
            h = h.Left;
        return h;
    }

    private static void AddRangeByRank(
        Node? node,
        int rankBeforeSubtree,
        int start,
        int end,
        List<RankedCustomer> results)
    {
        if (node is null)
            return;

        var leftSize = GetSize(node.Left);
        var nodeRank = rankBeforeSubtree + leftSize + 1;

        if (start <= rankBeforeSubtree + leftSize)
            AddRangeByRank(node.Left, rankBeforeSubtree, start, end, results);

        if (start <= nodeRank && nodeRank <= end)
            results.Add(new RankedCustomer(node.Key.CustomerId, node.Key.Score, nodeRank));

        if (nodeRank < end)
            AddRangeByRank(node.Right, nodeRank, start, end, results);
    }

    private static Node RotateRight(Node h)
    {
        var x = h.Left!;
        h.Left = x.Right;
        x.Right = h;
        x.IsRed = h.IsRed;
        h.IsRed = Red;
        x.Size = h.Size;
        UpdateSize(h);
        return x;
    }

    private static Node RotateLeft(Node h)
    {
        var x = h.Right!;
        h.Right = x.Left;
        x.Left = h;
        x.IsRed = h.IsRed;
        h.IsRed = Red;
        x.Size = h.Size;
        UpdateSize(h);
        return x;
    }

    private static void FlipColors(Node h)
    {
        h.IsRed = !h.IsRed;
        if (h.Left is not null)
            h.Left.IsRed = !h.Left.IsRed;
        if (h.Right is not null)
            h.Right.IsRed = !h.Right.IsRed;
    }

    private static Node MoveRedLeft(Node h)
    {
        FlipColors(h);
        if (IsRed(h.Right?.Left))
        {
            h.Right = RotateRight(h.Right!);
            h = RotateLeft(h);
            FlipColors(h);
        }

        return h;
    }

    private static Node MoveRedRight(Node h)
    {
        FlipColors(h);
        if (IsRed(h.Left?.Left))
        {
            h = RotateRight(h);
            FlipColors(h);
        }

        return h;
    }

    private static Node Balance(Node h)
    {
        if (IsRed(h.Right) && !IsRed(h.Left))
            h = RotateLeft(h);

        if (IsRed(h.Left) && h.Left is { Left: { IsRed: true } })
            h = RotateRight(h);

        if (IsRed(h.Left) && IsRed(h.Right))
            FlipColors(h);

        UpdateSize(h);
        return h;
    }

    private static bool IsRed(Node? x) => x is { IsRed: true };

    private static int GetSize(Node? node) => node?.Size ?? 0;

    private static void UpdateSize(Node node) =>
        node.Size = GetSize(node.Left) + GetSize(node.Right) + 1;

    private sealed class Node(RankKey key, bool isRed)
    {
        public RankKey Key { get; set; } = key;

        public bool IsRed { get; set; } = isRed;

        public int Size { get; set; } = 1;

        public Node? Left { get; set; }

        public Node? Right { get; set; }
    }
}
