using Godot;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The game's one set of <see cref="CommonDepth"/> layers. The client loads every movie into a layer sprite of one shell
/// movie; here each movie has its own canvas layer, which is drawn only while its CommonDepth layer is visible.
/// </summary>
public static class MovieLayerHost
{
    public static readonly MovieLayers Layers = new();

    /// <summary>Draws <paramref name="node"/> only while CommonDepth layer <paramref name="layer"/> is visible, from the
    /// time it enters the tree until it leaves it.</summary>
    public static void Bind(CanvasLayer node, int layer)
    {
        void Apply(int l, bool on) { if (l == layer) node.Visible = on; }
        node.TreeEntered += () => { node.Visible = Layers.IsVisible(layer); Layers.Changed += Apply; };
        node.TreeExited += () => Layers.Changed -= Apply;
    }
}
