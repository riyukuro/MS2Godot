using Ms2.Formats.World;

namespace Ms2.Formats.Model;

/// <summary>
/// The world transform of an effect dummy (EffectNode::UpdateWorldData), in the client's Z-up world. The client attaches
/// each dummy under its parent node with its local transform from the row (EffectNode::ApplyDummy: translate, scale and the
/// rotation as FromEulerAnglesXYZ of the degrees) and sets the model's root as its rotation target
/// (PCBase::FUN_141674ef0, SetRotationTarget). Each frame, with the parent's world transform P, whose scale counts as 1
/// unless the row applies the local scale:
/// <list type="bullet">
/// <item>applyWorldTransform: the local translate becomes P's inverse rotation times (0, 0, z), so the world translate is
/// P's translate plus z times P's scale straight up the world; the world rotation is the target's world rotation times
/// the row's own; the target's rotation turns (x, y, 0) and only its x and y are added.</item>
/// <item>otherwise: an ordinary child, P times the local transform.</item>
/// </list>
/// Movement, the ground snap and collision of other effect nodes do not apply to these rows.
/// </summary>
public static class DummyWorld
{
    /// <summary>The dummy's world transform under a parent at <paramref name="parentWorld"/>, turning with a target (the model root) at <paramref name="targetRotate"/> (null: none).</summary>
    public static Xform Resolve(EffectDummy d, Xform parentWorld, double[] targetRotate)
    {
        var own = Own(d);
        var t = new Vec3(d.Translate.X, d.Translate.Y, d.Translate.Z);
        var parent = d.ApplyLocalScale ? parentWorld : parentWorld with { S = 1 };
        if (!d.ApplyWorldTransform)
            return parent.Compose(new Xform(t, own, d.Scale));

        var inv = Inverse(parentWorld.R);
        var local = new Xform(Mul(inv, new Vec3(0, 0, t.Z)), own, d.Scale);
        var world = parent.Compose(local);
        world = world with { R = own };
        if (targetRotate != null)
        {
            var off = Mul(targetRotate, new Vec3(t.X, t.Y, 0));
            world = world with { R = Mul(targetRotate, own), T = new Vec3(world.T.X + off.X, world.T.Y + off.Y, world.T.Z) };
        }
        return world;
    }

    /// <summary>
    /// The row's own rotation, EulerXyz of its degrees, kept per row with the degrees it was made from (a row's fields can
    /// be parsed over), so a dummy resolved every frame does not rebuild it. The same values as computing it each time; the
    /// array is shared, so it is read-only like every Xform.R.
    /// </summary>
    static double[] Own(EffectDummy d)
    {
        if (ownCache.TryGetValue(d, out var c) && c.Degrees == d.Rotation) return c.R;
        var r = EulerXyz(d.Rotation.X, d.Rotation.Y, d.Rotation.Z);
        ownCache.AddOrUpdate(d, new OwnRotation(d.Rotation, r));
        return r;
    }

    sealed record OwnRotation(System.Numerics.Vector3 Degrees, double[] R);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectDummy, OwnRotation> ownCache = new();

    /// <summary>
    /// The world transform of the named dummy of <paramref name="rows"/> on a model whose root is at <paramref name="root"/>:
    /// a parent named like the root's node (<paramref name="rootName"/>) is the root, one named like another row is that
    /// row's world transform, any other is looked up with <paramref name="node"/> (a model node's world transform, or null).
    /// Null when the dummy or a parent is missing, as the client does not attach a dummy whose parent it cannot find.
    /// </summary>
    public static Xform? Find(IReadOnlyList<EffectDummy> rows, string name, Xform root, string rootName, Func<string, Xform?> node = null)
        => Find(rows, name, root, rootName, node, 0);

    static Xform? Find(IReadOnlyList<EffectDummy> rows, string name, Xform root, string rootName, Func<string, Xform?> node, int depth)
    {
        var d = EffectDummyTable.Find(rows, name);
        if (d == null || depth > rows.Count) return null;
        Xform? parent = d.ParentNode == rootName ? root
            : EffectDummyTable.Find(rows, d.ParentNode) != null ? Find(rows, d.ParentNode, root, rootName, node, depth + 1)
            : node?.Invoke(d.ParentNode);
        return parent is { } p ? Resolve(d, p, root.R) : null;
    }

    /// <summary>NiMatrix3::FromEulerAnglesXYZ of degrees: X times Y times Z, each Gamebryo's (transposed) rotation.</summary>
    public static double[] EulerXyz(double xDeg, double yDeg, double zDeg)
    {
        const double r = 3.1415927f / 180.0f;     // ApplyDummy: degrees * 3.1415927f / 180
        double cx = Math.Cos(xDeg * r), sx = Math.Sin(xDeg * r), cy = Math.Cos(yDeg * r), sy = Math.Sin(yDeg * r), cz = Math.Cos(zDeg * r), sz = Math.Sin(zDeg * r);
        var x = new double[] { 1, 0, 0, 0, cx, sx, 0, -sx, cx };
        var y = new double[] { cy, 0, -sy, 0, 1, 0, sy, 0, cy };
        var z = new double[] { cz, sz, 0, -sz, cz, 0, 0, 0, 1 };
        return Mul(Mul(x, y), z);
    }

    static double[] Mul(double[] a, double[] b)
    {
        var o = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                o[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return o;
    }

    static Vec3 Mul(double[] m, Vec3 v) => new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[3] * v.X + m[4] * v.Y + m[5] * v.Z, m[6] * v.X + m[7] * v.Y + m[8] * v.Z);

    /// <summary>NiMatrix3::Inverse: the general 3x3 inverse (a singular matrix, which a node rotation never is, gives the identity here).</summary>
    static double[] Inverse(double[] m)
    {
        double a = m[4] * m[8] - m[5] * m[7], b = m[5] * m[6] - m[3] * m[8], c = m[3] * m[7] - m[4] * m[6];
        double det = m[0] * a + m[1] * b + m[2] * c;
        if (Math.Abs(det) < 1e-12) return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double k = 1 / det;
        return new[]
        {
            a * k, (m[2] * m[7] - m[1] * m[8]) * k, (m[1] * m[5] - m[2] * m[4]) * k,
            b * k, (m[0] * m[8] - m[2] * m[6]) * k, (m[2] * m[3] - m[0] * m[5]) * k,
            c * k, (m[1] * m[6] - m[0] * m[7]) * k, (m[0] * m[4] - m[1] * m[3]) * k,
        };
    }
}
