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
/// stretched into the 92 x 106 image (<see cref="Image"/>) the movie's loader shows in m_renderTarget, under the movie's
/// mask.
/// </summary>
public sealed partial class PortraitView : Node
{
    const int ImageWidth = 92, ImageHeight = 106, RenderWidth = 91;
    const float Fov = 40f, Near = 10f, Far = 100000f, HeadRaise = 25f, Distance = 130f, LightDimmer = 0.9f;

    SubViewport view;
    Camera3D camera;
    Node3D copy;
    Skeleton3D skeleton;
    int head = -1;

    SubViewport image;

    /// <summary>The 92 x 106 image the movie loads as "img://&lt;movie&gt;:faceView/92/106".</summary>
    public Texture2D Image => image?.GetTexture();

    /// <summary>The portrait PC and its views, or null when the model cannot be built. The node has to stay in the tree
    /// for the views to render; it keeps its model across HUD rebuilds, as the client keeps the portrait PC (CreatePortraitPC
    /// runs when the own user is added), so its idle runs on.</summary>
    public static PortraitView Create(PackSet packs, string nifXml)
    {
        if (string.IsNullOrEmpty(nifXml)) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pv = new PortraitView { Name = "PortraitView" };
        if (!pv.BuildModel(packs, nifXml)) { pv.QueueFree(); return null; }
        pv.image = new SubViewport
        {
            Size = new Vector2I(ImageWidth, ImageHeight), TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        pv.image.AddChild(new Sprite2D
        {
            Texture = pv.view.GetTexture(), Centered = false, Scale = new Vector2((float)ImageWidth / RenderWidth, 1f),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        });
        pv.AddChild(pv.image);
        GD.Print($"portrait: built in {sw.ElapsedMilliseconds} ms");
        return pv;
    }

    bool BuildModel(PackSet packs, string nifXml)
    {
        try
        {
            var builder = ModelBuilder.Ui(packs, nifXml);
            var (visual, anim, _, _) = Look.PlayerLook.Build(builder, packs);
            view = new SubViewport
            {
                Size = new Vector2I(RenderWidth, ImageHeight), TransparentBg = true, OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(view);
            copy = visual;
            view.AddChild(copy);
            camera = new Camera3D { Fov = Fov, Near = Near, Far = Far, KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true, Compositor = Ms2Bloom.GammaOut() };   // character_lit writes gamma values
            view.AddChild(camera);
            skeleton = copy.GetNode<Skeleton3D>($"Model/{ModelBuilder.SkeletonName}");
            head = skeleton.FindBone("Bip01 Head");
            if (anim.HasAnimation("idle")) anim.Play("idle");     // Idle_A
            OwnLights(copy, PortraitLightDir);
            return head >= 0;
        }
        catch (Exception ex) { GD.PushWarning("portrait: " + ex.Message); return false; }
    }

    /// <summary>The world direction UIPortrait::vfunc_14's directional light travels (uiportrait.cpp:129-142): it sets no
    /// rotation, and an NiDirectionalLight's world direction is its rotation's first column, (1, 0, 0) from the constructor
    /// (NiDirectionalLight.cpp:25, 34). Gamebryo's (x, y, z) is Godot's (x, z, -y).
    /// STILL-OPEN: the light is fixed in the portrait's world, so how it falls on the model follows the copy's rotation there;
    /// the copy here stands unrotated (facing the client's -y).</summary>
    internal static readonly Vector3 PortraitLightDir = new(1f, 0f, 0f);

    /// <summary>The world direction UIModelView::vfunc_14's directional light travels (uimodelview.cpp:292-316, the paper
    /// doll and the mount portrait): SetRotate(MakeXRotation(11.5) * MakeYRotation(68.9) * MakeZRotation(180)), whose first
    /// column (Foundation/efd/Win32/Matrix3_Win32.cpp:76-125) is (-0.360, -0.186, -0.914) in Gamebryo's (x, y, z), Godot's
    /// (x, z, -y). Both views hold the model in that world's frame.</summary>
    internal static readonly Vector3 ModelViewLightDir = new Vector3(-0.3600f, -0.9142f, 0.1860f).Normalized();

    /// <summary>
    /// UIPortrait::vfunc_14 lights the copy with its own pair, both dimmed by the 0.9 CreatePortrait passes: a directional
    /// light with diffuse and specular (1, 1, 1) and ambient (0, 0, 0), and an ambient light with ambient (1, 1, 1).
    /// vfunc_11 attaches only these two to the copy's roots, so no field light reaches it. The character light (types 1-3)
    /// takes its direction from the camera (SetSunDirection(camera direction) sets only the character light's constants,
    /// unit_14107ac30_tcreatecharinfo.cpp:264-284); types 0 and NiStandardMaterial meshes take the directional light's own
    /// world direction, <paramref name="sunDir"/> (<see cref="PortraitLightDir"/>, <see cref="ModelViewLightDir"/>).
    /// </summary>
    internal static void OwnLights(Node n, Vector3 sunDir)
    {
        const float dim = LightDimmer;
        if (n is GeometryInstance3D g && UsesCharacterShader(g))
        {
            g.SetInstanceShaderParameter("own_lights", 1f);
            g.SetInstanceShaderParameter("own_sun_dir", sunDir);
            g.SetInstanceShaderParameter("own_ambient", new Vector3(dim, dim, dim));
            g.SetInstanceShaderParameter("own_sun_color", new Vector3(dim, dim, dim));
            g.SetInstanceShaderParameter("own_sun_ambient", Vector3.Zero);
            g.SetInstanceShaderParameter("own_sun_specular", new Vector3(dim, dim, dim));
        }
        foreach (var c in n.GetChildren()) OwnLights(c, sunDir);
    }

    static bool UsesCharacterShader(GeometryInstance3D g)
    {
        if (g.MaterialOverride is ShaderMaterial o) return IsCharacter(o);
        if (g is not MeshInstance3D mi || mi.Mesh == null) return false;
        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is ShaderMaterial sm && IsCharacter(sm)) return true;
        return false;
        static bool IsCharacter(ShaderMaterial m) => ModelBuilder.IsCharacterShader(m.Shader);
    }

    public override void _Process(double delta)
    {
        if (head < 0) return;
        // UIPortrait::vfunc_2: target = head + (0, 0, 25), eye = target + forward * 130, looking at the target, Z up
        var target = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head)).Origin + new Vector3(0, HeadRaise, 0);
        var forward = copy.GlobalBasis.Z.Normalized();          // the model faces +Z (MS2 -Y)
        camera.LookAtFromPosition(target + forward * Distance, target, Vector3.Up);
    }
}
