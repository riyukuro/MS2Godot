using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Model;

namespace Ms2.Formats.World;

/// <summary>
/// The &lt;minimap&gt; block of Xml.m2d : mapxblock/&lt;xblock&gt;.xml: the field's illustrated map image and the orthographic
/// camera it was drawn from. <see cref="ToImage"/> maps a world point onto the image as the client's minimap viewport does:
/// project with the camera axes, map the frustum onto the screen rectangle, then into the image rectangle.
/// </summary>
public sealed class MinimapInfo
{
    public string Image = "";                                   // e.g. ./Data/Resource/Image/Map/Minimap/02000001.dds
    public double ImgLeft, ImgRight, ImgTop, ImgBottom;         // the image's rectangle in screen units
    public double FrLeft, FrRight, FrTop, FrBottom;             // orthographic frustum
    public double ScrLeft, ScrRight = 1920, ScrTop, ScrBottom = 1200;
    public Vec3 CamPos;
    public Vec3 CamRotDeg;

    /// <summary>The image entry inside the Image archive, e.g. "map/minimap/02000001.dds".</summary>
    public string ImageEntry
    {
        get
        {
            var p = Image.Replace('\\', '/');
            int i = p.IndexOf("/Image/", StringComparison.OrdinalIgnoreCase);
            return (i >= 0 ? p[(i + 7)..] : p).ToLowerInvariant();
        }
    }

    public static MinimapInfo Parse(XDocument xblock)
    {
        var m = xblock.Root?.Element("minimap");
        if (m == null) return null;
        var o = new MinimapInfo();
        var img = m.Element("image"); var fr = m.Element("frustum"); var scr = m.Element("screen"); var cam = m.Element("camera");
        if (img == null || fr == null || cam == null) return null;
        o.Image = (string)img.Attribute("name") ?? "";
        o.ImgLeft = D(img, "left"); o.ImgRight = D(img, "right"); o.ImgTop = D(img, "top"); o.ImgBottom = D(img, "bottom");
        o.FrLeft = D(fr, "left"); o.FrRight = D(fr, "right"); o.FrTop = D(fr, "top"); o.FrBottom = D(fr, "bottom");
        if (scr != null) { o.ScrLeft = D(scr, "left"); o.ScrRight = D(scr, "right"); o.ScrTop = D(scr, "top"); o.ScrBottom = D(scr, "bottom"); }
        o.CamPos = V((string)cam.Attribute("position"));
        o.CamRotDeg = V((string)cam.Attribute("rotation"));
        return o;
    }

    /// <summary>
    /// Camera axes (right, forward, up). The rotation is Gamebryo's FromEulerAnglesXYZ (its matrices rotate the other way,
    /// so each factor is transposed); the camera looks down the matrix's first column, up is minus the second, right minus the third.
    /// </summary>
    public (Vec3 right, Vec3 forward, Vec3 up) Axes()
    {
        double r = Math.PI / 180;
        var m = Mul(Mul(RotX(CamRotDeg.X * r), RotY(CamRotDeg.Y * r)), RotZ(CamRotDeg.Z * r));
        var fwd = new Vec3(m[0, 0], m[1, 0], m[2, 0]);
        var up = new Vec3(-m[0, 1], -m[1, 1], -m[2, 1]);
        var right = new Vec3(-m[0, 2], -m[1, 2], -m[2, 2]);
        return (right, fwd, up);
    }

    /// <summary>A world point as a fraction of the image (0..1 left to right, top to bottom).</summary>
    public (double u, double v) ToImage(Vec3 p)
    {
        var (right, _, up) = Axes();
        var d = new Vec3(p.X - CamPos.X, p.Y - CamPos.Y, p.Z - CamPos.Z);
        double fx = (Dot(right, d) - FrLeft) / (FrRight - FrLeft);
        double fy = (Dot(up, d) - FrBottom) / (FrTop - FrBottom);
        double sx = ScrLeft + fx * (ScrRight - ScrLeft), sy = ScrTop + (1 - fy) * (ScrBottom - ScrTop);
        return ((sx - ImgLeft) / (ImgRight - ImgLeft), (sy - ImgTop) / (ImgBottom - ImgTop));
    }

    static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // transposed (clockwise) rotations, as Gamebryo's NiMatrix3::MakeXRotation etc.
    static double[,] RotX(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new[,] { { 1, 0, 0 }, { 0, c, s }, { 0, -s, c } }; }
    static double[,] RotY(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new[,] { { c, 0, -s }, { 0, 1, 0 }, { s, 0, c } }; }
    static double[,] RotZ(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new[,] { { c, s, 0 }, { -s, c, 0 }, { 0, 0, 1 } }; }

    static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    static double D(XElement e, string a) => double.Parse((string)e.Attribute(a) ?? "0", CultureInfo.InvariantCulture);

    static Vec3 V(string s)
    {
        var p = (s ?? "0,0,0").Split(',').Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
        return new Vec3(p[0], p[1], p[2]);
    }
}
