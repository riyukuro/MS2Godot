using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// A beam part (CBeamEffect, beameffect.cpp; type 0x15): a ribbon along a curve between the first and last points of its
/// target's point list (<see cref="LightningPoints"/>, the same list a lightning reads), bent by the param's beamInfo and
/// laid again at each update (<see cref="BeamStrip"/>).
/// - Made only with a point list and a scene node (vfunc_3 @0x140a07320: false without them, or with an empty texture
///   list, so the effect service counts no part). CMagic's path segment effects are the only requests that pass one
///   (magicdata.cpp:1287-1305); their node is the magic's scene, posed with its rotation (the holder node here), which a
///   directionCurve reads.
/// - Its vertices are world points (the root hangs on the effect service's least loaded world root, vfunc_4), so the node
///   here is top level at the identity.
/// - The material (vfunc_3 :114-153): the picked texture, filter mode 0, apply mode 1, clamp mode 0; the white vertex
///   colour as emissive; blended by alphaMode as the lightning's; fog off, both sides, no z write
///   (<see cref="SwooshEffect.MaterialFor"/>).
/// - vfunc_9 @0x140a07ef0: an app-culled root lays nothing.
/// </summary>
public sealed partial class BeamEffect : MeshInstance3D
{
    readonly BeamStrip strip;
    readonly LightningPoints points;
    readonly Node3D node;
    // one surface of the strip's pairs with fixed triangles and uv; each update rewrites the pairs in place
    readonly ArrayMesh mesh = new();
    readonly byte[] vertices;
    readonly int vertexStride, pairs;
    Material laidMaterial;
    // the param's textures' materials, in its order (null: the texture is missing)
    readonly Material[] materials;
    float delay, life;
    public static int Made, Laid;
    /// <summary>The texture the last lay picked (-1 before one).</summary>
    public int Picked { get; private set; } = -1;
    // STILL-OPEN: the client's CRT rand (FUN_14109a9e0); Random.Shared gives the same range (0..32767) from another generator
    static readonly System.Func<int> Rand = () => System.Random.Shared.Next(0x8000);

    BeamEffect(BeamStrip strip, LightningPoints points, Node3D node, Material[] materials, int pairs, float delay, float life)
    {
        this.strip = strip; this.points = points; this.node = node; this.materials = materials; this.pairs = pairs;
        this.delay = delay; this.life = life;
        Name = "Beam";
        TopLevel = true;
        int n = pairs;
        var at = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var tri = new int[(n - 1) * 6];
        float div = n - 1;
        for (int j = 0; j < n; j++)
        {
            // UpdateMesh :260-271: uv (j / (n - 1), 0) on the left row, 1 on the right; they never change with the count
            uv[2 * j] = new Vector2(j / div, 0);
            uv[2 * j + 1] = new Vector2(j / div, 1);
        }
        for (int i = 0; i < n - 1; i++)
        {
            // pair i is vertices 2i (left) and 2i + 1 (right): the client's (i, i + n, i + 1), (i + 1, i + n, i + 1 + n)
            int a0 = 2 * i, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
            tri[6 * i] = a0; tri[6 * i + 1] = b0; tri[6 * i + 2] = a1;
            tri[6 * i + 3] = a1; tri[6 * i + 4] = b0; tri[6 * i + 5] = b1;
        }
        using (var arrays = new Godot.Collections.Array())
        {
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = at;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uv;
            arrays[(int)Godot.Mesh.ArrayType.Index] = tri;
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        }
        var format = (RenderingServer.ArrayFormat)mesh.SurfaceGetFormat(0);
        vertexStride = (int)RenderingServer.MeshSurfaceGetFormatVertexStride(format, n * 2);
        vertices = new byte[n * 2 * vertexStride];
        Mesh = mesh;
        CastShadow = ShadowCastingSetting.Off;
    }

    /// <summary>Whether <paramref name="part"/> makes a beam on <paramref name="points"/>: a beam param that the service
    /// makes (<see cref="EffectRandom.ParamMade"/>), a point list and a texture (vfunc_3 :112-113). A param with no curve
    /// (no beamInfo, or a quality the strip cannot hold) is still made; it lays nothing.</summary>
    public static bool Makes(IReadOnlyList<EffectPart> parts, EffectPart part, LightningPoints points) =>
        points != null && part.Strip is { TexturesA.Length: > 0 } && part.TypeId == EffectSkipTable.TypeBeam
        && EffectRandom.ParamMade(parts, part);

    /// <summary>The beam of <paramref name="part"/> on <paramref name="points"/> and the scene node <paramref name="node"/>,
    /// null when the client makes none (<see cref="Makes"/>). Its textures are looked up here, once. A loop part lives until
    /// its effect is stopped.</summary>
    public static BeamEffect Make(IReadOnlyList<EffectPart> parts, EffectPart part, LightningPoints points, Node3D node, ModelBuilder mb)
    {
        if (node == null || !Makes(parts, part, points)) return null;
        var p = part.Strip;
        var mats = new Material[p.TexturesA.Length];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = SwooshEffect.MaterialFor(mb.TextureEntry(CombatLayer.EffectArchive, EffectStripParam.TextureEntry(p.TexturesA[i])),
                p.AlphaMode, 1f);
        Made++;
        // a strip with no curve still takes the part's place; one pair keeps the surface valid and draws nothing
        int n = System.Math.Max(2, BeamStrip.Samples(p));
        return new BeamEffect(new BeamStrip(p), points, node, mats, n, part.StartDelay, part.Loop ? float.MaxValue : part.Duration);
    }

    public override void _Process(double delta)
    {
        if (delay > 0)
        {
            delay -= (float)delta;
            if (delay > 0) return;
        }
        life -= (float)delta;
        if (life <= 0) { QueueFree(); return; }
        if (!Visible) return;
        var cam = GetViewport()?.GetCamera3D();
        // CameraMgr::FUN_140af0250: the camera's world direction (the view axis), zero without a camera
        var look = cam != null ? -cam.GlobalBasis.Z : Vector3.Zero;
        // the scene node's world rotation as client columns (godot basis (x, z, -y), RemoteLayer.ClientBasis)
        var b = GodotObject.IsInstanceValid(node) ? node.GlobalBasis : Basis.Identity;
        if (!strip.Rebuild(points.From, points.To, C(b.Column0), -C(b.Column2), C(b.Column1),
                new System.Numerics.Vector3(look.X, -look.Z, look.Y)))
            return;
        Lay(Rand() % materials.Length);
    }

    /// <summary>
    /// The strip (UpdateMesh @0x140a081d0 :225-286): the texture <paramref name="pick"/> (rand() % count, picked at each update
    /// that made a curve), the left row then the right row in white, the triangles (i, i + n, i + 1) and (i + 1, i + n, i + 1 + n).
    /// Here the rows are pairs in a surface made once at the strip's fixed count: an update writes the pairs in place (one
    /// vertex region update and the box), the material only when the pick changes, so a lay costs 2 or 3 engine calls.
    /// </summary>
    internal void Lay(int pick)
    {
        Picked = pick;
        var mat = materials[pick];
        var l = strip.Left;
        var r = strip.Right;
        if (mat == null || l.Length != pairs) return;
        // the client points as godot (x, z, -y), written as floats into the kept buffer; the box in client space
        var f = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(vertices.AsSpan()); // perf-ok: MemoryMarshal.Cast is a span view, not LINQ
        int step = vertexStride / 4;
        var lo = System.Numerics.Vector3.Min(l[0], r[0]); // perf-ok: System.Numerics.Vector3.Min/Max, not LINQ
        var up = System.Numerics.Vector3.Max(l[0], r[0]); // perf-ok: as above
        for (int j = 0, o = 0; j < pairs; j++, o += 2 * step)
        {
            var a = l[j]; var c = r[j];
            f[o] = a.X; f[o + 1] = a.Z; f[o + 2] = -a.Y;
            f[o + step] = c.X; f[o + step + 1] = c.Z; f[o + step + 2] = -c.Y;
            lo = System.Numerics.Vector3.Min(lo, System.Numerics.Vector3.Min(a, c)); // perf-ok: as above
            up = System.Numerics.Vector3.Max(up, System.Numerics.Vector3.Max(a, c)); // perf-ok: as above
        }
        mesh.SurfaceUpdateVertexRegion(0, 0, vertices);
        // client (x, y, z) to godot (x, z, -y): the godot box runs x lo..up, y lo.z..up.z, z -up.y..-lo.y
        mesh.CustomAabb = new Aabb(new Vector3(lo.X, lo.Z, -up.Y), new Vector3(up.X - lo.X, up.Z - lo.Z, up.Y - lo.Y));
        if (!ReferenceEquals(mat, laidMaterial)) { mesh.SurfaceSetMaterial(0, mat); laidMaterial = mat; }
        Laid++;
    }

    /// <summary>The pairs the surface holds.</summary>
    public int Pairs => pairs;

    /// <summary>Vertex <paramref name="vertex"/> as the last lay handed it to the surface (pair i: 2i left, 2i + 1 right). For
    /// the checks: the headless renderer keeps no region updates.</summary>
    internal Vector3 LaidVertex(int vertex)
    {
        var at = vertices.AsSpan(vertex * vertexStride);
        return new(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at[4..]),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at[8..]));
    }

    /// <summary>Godot (x, y, z) as client (x, -z, y).</summary>
    static System.Numerics.Vector3 C(Vector3 v) => new(v.X, -v.Z, v.Y);
}
