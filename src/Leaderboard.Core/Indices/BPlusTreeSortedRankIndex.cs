namespace Leaderboard.Core;

/// <summary>
/// B+ tree index: leaves store sorted keys with a doubly linked list; internal nodes store child pointers,
/// separator keys (minimum key of <c>Children[i+1]</c>), and subtree sizes.
/// </summary>
public sealed class BPlusTreeSortedRankIndex : ISortedRankIndex
{
    private const int MaxLeaf = 64;
    private const int MinLeaf = MaxLeaf / 2;
    private const int MaxChildren = 64;
    private const int MinChildren = MaxChildren / 2;

    private Node? _root;

    public int Count => _root?.TotalCount ?? 0;

    public void Insert(in RankKey key)
    {
        if (_root is null)
        {
            var lf = new Leaf();
            lf.Keys.Add(key);
            lf.RecountLocal();
            _root = lf;
            return;
        }

        var leaf = FindLeaf(_root, in key);
        var pos = LowerBound(leaf.Keys, in key);
        if (pos < leaf.Keys.Count && leaf.Keys[pos].CustomerId == key.CustomerId && leaf.Keys[pos].Score == key.Score)
            return;

        leaf.Keys.Insert(pos, key);
        leaf.RecountLocal();
        BubbleRecount(leaf);

        if (leaf.Keys.Count > MaxLeaf)
            SplitLeaf(leaf);
    }

    public void Remove(in RankKey key)
    {
        if (_root is null)
            return;

        var leaf = FindLeaf(_root, in key);
        var pos = LowerBound(leaf.Keys, in key);
        if (pos >= leaf.Keys.Count || leaf.Keys[pos].CustomerId != key.CustomerId || leaf.Keys[pos].Score != key.Score)
            return;

        leaf.Keys.RemoveAt(pos);
        leaf.RecountLocal();
        BubbleRecount(leaf);

        if (leaf.Keys.Count == 0)
        {
            DetachLeaf(leaf);
            return;
        }

        if (leaf.Parent is not null && leaf.Keys.Count < MinLeaf)
            BalanceLeaf(leaf);
    }

    public bool TryGetRank(in RankKey key, out int rank)
    {
        rank = 0;
        if (_root is null)
            return false;

        var before = 0;
        var n = _root;
        while (n is Internal inn)
        {
            var ci = ChildIndex(inn, in key);
            for (var j = 0; j < ci; j++)
                before += inn.Children[j].TotalCount;
            n = inn.Children[ci];
        }

        var lf = (Leaf)n;
        var ix = LowerBound(lf.Keys, in key);
        if (ix >= lf.Keys.Count || lf.Keys[ix].CustomerId != key.CustomerId || lf.Keys[ix].Score != key.Score)
            return false;

        rank = before + ix + 1;
        return true;
    }

    public void FillRangeByRank(int startRank, int endRank, List<RankedCustomer> buffer)
    {
        if (_root is null || startRank > Count)
            return;

        var end = Math.Min(endRank, Count);
        var leaf = WalkToRank(startRank, out var idx);
        if (leaf is null)
            return;

        var r = startRank;
        while (leaf is not null && r <= end)
        {
            for (; idx < leaf.Keys.Count && r <= end; idx++, r++)
            {
                var k = leaf.Keys[idx];
                buffer.Add(new RankedCustomer(k.CustomerId, k.Score, r));
            }
            leaf = leaf.Next;
            idx = 0;
        }
    }

    public bool TryFillNeighborhood(in RankKey key, int higher, int lower, List<RankedCustomer> buffer)
    {
        buffer.Clear();
        if (!TryGetRank(in key, out var my))
            return false;

        var s = Math.Max(1, my - higher);
        var e = Math.Min(Count, my + lower);
        FillRangeByRank(s, e, buffer);
        return true;
    }

    // --- Leaf lookup: internal nodes route by separator keys ---

    /// <summary><c>Separators[i]</c> is the minimum key in subtree <c>Children[i+1]</c>; all keys in <c>Children[0]</c> are &lt; <c>Separators[0]</c>.</summary>
    private static int ChildIndex(Internal inn, in RankKey key)
    {
        var lo = 0;
        var hi = inn.Separators.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (RankKey.Compare(key, inn.Separators[mid]) < 0)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }

    private static Leaf FindLeaf(Node n, in RankKey key)
    {
        while (n is Internal inn)
            n = inn.Children[ChildIndex(inn, in key)];
        return (Leaf)n;
    }

    private Leaf? WalkToRank(int rank, out int indexInLeaf)
    {
        indexInLeaf = 0;
        if (_root is null || rank < 1 || rank > _root.TotalCount)
            return null;

        // Use cached subtree sizes to descend to the leaf that contains the target rank.
        var rest = rank;
        var n = _root;
        while (n is Internal inn)
        {
            foreach (var ch in inn.Children)
            {
                if (rest <= ch.TotalCount)
                {
                    n = ch;
                    goto next;
                }
                rest -= ch.TotalCount;
            }
            return null;
        next:;
        }

        var lf = (Leaf)n;
        indexInLeaf = rest - 1;
        return lf;
    }

    private static int LowerBound(List<RankKey> keys, in RankKey k)
    {
        var lo = 0;
        var hi = keys.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (RankKey.Compare(keys[mid], k) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static void BubbleRecount(Node n)
    {
        for (var p = n.Parent; p is not null; p = p.Parent)
            p.RecountLocal();
    }

    private static RankKey FirstKey(Node n) =>
        n is Leaf lf ? lf.Keys[0] : FirstKey(((Internal)n).Children[0]);

    // --- Leaf split ---

    private void SplitLeaf(Leaf leaf)
    {
        var mid = leaf.Keys.Count / 2;
        var right = new Leaf();
        right.Keys.AddRange(leaf.Keys.GetRange(mid, leaf.Keys.Count - mid));
        leaf.Keys.RemoveRange(mid, leaf.Keys.Count - mid);
        leaf.RecountLocal();
        right.RecountLocal();

        right.Next = leaf.Next;
        if (leaf.Next is not null)
            leaf.Next.Prev = right;
        right.Prev = leaf;
        leaf.Next = right;

        if (leaf.Parent is null)
        {
            var root = new Internal();
            root.Children.Add(leaf);
            root.Children.Add(right);
            root.Separators.Add(FirstKey(right));
            RefreshChildIndices(root);
            _root = root;
            root.RecountLocal();
            return;
        }

        InsertInternalChild(leaf.Parent, leaf.IndexInParent + 1, right);
    }

    private void InsertInternalChild(Internal parent, int insertAt, Node newChild)
    {
        parent.Children.Insert(insertAt, newChild);
        newChild.Parent = parent;
        RefreshChildIndices(parent);
        parent.Separators.Clear();
        for (var i = 0; i < parent.Children.Count - 1; i++)
            parent.Separators.Add(FirstKey(parent.Children[i + 1]));
        parent.RecountLocal();
        BubbleRecount(parent);

        if (parent.Children.Count > MaxChildren)
            SplitInternal(parent);
    }

    private void SplitInternal(Internal node)
    {
        var mid = node.Children.Count / 2;
        var right = new Internal();
        // Move children from node to right - need to ensure parent pointers are updated
        right.Children.AddRange(node.Children.GetRange(mid, node.Children.Count - mid));
        node.Children.RemoveRange(mid, node.Children.Count - mid);
        // Refresh indices: node first, then right - this also fixes parent pointers of moved children
        RefreshChildIndices(node);
        RefreshChildIndices(right);
        // Additional safety: ensure all children in 'right' have correct parent
        // (RefreshChildIndices should handle this, but we double-check for correctness)
        foreach (var child in right.Children)
        {
            child.Parent = right;
        }
        node.Separators.Clear();
        for (var i = 0; i < node.Children.Count - 1; i++)
            node.Separators.Add(FirstKey(node.Children[i + 1]));
        right.Separators.Clear();
        for (var i = 0; i < right.Children.Count - 1; i++)
            right.Separators.Add(FirstKey(right.Children[i + 1]));
        node.RecountLocal();
        right.RecountLocal();

        if (node.Parent is null)
        {
            var nr = new Internal();
            nr.Children.Add(node);
            nr.Children.Add(right);
            nr.Separators.Add(FirstKey(right));
            RefreshChildIndices(nr);
            _root = nr;
            nr.RecountLocal();
            return;
        }

        InsertInternalChild(node.Parent, node.IndexInParent + 1, right);
    }

    private static void RefreshChildIndices(Internal inn)
    {
        for (var i = 0; i < inn.Children.Count; i++)
        {
            inn.Children[i].Parent = inn;
            inn.Children[i].IndexInParent = i;
        }
    }

    // --- Leaf removal / rebalance ---

    private void DetachLeaf(Leaf leaf)
    {
        if (leaf.Prev is not null)
            leaf.Prev.Next = leaf.Next;
        if (leaf.Next is not null)
            leaf.Next.Prev = leaf.Prev;

        if (leaf.Parent is null)
        {
            _root = null;
            return;
        }

        var p = leaf.Parent;
        p.Children.RemoveAt(leaf.IndexInParent);
        if (p.Children.Count == 0)
        {
            _root = null;
            return;
        }

        RefreshChildIndices(p);
        p.Separators.Clear();
        for (var i = 0; i < p.Children.Count - 1; i++)
            p.Separators.Add(FirstKey(p.Children[i + 1]));
        p.RecountLocal();
        BubbleRecount(p);

        if (p.Children.Count == 1 && p.Parent is null)
        {
            _root = p.Children[0];
            _root.Parent = null;
            return;
        }

        if (p.Parent is not null && p.Children.Count < MinChildren)
            BalanceInternal(p);
    }

    private void BalanceLeaf(Leaf leaf)
    {
        var p = leaf.Parent!;
        var ix = leaf.IndexInParent;
        var L = ix > 0 ? (Leaf)p.Children[ix - 1] : null;
        var R = ix + 1 < p.Children.Count ? (Leaf)p.Children[ix + 1] : null;

        if (L is not null && L.Keys.Count + leaf.Keys.Count <= MaxLeaf)
        {
            L.Keys.AddRange(leaf.Keys);
            L.RecountLocal();
            DetachLeaf(leaf);
            return;
        }

        if (R is not null && leaf.Keys.Count + R.Keys.Count <= MaxLeaf)
        {
            leaf.Keys.AddRange(R.Keys);
            leaf.RecountLocal();
            DetachLeaf(R);
            return;
        }

        if (L is not null && L.Keys.Count > MinLeaf)
        {
            leaf.Keys.Insert(0, L.Keys[^1]);
            L.Keys.RemoveAt(L.Keys.Count - 1);
            leaf.RecountLocal();
            L.RecountLocal();
            p.Separators[ix - 1] = FirstKey(leaf);
            BubbleRecount(leaf);
            return;
        }

        if (R is not null && R.Keys.Count > MinLeaf)
        {
            leaf.Keys.Add(R.Keys[0]);
            R.Keys.RemoveAt(0);
            leaf.RecountLocal();
            R.RecountLocal();
            if (ix < p.Separators.Count)
                p.Separators[ix] = FirstKey(R);
            BubbleRecount(leaf);
        }
    }

    private void BalanceInternal(Internal node)
    {
        if (node.Parent is null)
            return;

        var p = node.Parent;
        var ix = node.IndexInParent;
        var L = ix > 0 ? (Internal)p.Children[ix - 1] : null;
        var R = ix + 1 < p.Children.Count ? (Internal)p.Children[ix + 1] : null;

        if (L is not null && L.Children.Count + node.Children.Count <= MaxChildren)
        {
            node.Children.InsertRange(0, L.Children);
            L.Children.Clear();
            RefreshChildIndices(node);
            node.Separators.Clear();
            for (var i = 0; i < node.Children.Count - 1; i++)
                node.Separators.Add(FirstKey(node.Children[i + 1]));
            node.RecountLocal();
            p.Children.RemoveAt(ix - 1);
            RefreshChildIndices(p);
            p.Separators.Clear();
            for (var i = 0; i < p.Children.Count - 1; i++)
                p.Separators.Add(FirstKey(p.Children[i + 1]));
            p.RecountLocal();
            BubbleRecount(p);
            CollapseRootIfSingle(p);
            return;
        }

        if (R is not null && node.Children.Count + R.Children.Count <= MaxChildren)
        {
            node.Children.AddRange(R.Children);
            R.Children.Clear();
            RefreshChildIndices(node);
            node.Separators.Clear();
            for (var i = 0; i < node.Children.Count - 1; i++)
                node.Separators.Add(FirstKey(node.Children[i + 1]));
            node.RecountLocal();
            p.Children.RemoveAt(ix + 1);
            RefreshChildIndices(p);
            p.Separators.Clear();
            for (var i = 0; i < p.Children.Count - 1; i++)
                p.Separators.Add(FirstKey(p.Children[i + 1]));
            p.RecountLocal();
            BubbleRecount(p);
            CollapseRootIfSingle(p);
        }
    }

    private void CollapseRootIfSingle(Internal p)
    {
        if (p.Parent is null && p.Children.Count == 1)
        {
            _root = p.Children[0];
            _root.Parent = null;
        }
    }

    private abstract class Node
    {
        public Internal? Parent;
        public int IndexInParent;
        public int TotalCount;

        public abstract void RecountLocal();
    }

    private sealed class Leaf : Node
    {
        public readonly List<RankKey> Keys = new();
        public Leaf? Next;
        public Leaf? Prev;

        public override void RecountLocal() => TotalCount = Keys.Count;
    }

    private sealed class Internal : Node
    {
        public readonly List<Node> Children = new();
        public readonly List<RankKey> Separators = new();

        public override void RecountLocal()
        {
            TotalCount = 0;
            foreach (var c in Children)
                TotalCount += c.TotalCount;
        }
    }
}
