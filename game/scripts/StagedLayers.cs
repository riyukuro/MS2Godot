using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Keeps the canvas layers below a node hidden while the node is staged (a field built behind the loading screen). A
/// Node3D's visibility does not reach canvas layers, so each one is hidden by itself. The layers already below the node
/// are found once, at the first <see cref="Hide"/>; the ones added later are taken from the tree's node_added as they
/// enter, so a frame boundary checks only the layers, not the whole subtree. <see cref="Restore"/> shows the layers it
/// hid again and stops listening.
/// </summary>
public sealed class StagedLayers
{
    readonly Node root;
    readonly List<CanvasLayer> below = new(), hidden = new();
    SceneTree tree;

    public StagedLayers(Node root) => this.root = root;

    /// <summary>The layers seen below the root so far (for the headless check).</summary>
    public int Tracked => below.Count;

    /// <summary>Hides every visible canvas layer below the root.</summary>
    public void Hide()
    {
        if (tree == null)
        {
            tree = root.GetTree();
            foreach (var n in root.FindChildren("*", nameof(CanvasLayer), true, false))
                if (n is CanvasLayer l) below.Add(l);
            tree.NodeAdded += Added;
        }
        for (int i = 0; i < below.Count; i++)
        {
            var l = below[i];
            if (!GodotObject.IsInstanceValid(l)) continue;
            if (l.Visible && root.IsAncestorOf(l)) { l.Visible = false; hidden.Add(l); }
        }
    }

    void Added(Node n)
    {
        if (n is CanvasLayer l && root.IsAncestorOf(l)) below.Add(l);
    }

    /// <summary>Shows the layers it hid and stops listening; safe to call more than once.</summary>
    public void Restore()
    {
        Stop();
        foreach (var l in hidden) if (GodotObject.IsInstanceValid(l)) l.Visible = true;
        hidden.Clear();
    }

    /// <summary>Stops listening to the tree without showing anything (the root is leaving the tree).</summary>
    public void Stop()
    {
        if (tree != null && GodotObject.IsInstanceValid(tree)) tree.NodeAdded -= Added;
        tree = null;
        below.Clear();
    }
}
