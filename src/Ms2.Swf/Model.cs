namespace Ms2.Swf;

/// <summary>Rectangle in pixels (twips / 20).</summary>
public sealed record Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public static Rect Union(Rect a, Rect b)
    {
        if (a == null) return b;
        if (b == null) return a;
        double x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
        double x1 = Math.Max(a.Right, b.Right), y1 = Math.Max(a.Bottom, b.Bottom);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>2D affine transform, translation in pixels. x' = A x + C y + Tx, y' = B x + D y + Ty.</summary>
public sealed record Matrix(double A, double B, double C, double D, double Tx, double Ty)
{
    public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);
    public bool IsIdentity => this == Identity;

    public (double, double) Apply(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);

    /// <summary>this applied after <paramref name="inner"/> (inner is the child's own matrix).</summary>
    public Matrix Compose(Matrix inner) => new(
        A * inner.A + C * inner.B, B * inner.A + D * inner.B,
        A * inner.C + C * inner.D, B * inner.C + D * inner.D,
        A * inner.Tx + C * inner.Ty + Tx, B * inner.Tx + D * inner.Ty + Ty);

    public Rect Transform(Rect r)
    {
        if (r == null) return null;
        var pts = new[] { Apply(r.X, r.Y), Apply(r.Right, r.Y), Apply(r.X, r.Bottom), Apply(r.Right, r.Bottom) };
        double x0 = pts.Min(p => p.Item1), x1 = pts.Max(p => p.Item1);
        double y0 = pts.Min(p => p.Item2), y1 = pts.Max(p => p.Item2);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>Colour transform: out = in * Mult + Add, mult 0..1 (can exceed), add 0..255.</summary>
public sealed record ColorXform(double RMul, double GMul, double BMul, double AMul, int RAdd, int GAdd, int BAdd, int AAdd);

public sealed record Rgba(byte R, byte G, byte B, byte A)
{
    public string Hex => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";
}

/// <summary>A PlaceObject3 filter. Angle in radians, distance in pixels, strength as a multiplier; Passes is the blur quality.</summary>
public sealed record FilterInfo(string Type, string Color, double BlurX, double BlurY, double Angle = 0, double Distance = 0,
                                double Strength = 1, int Passes = 1, bool Inner = false, bool Knockout = false);

/// <summary>One PlaceObject/PlaceObject2/PlaceObject3 record.</summary>
public sealed class Placement
{
    public int Depth;
    public int? CharacterId;
    public bool Move;
    public Matrix Matrix;
    public ColorXform Color;
    public int? Ratio;
    public string Name;
    public int? ClipDepth;
    public string ClassName;
    public int? BlendMode;
    public bool? Visible;
    public bool? CacheAsBitmap;
    public List<FilterInfo> Filters;
}

public sealed class FrameLabel { public int Frame; public string Name; }

/// <summary>A sprite or the root timeline: control tags only.</summary>
public sealed class Timeline
{
    public int FrameCount;
    /// <summary>Per frame (0-based) the placements and removals executed before that frame's ShowFrame.</summary>
    public List<List<TimelineOp>> Frames = new();
    public List<FrameLabel> Labels = new();
}

public sealed class TimelineOp
{
    public Placement Place;     // null for a removal
    public int RemoveDepth;
}

public enum CharKind { Shape, Sprite, EditText, Button, Image, SubImage, Font, MorphShape, StaticText, Video, Sound, Binary, Other }

public sealed class Character
{
    public int Id;
    public CharKind Kind;
    public int TagCode;
    public Rect Bounds;                 // own bounds (shapes, texts); computed for sprites and buttons
    public string ExportName, ClassName;
    public Rect Scale9;
    public bool AaReadable;             // CSMTextSettings (tag 74) names this text: drawn for readability
    // shape
    public List<BitmapFill> BitmapFills;
    public Rgba SolidFill;
    public List<VectorPath> Paths;      // solid-colour vector fills (flattened polygons in pixels)
    // sprite
    public Timeline Timeline;
    // text
    public EditText Text;
    // button
    public List<ButtonRecord> Button;
    public bool ButtonTrackAsMenu;
    // font / image
    public string FontName;
    public FontData Font;               // DefineFont2/3 glyph outlines
    public string ImageFile;            // external GFx image file (atlas)
    public int? AtlasImageId;           // sub image: id of the atlas image
    public Rect AtlasRect;              // sub image: region in atlas pixels
    public int ImageWidth, ImageHeight;
}

public sealed record VectorPath(Rgba Color, float[] Points);

public sealed record BitmapFill(int BitmapId, Matrix Matrix, bool Smooth, bool Repeat);

public sealed class EditText
{
    public int? FontId;
    public string FontClass;
    public string FontName;
    public FontData Font;               // DefineFont2/3 glyph outlines
    public double? Size;                // pixels
    public Rgba Color;
    public int? MaxLength;
    public string Align;                // left right center justify
    public double LeftMargin, RightMargin, Indent, Leading;
    public string Variable;
    public string InitialText;
    public bool WordWrap, Multiline, Password, ReadOnly, AutoSize, NoSelect, Border, Html, UseOutlines;
}

public sealed class ButtonRecord
{
    public bool Up, Over, Down, Hit;
    public int CharacterId, Depth;
    public Matrix Matrix;
    public ColorXform Color;
}

public sealed class ImportGroup { public string Url; public List<(int Id, string Name)> Assets = new(); }

public sealed class TagStat { public int Parsed; public int Skipped; public int Failed; }

/// <summary>Outline of one glyph: closed contours of points; Off marks quadratic control points. Units are the font's EM square.</summary>
public sealed class Glyph
{
    public ushort Code;
    public int Advance;
    public List<List<(int X, int Y, bool On)>> Contours = new();
}

/// <summary>A DefineFont2/3 font. Y grows downward as in SWF; EmSquare is 20480 for DefineFont3, 1024 for DefineFont2.</summary>
public sealed class FontData
{
    public string Name;
    public bool Bold, Italic, HasLayout;
    public int EmSquare = 20480, Ascent, Descent, Leading;
    public List<Glyph> Glyphs = new();
}
