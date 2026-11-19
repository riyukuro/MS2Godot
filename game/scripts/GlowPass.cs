using Godot;

namespace Ms2Godot;

/// <summary>
/// The glow target of the client's post processing (GlowRenderClick): the objects that carry a glow map are drawn alone
/// (visual layer <see cref="Layer"/>, material field_glow) into a viewport that follows the main camera; Ms2Bloom adds that target
/// to its bright pass. The main camera must not draw the layer (<see cref="MainMask"/>).
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
        main.CullMask = MainMask;
        var vp = new GlowPass { Name = "GlowPass", src = main, TransparentBg = false, RenderTargetUpdateMode = UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        vp.cam = new Camera3D { CullMask = Bit, Current = true, Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black, AmbientLightSource = Godot.Environment.AmbientSource.Disabled } };
        vp.cam.AddToGroup(GammaGuard.Exempt);   // the glow target keeps its own values (field_glow), read by Ms2Bloom's bright pass
        vp.AddChild(vp.cam);
        parent.AddChild(vp);
        return vp;
    }

    public override void _Process(double delta)
    {
        var size = (Vector2I)(GetParent().GetViewport().GetVisibleRect().Size / 2);
        if (size.X > 8 && size != Size) Size = size;
        cam.GlobalTransform = src.GlobalTransform;
        cam.Fov = src.Fov; cam.Near = src.Near; cam.Far = src.Far;
        cam.KeepAspect = src.KeepAspect;
        cam.Projection = src.Projection; cam.Size = src.Size; cam.FrustumOffset = src.FrustumOffset;   // the main camera's raster origin, in near-plane units
    }

    /// <summary>The target as a rendering device texture (render thread).</summary>
    public Rid Texture() => RenderingServer.TextureGetRdTexture(GetTexture().GetRid());
}
