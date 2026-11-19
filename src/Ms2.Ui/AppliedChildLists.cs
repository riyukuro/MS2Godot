using System.Collections.Generic;

namespace Ms2.Ui;

/// <summary>
/// The child order a view last gave each parent node, and the lists it builds the next order in. A sync builds the wanted
/// order of every container it walks, and for most of them nothing changed since the last sync: those lists come from a pool
/// and go back to it, and only a parent whose order changed keeps a list of its own.
/// </summary>
public sealed class AppliedChildLists<TKey, TNode> where TKey : class where TNode : class
{
    readonly Dictionary<TKey, List<TNode>> applied = new();
    readonly Stack<List<TNode>> pool = new();

    /// <summary>An empty list to build an order in; give it back with <see cref="Return"/>.</summary>
    public List<TNode> Rent() => pool.Count > 0 ? pool.Pop() : new List<TNode>();

    public void Return(List<TNode> list)
    {
        list.Clear();
        pool.Push(list);
    }

    /// <summary>True when <paramref name="parent"/> was last given exactly <paramref name="want"/> (same nodes, same order).</summary>
    public bool IsApplied(TKey parent, List<TNode> want)
    {
        if (!applied.TryGetValue(parent, out var last) || last.Count != want.Count) return false;
        for (int i = 0; i < want.Count; i++) if (!ReferenceEquals(last[i], want[i])) return false;
        return true;
    }

    /// <summary>Remembers <paramref name="want"/> as the parent's order (a copy: the caller's list is reused).</summary>
    public void Set(TKey parent, List<TNode> want) => applied[parent] = new List<TNode>(want);

    public void Remove(TKey parent) => applied.Remove(parent);
}
