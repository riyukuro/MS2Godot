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
/// STILL-OPEN: the beam part (type 0x15, beameffect.cpp) reads the same point list; three ray effects lay only beams
/// (Handy Lightning, Plasma Chain, Harmony of Soul), so those segments draw nothing yet.
/// STILL-OPEN: the NiSortAdjustNode's sortingMode (OFF or INHERIT) is not followed; the strip sorts as any transparent mesh.
/// </summary>
public sealed partial class LightningEffect : MeshInstance3D
{
    readonly LightningPath path;
    readonly LightningPoints points;
    readonly ImmediateMesh mesh = new();
    // each distance class's textures' materials, in the param's order (null: the texture is missing)
    readonly Material[][] materials;
    float delay, life;
    public static int Made, Laid;
    // STILL-OPEN: the client's CRT rand (FUN_14109a9e0); Random.Shared gives the same range (0..32767) from another generator
    static readonly System.Func<int> Rand = () => System.Random.Shared.Next(0x8000);

    LightningEffect(LightningPath path, LightningPoints points, Material[][] materials, float delay, float life)
    {
        this.path = path; this.points = points; this.materials = materials; this.delay = delay; this.life = life;
        Name = "Lightning";
        TopLevel = true;
        Mesh = mesh;
        CastShadow = ShadowCastingSetting.Off;
        ExtraCullMargin = 16384;   // the strip moves every update; its box is not kept
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
    /// list) draws nothing.</summary>
    void Lay()
    {
        mesh.ClearSurfaces();
        int n = path.Count;
        if (n < 2 || path.TextureIndex < 0) return;
        var mat = materials[path.TextureClass][path.TextureIndex];
        if (mat == null) return;
        var l = path.Left;
        var r = path.Right;
        float last = n - 1;
        mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles, mat);
        for (int i = 0; i < n - 1; i++)
        {
            float u0 = i / last, u1 = (i + 1) / last;
            Vector3 a0 = G(l[i]), a1 = G(l[i + 1]), b0 = G(r[i]), b1 = G(r[i + 1]);
            mesh.SurfaceSetUV(new Vector2(u0, 0)); mesh.SurfaceAddVertex(a0);
            mesh.SurfaceSetUV(new Vector2(u0, 1)); mesh.SurfaceAddVertex(b0);
            mesh.SurfaceSetUV(new Vector2(u1, 0)); mesh.SurfaceAddVertex(a1);
            mesh.SurfaceSetUV(new Vector2(u1, 0)); mesh.SurfaceAddVertex(a1);
            mesh.SurfaceSetUV(new Vector2(u0, 1)); mesh.SurfaceAddVertex(b0);
            mesh.SurfaceSetUV(new Vector2(u1, 1)); mesh.SurfaceAddVertex(b1);
        }
        mesh.SurfaceEnd();
        Laid++;
    }

    /// <summary>Client (x, y, z) as godot (x, z, -y).</summary>
    static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);
}
