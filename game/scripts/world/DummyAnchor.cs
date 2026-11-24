using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// Effect dummies on a built model (ModelBuilder.Build: the "Model" node is the model's root, turned from the file's Z-up to
/// Y-up): the model root's transform in the client's Z-up world, and a dummy's world point (DummyWorld) back in Godot.
/// </summary>
public static class DummyAnchor
{
    /// <summary>
    /// Undoes the Model node's turn: Godot (Y-up) world to the client's Z-up world, (x, y, z) to (x, -z, y), the inverse of
    /// FieldBuilder.ToGodotPoint. Written out, as a turn by an angle of pi / 2 leaves cos = -4.4e-8, which moves a point
    /// 3000 up by 1e-4 sideways.
    /// </summary>
    static readonly Transform3D ToClient = new(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)), Vector3.Zero);

    /// <summary>A node's global transform as a client-world transform (rotation, uniform scale, translate).</summary>
    public static Xform ClientWorld(Node3D node) => ClientWorld(node.GlobalTransform);

    /// <summary>A Godot world transform (a node's or a bone's) as a client-world transform.</summary>
    public static Xform ClientWorld(in Transform3D global)
    {
        var t = ToClient * global;
        var b = t.Basis;
        double s = b.Column0.Length();
        if (s == 0) s = 1;
        Vector3 c0 = b.Column0 / (float)s, c1 = b.Column1 / (float)s, c2 = b.Column2 / (float)s;
        var r = new double[] { c0.X, c1.X, c2.X, c0.Y, c1.Y, c2.Y, c0.Z, c1.Z, c2.Z };
        return new Xform(new Vec3(t.Origin.X, t.Origin.Y, t.Origin.Z), r, s);
    }

    /// <summary>A client-world transform back in Godot's world, the inverse of <see cref="ClientWorld(in Transform3D)"/>.</summary>
    public static Transform3D GodotWorld(in Xform x)
    {
        var r = x.R; float s = (float)x.S;
        var b = new Basis(new Vector3((float)r[0], (float)r[3], (float)r[6]) * s, new Vector3((float)r[1], (float)r[4], (float)r[7]) * s,
            new Vector3((float)r[2], (float)r[5], (float)r[8]) * s);
        return ToClient.AffineInverse() * new Transform3D(b, new Vector3((float)x.T.X, (float)x.T.Y, (float)x.T.Z));
    }

    /// <summary>
    /// The Godot world point of the dummy <paramref name="name"/> of <paramref name="rows"/> on the model whose root node is
    /// <paramref name="model"/> (named <paramref name="rootName"/> in its file); null when the dummy or its parent is missing.
    /// Parents other than the root and the rows themselves are not looked up.
    /// </summary>
    public static Vector3? Point(IReadOnlyList<EffectDummy> rows, string name, Node3D model, string rootName)
    {
        if (model == null || rows == null) return null;
        // an npc that stands still is asked for the same point by its name tag, HP bar and emoticon every frame
        var g = model.GlobalTransform;
        var b = g.Basis;
        var at = new DummyMemo.Key(b.Column0.X, b.Column0.Y, b.Column0.Z, b.Column1.X, b.Column1.Y, b.Column1.Z, b.Column2.X, b.Column2.Y, b.Column2.Z, g.Origin.X, g.Origin.Y, g.Origin.Z);
        var memo = memos.GetValue(model, _ => new DummyMemo());
        if (!memo.TryGet(rows, name, rootName, at, out var r))
        {
            if (DummyWorld.Find(rows, name, ClientWorld(model), rootName) is { } w)
            {
                var v = FieldBuilder.ToGodotPoint(w.T);
                r = new DummyMemo.Point(v.X, v.Y, v.Z, true);
            }
            else r = default;
            memo.Set(rows, name, rootName, at, r);
        }
        return r.Has ? new Vector3(r.X, r.Y, r.Z) : null;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, DummyMemo> memos = new();
}
