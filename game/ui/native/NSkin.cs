using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// Static art: one frame of a movie symbol drawn as a texture (nine-patch when the symbol has a scale-9 grid and the node
/// is resized). The node's position is where the movie places the symbol's origin; with no size set it takes the
/// symbol's own size. <see cref="SkipComponents"/> bakes a container's background without the widgets on it.
/// </summary>
[GlobalClass]
public partial class NSkin : Control
{
    [Export] public string Movie = "";
    [Export] public string Symbol = "";
    [Export] public string FrameLabel = "";
    [Export] public int Frame;
    [Export] public bool SkipComponents;
    /// <summary>Also put the symbol's static text fields on as labels.</summary>
    [Export] public bool ShowTexts;
    [Export] public float Alpha = 1;
    /// <summary>Text for the first text field (a Label component's inspector "text"); a "$key" goes through the string table.</summary>
    [Export] public string Text = "";
    /// <summary>Children the dialog's code hides (visible = false), comma separated names or dotted paths.</summary>
    [Export] public string Hide = "";

    public NativeSkin Skin { get; private set; }
    public readonly Dictionary<string, Label> Labels = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (NativeUi.Art == null) return;
        int frame = Frame;
        if (!string.IsNullOrEmpty(FrameLabel))
        {
            var (labels, frames) = NativeUi.Art.Timeline(Movie, Symbol);
            int f = NativeArt.RestFrame(labels, frames, FrameLabel);
            if (f >= 0) frame = f;
        }
        Skin = NativeUi.Art.Bake(Movie, Symbol, frame, SkipComponents, 1, Hide);
        if (Skin == null) return;
        if (Size == Vector2.Zero) Size = Skin.Size;
        if (ShowTexts)
            foreach (var t in Skin.Texts) Labels[t.Name] = NativeUi.PlaceLabel(this, t, Vector2.Zero, Labels.Count == 0 && Text != "" ? NativeUi.Str(Text) : null);
        QueueRedraw();
    }

    /// <summary>Where the skin's top-left lands in this node (the symbol's origin may not be its top-left).</summary>
    public Vector2 Origin => Skin == null ? Vector2.Zero : Skin.Origin * Stretch;

    Vector2 Stretch => Skin == null || Skin.Size.X <= 0 || Skin.Size.Y <= 0 ? Vector2.One : new Vector2(Size.X / Skin.Size.X, Size.Y / Skin.Size.Y);

    public override void _Draw()
    {
        if (Skin == null) return;
        var s = Stretch;
        var grid = Skin.Grid != null;
        // a grid keeps the origin offset unscaled (corners keep their size)
        var at = grid ? Skin.Origin : Skin.Origin * s;
        Skin.Draw(this, new Rect2(at, Size), new Color(1, 1, 1, Alpha));
    }
}
