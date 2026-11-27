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

    readonly CanvasLayer layer;
    readonly TextureRect rect;
    readonly bool scroll, posFlag;
    readonly MinimapInfo minimap;     // null: no minimap, so vfunc_8 moves nothing (m_n98 == 0)
    Vector2 content;                  // bitmap's current width and height (Background.onResizeNoScroll reads a1.width / height)
    Vector2 stage = new(-1, -1);
    Vector2 pos;
    Vector2 appliedPos = new(float.NaN, float.NaN);

    FieldBackdrop(CanvasLayer layer, TextureRect rect, Vector2 content, bool scroll, bool posFlag, MinimapInfo minimap)
    {
        this.layer = layer; this.rect = rect; this.content = content; this.scroll = scroll; this.posFlag = posFlag; this.minimap = minimap;
    }

    /// <summary>
    /// The field's own backdrop: CUIService::LoadBackgroundMovie (uiservice.cpp:504, @0x141ad9650) on the field load, with the
    /// fielddata &lt;ui vscroll&gt; and &lt;ui hscroll&gt; flags. Adds it under parent and switches env to a canvas background; null
    /// when there is none.
    /// </summary>
    public static FieldBackdrop Add(Node parent, Godot.Environment env, PackSet packs, XDocument mapxblock, FieldData fd)
    {
        var names = (string)mapxblock?.Root?.Element("clientProperty")?.Attribute("bgDay");
        var name = names?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return Load(parent, env, packs, name, (fd?.VScroll ?? 1) != 0, (fd?.HScroll ?? 1) != 0, mapxblock);
    }

    /// <summary>
    /// A background movie for one name (GFxMovieBackground(name, camera flag, pos flag), gfxmoviebackground.cpp:390): the
    /// picture <see cref="FieldBackground.ImageEntry"/> names, sized and moved by the two flags, on a canvas layer under parent
    /// that env draws as its background. Null when the name names no picture in Image.m2d.
    /// STILL-OPEN: a name that is not a .png or .dds goes to loadBG unchanged (a movie of its own); no data uses one.
    /// </summary>
    public static FieldBackdrop Load(Node parent, Godot.Environment env, PackSet packs, string name, bool scroll, bool posFlag, XDocument mapxblock)
    {
        string entry = FieldBackground.ImageEntry(name);
        if (entry == null) { GD.PushWarning($"backdrop {name}: not a picture name"); return null; }
        if (!packs.Exists(Archive, entry)) { GD.PushWarning($"backdrop {entry} not found"); return null; }
        var img = new Image();
        var bytes = packs.Read(Archive, entry);
        var err = entry.EndsWith(".png", StringComparison.Ordinal) ? img.LoadPngFromBuffer(bytes) : img.LoadDdsFromBuffer(bytes);
        if (err != Error.Ok) return null;
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
        var b = new FieldBackdrop(layer, rect, new Vector2(img.GetWidth(), img.GetHeight()), scroll, posFlag, mm) { Entry = entry };
        b.Resize(rect.GetViewportRect().Size);
        return b;
    }

    /// <summary>The picture's entry in Image.m2d.</summary>
    public string Entry { get; private init; }

    /// <summary>Frees the layer: the movie m_mapMovies slot 4 held is replaced (GFxManager::LoadBackgroundMovie, gfxmanager.cpp:174).</summary>
    public void Free()
    {
        if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
    }

    /// <summary>Background.onResize: on load and whenever the window size changes (GFxMovieBackground::vfunc_16 on WM_SIZE).</summary>
    void Resize(Vector2 size)
    {
        uint w = (uint)size.X, h = (uint)size.Y;
        if (w == 0 || h == 0) return;
        stage = size;
        if (scroll)
        {
            var (cw, ch) = FieldBackground.ResizeScroll(w, h);
            content = new Vector2((float)cw, (float)ch);     // onResizeScroll sets the size only
        }
        else
        {
            var (x, y, cw, ch) = FieldBackground.ResizeNoScroll(content.X, content.Y, w, h);
            content = new Vector2((float)cw, (float)ch);
            pos = new Vector2((float)x, (float)y);
        }
        rect.Size = content;
        ApplyPosition();
    }

    void ApplyPosition()
    {
        if (pos == appliedPos) return;
        appliedPos = pos;
        rect.Position = pos;
    }

    /// <summary>
    /// GFxMovieBackground::vfunc_8 (@0x14127a070) for this frame: the camera's world up vector (Godot y-up turned back to the
    /// client's z-up axes: client x = Godot x) and the local player's position.
    /// </summary>
    public void Update(Camera3D camera, Node3D player)
    {
        if (!GodotObject.IsInstanceValid(rect)) return;
        var size = rect.GetViewportRect().Size;
        if (size != stage) Resize(size);
        if (minimap == null || stage.X <= 0 || stage.Y <= 0) return;
        uint w = (uint)stage.X, h = (uint)stage.Y;
        if (scroll && camera != null)
        {
            var up = camera.GlobalBasis.Y.Normalized();
            pos.Y = (float)FieldBackground.CameraY(content.Y, h, FieldBackground.CameraAngle(up.X));
        }
        if (posFlag && player != null)
        {
            var g = player.GlobalPosition;     // FieldBuilder.ToGodotPoint: (x, y, z) -> (x, z, -y)
            if (minimap.ToScreenX(new Vec3(g.X, -g.Z, g.Y), out var sx))
                pos.X = (float)FieldBackground.PosX(content.X, w, sx);
        }
        ApplyPosition();
    }
}
