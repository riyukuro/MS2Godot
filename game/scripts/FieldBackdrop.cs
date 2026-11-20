using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's backdrop picture: mapxblock &lt;clientProperty bgDay&gt; names one or more images (comma separated; the
/// client picks one at random, CUIService::LoadBackgroundMovie uiservice.cpp:520-522; this takes the first) in Image.m2d bg/.
/// The client shows it through its background movie (Background.gfx, GFxMovieBackground) behind the 3D scene; here it is a
/// picture on a canvas layer the environment draws as its background, sized and moved as that movie does
/// (<see cref="FieldBackground"/>): with &lt;ui vscroll&gt; twice the window high and slid up and down with the camera's
/// pitch, with &lt;ui hscroll&gt; slid sideways with the player's place on the minimap.
/// STILL-OPEN: the random pick among several names; without a local player the client projects
/// WorldService::FUN_141bd3dc0 (gfxmoviebackground.cpp:513), not ported, so the picture keeps its x then.
/// </summary>
public sealed class FieldBackdrop
{
    public const string Archive = "Resource/Image";
    const int Layer = -10;

    readonly TextureRect rect;
    readonly bool scroll, posFlag;
    readonly MinimapInfo minimap;     // null: no minimap, so vfunc_8 moves nothing (m_n98 == 0)
    readonly Vector2 picture;
    Vector2 stage = new(-1, -1);
    Vector2 pos;

    FieldBackdrop(TextureRect rect, Vector2 picture, bool scroll, bool posFlag, MinimapInfo minimap)
    {
        this.rect = rect; this.picture = picture; this.scroll = scroll; this.posFlag = posFlag; this.minimap = minimap;
    }

    /// <summary>Adds the backdrop under parent and switches env to a canvas background; null when there is none.</summary>
    public static FieldBackdrop Add(Node parent, Godot.Environment env, PackSet packs, XDocument mapxblock, FieldData fd)
    {
        var names = (string)mapxblock?.Root?.Element("clientProperty")?.Attribute("bgDay");
        var name = names?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(name)) return null;
        string entry = "bg/" + name.ToLowerInvariant();
        if (!packs.Exists(Archive, entry)) { GD.PushWarning($"backdrop {entry} not found"); return null; }
        var img = new Image();
        if (img.LoadDdsFromBuffer(packs.Read(Archive, entry)) != Error.Ok) return null;
        var layer = new CanvasLayer { Name = "Backdrop", Layer = Layer };
        var rect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(img),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/backdrop.gdshader") },
        };
        layer.AddChild(rect);
        parent.AddChild(layer);
        env.BackgroundMode = Godot.Environment.BGMode.Canvas;
        env.BackgroundCanvasMaxLayer = Layer;
        // InitFromFieldMinimap: a minimap with an image rectangle of positive width (minimapviewport.cpp:42-66)
        var mm = mapxblock != null ? MinimapInfo.Parse(mapxblock) : null;
        if (mm != null && mm.ImgRight - mm.ImgLeft <= 0) mm = null;
        var b = new FieldBackdrop(rect, new Vector2(img.GetWidth(), img.GetHeight()), (fd?.VScroll ?? 1) != 0, (fd?.HScroll ?? 1) != 0, mm);
        b.Resize(rect.GetViewportRect().Size);
        return b;
    }

    /// <summary>Background.onResize: on load and whenever the window size changes (GFxMovieBackground::vfunc_16 on WM_SIZE).</summary>
    void Resize(Vector2 size)
    {
        stage = size;
        uint w = (uint)size.X, h = (uint)size.Y;
        if (scroll)
        {
            var (cw, ch) = FieldBackground.ResizeScroll(w, h);
            rect.Size = new Vector2((float)cw, (float)ch);     // onResizeScroll sets the size only
        }
        else
        {
            var (x, y, cw, ch) = FieldBackground.ResizeNoScroll(picture.X, picture.Y, w, h);
            rect.Size = new Vector2((float)cw, (float)ch);
            pos = new Vector2((float)x, (float)y);
        }
        rect.Position = pos;
    }

    /// <summary>
    /// GFxMovieBackground::vfunc_8 (@0x14127a070) for this frame: the camera's world up vector (Godot y-up turned back to the
    /// client's z-up axes: client x = Godot x) and the local player's position.
    /// </summary>
    public void Update(Camera3D camera, Node3D player)
    {
        var size = rect.GetViewportRect().Size;
        if (size != stage) Resize(size);
        if (minimap == null || !GodotObject.IsInstanceValid(rect)) return;
        uint w = (uint)stage.X, h = (uint)stage.Y;
        if (scroll && camera != null)
        {
            var up = camera.GlobalBasis.Y.Normalized();
            pos.Y = (float)FieldBackground.CameraY(rect.Size.Y, h, FieldBackground.CameraAngle(up.X));
        }
        if (posFlag && player != null)
        {
            var g = player.GlobalPosition;     // FieldBuilder.ToGodotPoint: (x, y, z) -> (x, z, -y)
            if (minimap.ToScreenX(new Vec3(g.X, -g.Z, g.Y), out var sx))
                pos.X = (float)FieldBackground.PosX(rect.Size.X, w, sx);
        }
        rect.Position = pos;
    }
}
