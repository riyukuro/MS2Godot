using System.Xml.Linq;
using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// The field's backdrop picture: mapxblock &lt;clientProperty bgDay&gt; names one or more images (comma separated; the
/// client picks one at random, this takes the first) in Image.m2d bg/. The client shows it through a Flash background
/// movie behind the 3D scene, shifted with the camera angle and the player's screen x; here it is a still picture that
/// covers the screen, on a canvas layer the environment draws as its background. Not done: the parallax.
/// </summary>
public static class FieldBackdrop
{
    public const string Archive = "Resource/Image";
    const int Layer = -10;

    /// <summary>Adds the backdrop under parent and switches env to a canvas background; false when there is none.</summary>
    public static bool Add(Node parent, Godot.Environment env, PackSet packs, XDocument mapxblock)
    {
        var names = (string)mapxblock?.Root?.Element("clientProperty")?.Attribute("bgDay");
        var name = names?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(name)) return false;
        string entry = "bg/" + name.ToLowerInvariant();
        if (!packs.Exists(Archive, entry)) { GD.PushWarning($"backdrop {entry} not found"); return false; }
        var img = new Image();
        if (img.LoadDdsFromBuffer(packs.Read(Archive, entry)) != Error.Ok) return false;
        var layer = new CanvasLayer { Name = "Backdrop", Layer = Layer };
        var rect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(img),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/backdrop.gdshader") },
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
        parent.AddChild(layer);
        env.BackgroundMode = Godot.Environment.BGMode.Canvas;
        env.BackgroundCanvasMaxLayer = Layer;
        return true;
    }
}
