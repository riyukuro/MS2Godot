using Godot;

namespace Ms2Godot;

/// <summary>
/// The glow target of the client's post processing (GlowRenderClick): the objects that carry a glow map are drawn alone
/// (visual layer <see cref="Layer"/>, material field_glow) into a viewport that follows the main camera; Ms2Bloom adds that target
/// to its bright pass. The main camera must not draw the layer (<see cref="MainMask"/>).
/// The client's target is SINGLE_COLOR_16 (R16F) at the full screen size (postprocessingservice.cpp:2533-2534); here a
/// full-size viewport with an HDR (RGBA16F, linear) target, the nearest Godot gives. It has its own depth buffer and a black
/// clear, as the client's GlowRenderClick (CreateGlowClick @0x1416a92e0).
/// </summary>
public partial class GlowPass : SubViewport
{
    /// <summary>Set by a field before it builds models: meshes with a glow map get a twin on the glow layer.</summary>
    public static bool Wanted;
    public const int Layer = 20;
    public const uint Bit = 1u << (Layer - 1);
    public const uint MainMask = 0xFFFFFu & ~Bit;

    Camera3D src, cam;

    public static GlowPass Attach(Node parent, Camera3D main)
    {
        main.CullMask &= ~Bit;   // the other bits stay (Field.FogLayer)
        var vp = new GlowPass { Name = "GlowPass", src = main, TransparentBg = false, UseHdr2D = true, RenderTargetUpdateMode = UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.cam = new Camera3D { CullMask = Bit, Current = true, Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black, AmbientLightSource = Godot.Environment.AmbientSource.Disabled } };
        vp.cam.AddToGroup(GammaGuard.Exempt);   // the glow target keeps its own values (field_glow), read by Ms2Bloom's bright pass
        vp.AddChild(vp.cam);
        parent.AddChild(vp);
        return vp;
    }

    public override void _Process(double delta)
    {
        // full size, as the client's glow texture ([8], postprocessingservice.cpp:2533-2534)
        var size = (Vector2I)GetParent().GetViewport().GetVisibleRect().Size;
        if (size.X > 8 && size != Size) Size = size;
        cam.GlobalTransform = src.GlobalTransform;
        cam.Fov = src.Fov; cam.Near = src.Near; cam.Far = src.Far;
        cam.KeepAspect = src.KeepAspect;
        cam.Projection = src.Projection; cam.Size = src.Size; cam.FrustumOffset = src.FrustumOffset;   // the main camera's raster origin, in near-plane units
    }

    /// <summary>PostProcessingService::SetClickActive(8, active) @0x1416aa450 (postprocessingservice.cpp:2163; from
    /// SetSoftParticlesOption, applyeffect.cpp:490): the glow target is drawn only while the HDR bloom step is active. Off, the
    /// viewport is not updated and _Process does not run, so the switched-off click costs nothing per frame.</summary>
    public void SetActive(bool active)
    {
        RenderTargetUpdateMode = active ? UpdateMode.Always : UpdateMode.Disabled;
        SetProcess(active);
    }

    /// <summary>The target as a rendering device texture (render thread).</summary>
    public Rid Texture() => RenderingServer.TextureGetRdTexture(GetTexture().GetRid());
}
