using Godot;

namespace Ms2Godot;

/// <summary>
/// Children by index without GetChildren()'s two arrays: <c>foreach (var c in node.Kids())</c>. The count is read on every
/// step and the order is the tree's, so a child added or removed by the body is seen (GetChildren() is a snapshot: use it
/// where the body frees or reparents the children it walks and every one must still be visited).
/// </summary>
public static class NodeKids
{
    public static Enumerator Kids(this Node node) => new(node);

    public struct Enumerator
    {
        readonly Node node;
        int index;

        public Enumerator(Node node) { this.node = node; index = -1; }

        public Node Current => node.GetChild(index);
        public bool MoveNext() => ++index < node.GetChildCount();
        public Enumerator GetEnumerator() => this;
    }
}
