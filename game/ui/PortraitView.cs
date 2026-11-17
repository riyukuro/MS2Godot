using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>
/// The game menu's face portrait: UIGameMenuDialog.loadRenderTarget loads "img://&lt;movie&gt;:faceView/92/106" into
/// m_renderTarget. GFxImageLoader makes a 92 x 106 image for it and UIGameMenuDialog::vfunc_25 fills that with
/// CreatePortrait(92, 106, portrait PC, 0.9): a UIPortrait render view whose texture the image wraps (FUN_141ab7200).
/// <list type="bullet">
/// <item>Model: the PortraitPC, a copy of the player made by CreatePortraitPC when the own user is added. It has the
/// player's body, look and shown equipment, and stands in state 1 (StateIdle, sub state 1). Its bore idles run only for
/// MyPC, so it holds the IDLE sequence: Idle_A (table/pcactiondata.xml; the job groups only override BORE).</item>
/// <item>Camera (UIPortrait vfunc_15 / vfunc_2): frustum +-0.312384 x +-0.36397 at near 10, far 100000, so a 40 degree
/// vertical field of view. Each frame it looks from 130 units in front of the copy at its "Bip01 Head" position
/// raised by 25.</item>
/// <item>Target: RenderedTextureRenderClick clears to (0, 0, 0, 0) into a texture with alpha, so the movie's backdrop
/// shows around the model.</item>
/// </list>
/// The client's frustum is 0.312384 / 0.36397 wide for a 92 x 106 image, about 1.1% narrower than square pixels give.
/// So the view renders 91 x 106 at the same vertical field of view (91 / 106 x 0.36397 = 0.31247) and is drawn
/// stretched to 92 x 106. The image goes 1:1 into m_renderTarget and is clipped by the movie's mask shape over it.
/// </summary>
public sealed partial class PortraitView : Node2D
{
    const int ImageWidth = 92, ImageHeight = 106, RenderWidth = 91;
    const float Fov = 40f, Near = 10f, Far = 100000f, HeadRaise = 25f, Distance = 130f, LightDimmer = 0.9f;

    SubViewport view;
    Camera3D camera;
    Node3D copy;
    Skeleton3D skeleton;
    int head = -1;

    /// <summary>Builds the portrait into <paramref name="target"/> (m_renderTarget), clipped by the mask child of
    /// <paramref name="body"/> that covers it. Null when the model cannot be built. With <paramref name="reuse"/> (the
    /// portrait of the previous build, on a HUD relayout) its render view, copy and camera move over instead of being
    /// built again: the client keeps the portrait PC across a resize (CreatePortraitPC runs when the own user is added), so
    /// its idle runs on.</summary>
    public static PortraitView Attach(MovieClipNode body, MovieClipNode.Child target, PackSet packs, string nifXml, PortraitView reuse = null)
    {
        if (target?.Node is not MovieClipNode rt || string.IsNullOrEmpty(nifXml)) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pv = new PortraitView { Name = "PortraitView" };
        bool reused = reuse != null && IsInstanceValid(reuse) && pv.Adopt(reuse);
        if (!reused && !pv.BuildModel(packs, nifXml)) return null;

        // the mask: the masking child whose clip depth covers the target's depth
        int depth = (int)target.Def["depth"];
        var mask = body.Children.FirstOrDefault(c => c.IsMask && (int)c.Def["depth"] < depth && (int)c.Def["clipDepth"] >= depth);
        Node2D holder = pv;
        if (mask != null && body.Scope.Symbol((int)mask.Def["id"]) is { } sym && sym["paths"] is System.Text.Json.Nodes.JsonArray paths)
        {
            var clip = new MaskShape(paths) { ClipChildren = ClipChildrenMode.Only, Transform = rt.Transform.AffineInverse() * mask.Node.Transform };
            pv.AddChild(clip);
            holder = new Node2D { Transform = clip.Transform.AffineInverse() };
            clip.AddChild(holder);
        }
        holder.AddChild(new Sprite2D
        {
            Texture = pv.view.GetTexture(), Centered = false, Scale = new Vector2((float)ImageWidth / RenderWidth, 1f),
            TextureFilter = TextureFilterEnum.Linear,
        });
        rt.AddChild(pv);
        GD.Print($"portrait: {(reused ? "reused" : "built")} in {sw.ElapsedMilliseconds} ms");
        return pv;
    }

    /// <summary>Takes over <paramref name="old"/>'s render view and model; the old portrait no longer drives the camera.</summary>
    bool Adopt(PortraitView old)
    {
        if (old.view == null || !IsInstanceValid(old.view) || old.head < 0) return false;
        view = old.view; camera = old.camera; copy = old.copy; skeleton = old.skeleton; head = old.head;
        old.RemoveChild(view);
        AddChild(view);
        old.view = null; old.head = -1;
        return true;
    }

    bool BuildModel(PackSet packs, string nifXml)
    {
        try
        {
            var builder = new ModelBuilder(new Ms2.Formats.Nif.NifSpec(nifXml), packs);
            var (visual, anim, _, _) = Look.PlayerLook.Build(builder, packs);
            view = new SubViewport
            {
                Size = new Vector2I(RenderWidth, ImageHeight), TransparentBg = true, OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(view);
            copy = visual;
            view.AddChild(copy);
            camera = new Camera3D { Fov = Fov, Near = Near, Far = Far, KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true };
            view.AddChild(camera);
            skeleton = copy.GetNode<Skeleton3D>($"Model/{ModelBuilder.SkeletonName}");
            head = skeleton.FindBone("Bip01 Head");
            if (anim.HasAnimation("idle")) anim.Play("idle");     // Idle_A
            OwnLights(copy);
            return head >= 0;
        }
        catch (Exception ex) { GD.PushWarning("portrait: " + ex.Message); return false; }
    }

    /// <summary>
    /// UIPortrait::vfunc_14 lights the copy with its own pair, both dimmed by the 0.9 CreatePortrait passes: a directional
    /// light with diffuse and specular (1, 1, 1) and ambient (0, 0, 0), and an ambient light with ambient (1, 1, 1).
    /// vfunc_11 attaches only these two to the copy's roots, so no field light reaches it. The light's direction needs
    /// nothing here: SetSunDirection(camera direction) is what character_lit already takes from the camera that draws it.
    /// </summary>
    static void OwnLights(Node n)
    {
        const float dim = LightDimmer;
        if (n is GeometryInstance3D g && UsesCharacterShader(g))
        {
            g.SetInstanceShaderParameter("own_lights", 1f);
            g.SetInstanceShaderParameter("own_ambient", new Vector3(dim, dim, dim));
            g.SetInstanceShaderParameter("own_sun_color", new Vector3(dim, dim, dim));
            g.SetInstanceShaderParameter("own_sun_ambient", Vector3.Zero);
            g.SetInstanceShaderParameter("own_sun_specular", new Vector3(dim, dim, dim));
        }
        foreach (var c in n.GetChildren()) OwnLights(c);
    }

    static bool UsesCharacterShader(GeometryInstance3D g)
    {
        if (g.MaterialOverride is ShaderMaterial o) return IsCharacter(o);
        if (g is not MeshInstance3D mi || mi.Mesh == null) return false;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is ShaderMaterial sm && IsCharacter(sm)) return true;
        return false;
        static bool IsCharacter(ShaderMaterial m) => m.Shader?.ResourcePath == "res://shaders/character_lit.gdshader";
    }

    public override void _Process(double delta)
    {
        if (head < 0) return;
        // UIPortrait::vfunc_2: target = head + (0, 0, 25), eye = target + forward * 130, looking at the target, Z up
        var target = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head)).Origin + new Vector3(0, HeadRaise, 0);
        var forward = copy.GlobalBasis.Z.Normalized();          // the model faces +Z (MS2 -Y)
        camera.LookAtFromPosition(target + forward * Distance, target, Vector3.Up);
    }

    /// <summary>The mask's own outline (the movie's clip shape), drawn opaque so it clips its children by shape.</summary>
    sealed partial class MaskShape : Node2D
    {
        readonly List<Vector2[]> polys = new();

        public MaskShape(System.Text.Json.Nodes.JsonArray paths)
        {
            foreach (var p in paths)
            {
                var a = (System.Text.Json.Nodes.JsonArray)p["pts"];
                var pts = new Vector2[a.Count / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)(double)a[2 * i], (float)(double)a[2 * i + 1]);
                polys.Add(pts);
            }
        }

        public override void _Draw()
        {
            foreach (var pts in polys)
                if (Geometry2D.TriangulatePolygon(pts).Length > 0) DrawColoredPolygon(pts, Colors.White);
        }
    }
}
