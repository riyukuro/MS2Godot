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
        var parent = Pose.Of(parentWorld);
        Rot9 target = default;
        if (targetRotate != null) target = Rot9.Of(targetRotate);
        Resolve(d, parent, target, targetRotate != null, out var world);
        return world.ToXform();
    }

    /// <summary>
    /// A transform by value (<see cref="Xform"/>'s translate, row-major rotation and uniform scale), so a dummy looked up
    /// every frame while its model moves makes no arrays (<see cref="FindPoint"/>). The same operations in the same order
    /// as <see cref="Xform"/>'s, so the results are the same doubles.
    /// </summary>
    public struct Pose
    {
        public Vec3 T;
        public Rot9 R;
        public double S;

        public static Pose Of(in Xform x) => new() { T = x.T, R = Rot9.Of(x.R), S = x.S };
        public readonly Xform ToXform() => new(T, R.ToArray(), S);

        /// <summary><see cref="Xform.Apply"/>.</summary>
        public readonly Vec3 Apply(Vec3 p) => new(
            S * (R[0] * p.X + R[1] * p.Y + R[2] * p.Z) + T.X,
            S * (R[3] * p.X + R[4] * p.Y + R[5] * p.Z) + T.Y,
            S * (R[6] * p.X + R[7] * p.Y + R[8] * p.Z) + T.Z);

        /// <summary><see cref="Xform.Compose"/>: this * child (apply child first).</summary>
        public readonly Pose Compose(in Pose c) => new() { T = Apply(c.T), R = Mul(R, c.R), S = S * c.S };
    }

    /// <summary>A 3x3 rotation by value, row-major as <see cref="Xform.R"/>.</summary>
    [System.Runtime.CompilerServices.InlineArray(9)]
    public struct Rot9
    {
        double e0;

        public static Rot9 Of(double[] r)
        {
            Rot9 o = default;
            for (int i = 0; i < 9; i++) o[i] = r[i];
            return o;
        }

        public readonly double[] ToArray()
        {
            var a = new double[9];
            for (int i = 0; i < 9; i++) a[i] = this[i];
            return a;
        }
    }

    static void Resolve(EffectDummy d, in Pose parentWorld, in Rot9 target, bool hasTarget, out Pose world)
    {
        var own = Rot9.Of(Own(d));
        var t = new Vec3(d.Translate.X, d.Translate.Y, d.Translate.Z);
        var parent = parentWorld;
        if (!d.ApplyLocalScale) parent.S = 1;
        if (!d.ApplyWorldTransform)
        {
            world = parent.Compose(new Pose { T = t, R = own, S = d.Scale });
            return;
        }

        var inv = Inverse(parentWorld.R);
        var local = new Pose { T = Mul(inv, new Vec3(0, 0, t.Z)), R = own, S = d.Scale };
        world = parent.Compose(local);
        world.R = own;
        if (hasTarget)
        {
            var off = Mul(target, new Vec3(t.X, t.Y, 0));
            world.R = Mul(target, own);
            world.T = new Vec3(world.T.X + off.X, world.T.Y + off.Y, world.T.Z);
        }
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
        => Find(rows, name, Pose.Of(root), rootName, node, 0, out var w) ? w.ToXform() : null;

    /// <summary>
    /// The world point of the named dummy of <paramref name="rows"/> on a model whose root is at <paramref name="root"/>
    /// (<see cref="Find(IReadOnlyList{EffectDummy}, string, Xform, string, Func{string, Xform?})"/>'s translate, with no
    /// node lookup): false when the dummy or a parent is missing. Allocates nothing.
    /// </summary>
    public static bool FindPoint(IReadOnlyList<EffectDummy> rows, string name, in Pose root, string rootName, out Vec3 point)
    {
        bool found = Find(rows, name, root, rootName, null, 0, out var w);
        point = w.T;
        return found;
    }

    static bool Find(IReadOnlyList<EffectDummy> rows, string name, in Pose root, string rootName, Func<string, Xform?> node, int depth,
        out Pose world)
    {
        world = default;
        var d = EffectDummyTable.Find(rows, name);
        if (d == null || depth > rows.Count) return false;
        Pose parent;
        if (d.ParentNode == rootName) parent = root;
        else if (EffectDummyTable.Find(rows, d.ParentNode) != null)
        {
            if (!Find(rows, d.ParentNode, root, rootName, node, depth + 1, out parent)) return false;
        }
        else if (node?.Invoke(d.ParentNode) is { } n) parent = Pose.Of(n);
        else return false;
        Resolve(d, parent, root.R, true, out world);
        return true;
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

    static Rot9 Mul(in Rot9 a, in Rot9 b)
    {
        Rot9 o = default;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                o[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return o;
    }

    static Vec3 Mul(in Rot9 m, Vec3 v) => new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[3] * v.X + m[4] * v.Y + m[5] * v.Z, m[6] * v.X + m[7] * v.Y + m[8] * v.Z);

    /// <summary>NiMatrix3::Inverse: the general 3x3 inverse (a singular matrix, which a node rotation never is, gives the identity here).</summary>
    static Rot9 Inverse(in Rot9 m)
    {
        double a = m[4] * m[8] - m[5] * m[7], b = m[5] * m[6] - m[3] * m[8], c = m[3] * m[7] - m[4] * m[6];
        double det = m[0] * a + m[1] * b + m[2] * c;
        Rot9 o = default;
        if (Math.Abs(det) < 1e-12)
        {
            o[0] = o[4] = o[8] = 1;
            return o;
        }
        double k = 1 / det;
        o[0] = a * k; o[1] = (m[2] * m[7] - m[1] * m[8]) * k; o[2] = (m[1] * m[5] - m[2] * m[4]) * k;
        o[3] = b * k; o[4] = (m[0] * m[8] - m[2] * m[6]) * k; o[5] = (m[2] * m[3] - m[0] * m[5]) * k;
        o[6] = c * k; o[7] = (m[1] * m[6] - m[0] * m[7]) * k; o[8] = (m[0] * m[4] - m[1] * m[3]) * k;
        return o;
    }
}
