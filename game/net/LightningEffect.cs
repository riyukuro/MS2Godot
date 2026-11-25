using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>The point list a lightning part reads every update (the request's m_pIds, CMagic's slot points): its first and
/// last points, client space. The holder writes them each frame; the part only reads them.</summary>
public sealed class LightningPoints
{
    public System.Numerics.Vector3 From, To;
}

/// <summary>
/// A lightning part (LightningEffect, lightningeffectparam.cpp; type 0x10): a strip on a jagged path between the first and
/// last points of its target's point list (<see cref="LightningPoints"/>), laid again at each update (<see cref="LightningPath"/>).
/// - Made only with a point list and a scene node (vfunc_3 @0x14144bdf0, :173-187: false without them, so the effect service
///   counts no part). CMagic's path segment effects are the only requests that pass one (magicdata.cpp:1287-1305).
/// - Its vertices are world points (the root hangs on the effect service's least loaded world root, vfunc_4 :277), so the
///   node here is top level at the identity.
/// - The material (:200-247): the picked texture, filter mode 0, apply mode 1, clamp mode 0; the white vertex colour as
///   emissive; blended by alphaMode (SWOOSH src alpha / one, STANDARD src alpha / inverse src alpha, ADD one / one); fog off,
///   both sides, no z write. The swoosh's materials are the same (<see cref="SwooshEffect.MaterialFor"/>).
/// - vfunc_9 (:261): an app-culled root (FUN_1411a68f0 hidden) lays nothing.
/// - The beam part (type 0x15, <see cref="BeamEffect"/>) reads the same point list.
/// STILL-OPEN: the NiSortAdjustNode's sortingMode (OFF or INHERIT) is not followed; the strip sorts as any transparent mesh.
/// </summary>
public sealed partial class LightningEffect : MeshInstance3D
{
    readonly LightningPath path;
    readonly LightningPoints points;
    // one surface of LightningPath.MaxRow edge pairs with fixed triangles; each update rewrites the pairs in place
    readonly ArrayMesh mesh = new();
    readonly byte[] vertices, uvs;
    readonly int vertexStride, uvStride;
    // the pairs the last lay wrote (0: none); the pairs from it on are each one point twice (their triangles have no area)
    int laidRows;
    Material laidMaterial;
    // each distance class's textures' materials, in the param's order (null: the texture is missing)
    readonly Material[][] materials;
    float delay, life;
    public static int Made, Laid;
    /// <summary>The rows the last update laid (the path's count).</summary>
    public int Rows => path.Count;
    // STILL-OPEN: the client's CRT rand (FUN_14109a9e0); Random.Shared gives the same range (0..32767) from another generator
    static readonly System.Func<int> Rand = () => System.Random.Shared.Next(0x8000);

    LightningEffect(LightningPath path, LightningPoints points, Material[][] materials, float delay, float life)
    {
        this.path = path; this.points = points; this.materials = materials; this.delay = delay; this.life = life;
        Name = "Lightning";
        TopLevel = true;
        const int n = LightningPath.MaxRow;
        var at = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var tri = new int[(n - 1) * 6];
        for (int i = 0; i < n - 1; i++)
        {
            // pair i is vertices 2i (left) and 2i + 1 (right): triangles (a0, b0, a1), (a1, b0, b1), as the client's
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
        uvStride = (int)RenderingServer.MeshSurfaceGetFormatAttributeStride(format, n * 2);
        vertices = new byte[n * 2 * vertexStride];
        uvs = new byte[n * 2 * uvStride];
        Mesh = mesh;
        CastShadow = ShadowCastingSetting.Off;
    }

    /// <summary>Whether <paramref name="part"/> makes a lightning with the point list <paramref name="points"/>: a lightning
    /// param that the service makes (CEffectParam::vfunc_2: loop or a duration, <see cref="EffectRandom.ParamMade"/>) and a
    /// point list.</summary>
    public static bool Makes(IReadOnlyList<EffectPart> parts, EffectPart part, LightningPoints points) =>
        points != null && part.Strip != null && part.TypeId == EffectSkipTable.TypeLightning && EffectRandom.ParamMade(parts, part);

    /// <summary>The lightning of <paramref name="part"/> on <paramref name="points"/>, null when the client makes none
    /// (<see cref="Makes"/>). Its textures are looked up here, once. A loop part lives until its effect is stopped.</summary>
    public static LightningEffect Make(IReadOnlyList<EffectPart> parts, EffectPart part, LightningPoints points, ModelBuilder mb)
    {
        if (!Makes(parts, part, points)) return null;
        var p = part.Strip;
        var path = new LightningPath(p);
        var mats = new Material[3][];
        for (int c = 0; c < 3; c++)
        {
            var names = path.Textures(c);
            mats[c] = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
                mats[c][i] = SwooshEffect.MaterialFor(mb.TextureEntry(CombatLayer.EffectArchive, EffectStripParam.TextureEntry(names[i])),
                    p.AlphaMode, 1f);
        }
        Made++;
        return new LightningEffect(path, points, mats, part.StartDelay, part.Loop ? float.MaxValue : part.Duration);
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
        if (!path.Rebuild(points.From, points.To, new System.Numerics.Vector3(look.X, -look.Z, look.Y), (uint)Time.GetTicksMsec(), Rand))
            return;
        Lay();
    }

    /// <summary>The strip (Rebuild :356-405): the left row then the right row, uv (j / (n - 1), 0 or 1), the triangles
    /// (i, i + n, i + 1) and (i + 1, i + n, i + 1 + n); the class's picked texture's material. No texture picked (an empty
    /// list) draws nothing.
    /// Here the rows are pairs in a surface made once: an update writes the pairs in place (one vertex region update, the
    /// box), the uv only when the row count changes and the material only when the pick changes, so a lay costs 2 to 4
    /// engine calls whatever its rows (an ImmediateMesh took 12 a quad). The pairs past the count are each the last left
    /// point twice, so the triangles there have no area.</summary>
    void Lay()
    {
        int n = path.Count;
        Material mat = n >= 2 && path.TextureIndex >= 0 ? materials[path.TextureClass][path.TextureIndex] : null;
        if (mat == null)
        {
            if (laidRows == 0) return;
            // nothing to draw: every pair one point
            int hi = System.Math.Min(LightningPath.MaxRow, laidRows + 1);
            for (int j = 0; j < hi; j++) { Put(2 * j, default); Put(2 * j + 1, default); }
            mesh.SurfaceUpdateVertexRegion(0, 0, vertices.AsSpan(0, hi * 2 * vertexStride));
            laidRows = 0;
            return;
        }
        var l = path.Left;
        var r = path.Right;
        Vector3 lo = G(l[0]), up = lo;
        for (int j = 0; j < n; j++)
        {
            Vector3 a = G(l[j]), b = G(r[j]);
            Put(2 * j, a); Put(2 * j + 1, b);
            lo = lo.Min(a).Min(b); up = up.Max(a).Max(b);     // perf-ok: Godot Vector3.Min/Max (componentwise), not LINQ
        }
        // the pairs from n on to the last lay's count (at least one) collapse onto the last left point
        int end = System.Math.Min(LightningPath.MaxRow, System.Math.Max(n + 1, laidRows));
        var last = G(l[n - 1]);
        for (int j = n; j < end; j++) { Put(2 * j, last); Put(2 * j + 1, last); }
        mesh.SurfaceUpdateVertexRegion(0, 0, vertices.AsSpan(0, end * 2 * vertexStride));
        if (n != laidRows)
        {
            float div = n - 1;
            for (int j = 0; j < n; j++) { PutUv(2 * j, j / div, 0); PutUv(2 * j + 1, j / div, 1); }
            mesh.SurfaceUpdateAttributeRegion(0, 0, uvs.AsSpan(0, n * 2 * uvStride));
        }
        mesh.CustomAabb = new Aabb(lo, up - lo);
        if (!ReferenceEquals(mat, laidMaterial)) { mesh.SurfaceSetMaterial(0, mat); laidMaterial = mat; }
        laidRows = n;
        Laid++;
    }

    /// <summary>Vertex <paramref name="vertex"/> as the last lay handed it to the surface (pair i: 2i left, 2i + 1 right). For
    /// the checks: the headless renderer keeps no region updates.</summary>
    internal Vector3 LaidVertex(int vertex)
    {
        var at = vertices.AsSpan(vertex * vertexStride);
        return new(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at[4..]),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at[8..]));
    }

    /// <summary>The uv of <paramref name="vertex"/> as last handed over.</summary>
    internal Vector2 LaidUv(int vertex)
    {
        var at = uvs.AsSpan(vertex * uvStride);
        return new(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at),
            System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(at[4..]));
    }

    void Put(int vertex, Vector3 v)
    {
        var at = vertices.AsSpan(vertex * vertexStride);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at, v.X);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at[4..], v.Y);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at[8..], v.Z);
    }

    void PutUv(int vertex, float u, float v)
    {
        var at = uvs.AsSpan(vertex * uvStride);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at, u);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(at[4..], v);
    }

    /// <summary>Client (x, y, z) as godot (x, z, -y).</summary>
    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);
}
