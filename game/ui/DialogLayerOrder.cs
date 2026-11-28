using System;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The tree order of a parent's dialogs follows their CommonDepth layers, as the client's one stage does: each layer is a
/// sprite of the depth root (CommonDepth.addLayer, CommonDepth.as:143-150), so every movie of a higher layer is drawn and hit
/// over every movie of a lower one, and a click only reorders inside its own layer (CommonDepth.topmostLayer's setChildIndex,
/// CommonDepth.as:446). Godot draws a parent's children in tree order, so the dialogs of one parent are kept in runs by layer:
/// a new dialog goes after the others of its layer and before the first one of a higher layer, and a raise moves it to the end
/// of its own run. Children that are not dialogs (layer below 0: the HUD slots, the field) keep their places.
/// </summary>
public static class DialogLayerOrder
{
    /// <summary>
    /// <paramref name="child"/> was just added to <paramref name="parent"/> (at its end, or wherever the caller put it): it
    /// moves before the first earlier sibling of a higher layer, so the parent stays sorted by layer (msLoader.loadSwf adds a
    /// loader at its layer's end).
    /// </summary>
    public static void Added(Node parent, Node child, Func<Node, int> layerOf)
    {
        int layer = layerOf(child);
        if (layer < 0) return;
        int at = child.GetIndex();
        for (int i = 0; i < at; i++)
            if (layerOf(parent.GetChild(i)) > layer) { parent.MoveChild(child, i); return; }
    }

    /// <summary>
    /// <paramref name="child"/> goes to the end of its layer's run: after the last later sibling of the same layer, never past
    /// one of a higher layer.
    /// </summary>
    public static void ToLayerEnd(Node parent, Node child, Func<Node, int> layerOf)
    {
        int layer = layerOf(child);
        if (layer < 0) return;
        int at = child.GetIndex(), last = -1;
        for (int i = at + 1, n = parent.GetChildCount(); i < n; i++)
        {
            int l = layerOf(parent.GetChild(i));
            if (l > layer) break;
            if (l == layer) last = i;
        }
        if (last > at) parent.MoveChild(child, last);
    }
}
