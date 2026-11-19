using System.Runtime.CompilerServices;

namespace Ms2.Avm2.Flash;

/// <summary>A 2D affine matrix in Flash order: x' = a*x + c*y + tx, y' = b*x + d*y + ty.</summary>
public struct Mat
{
    public double A, B, C, D, Tx, Ty;
    public static readonly Mat Identity = new() { A = 1, D = 1 };
    public Mat(double a, double b, double c, double d, double tx, double ty) { A = a; B = b; C = c; D = d; Tx = tx; Ty = ty; }
    /// <summary>this applied after <paramref name="inner"/>: (this * inner)(p) = this(inner(p)).</summary>
    public Mat Then(Mat outer) => new(
        outer.A * A + outer.C * B, outer.B * A + outer.D * B,
        outer.A * C + outer.C * D, outer.B * C + outer.D * D,
        outer.A * Tx + outer.C * Ty + outer.Tx, outer.B * Tx + outer.D * Ty + outer.Ty);
    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);
    public Mat Inverse()
    {
        double det = A * D - B * C;
        if (Math.Abs(det) < 1e-12) return new Mat(0, 0, 0, 0, -Tx, -Ty);
        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        return new(ia, ib, ic, id, -(ia * Tx + ic * Ty), -(ib * Tx + id * Ty));
    }
    public RectD Apply(RectD r)
    {
        if (r.IsEmpty) return r;
        var (x0, y0) = Apply(r.X, r.Y); var (x1, y1) = Apply(r.X + r.W, r.Y); var (x2, y2) = Apply(r.X, r.Y + r.H); var (x3, y3) = Apply(r.X + r.W, r.Y + r.H);
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new RectD(minX, minY, maxX - minX, maxY - minY);
    }
}

public struct RectD
{
    public double X, Y, W, H;
    bool valid;
    /// <summary>No area at all (as opposed to a zero-size rectangle at a point).</summary>
    public bool IsEmpty => !valid;
    public static readonly RectD None = default;
    public RectD(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; valid = true; }
    public bool Contains(double x, double y) => !IsEmpty && x >= X && x <= X + W && y >= Y && y <= Y + H;
    public RectD Union(RectD o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        double x0 = Math.Min(X, o.X), y0 = Math.Min(Y, o.Y), x1 = Math.Max(X + W, o.X + o.W), y1 = Math.Max(Y + H, o.Y + o.H);
        return new RectD(x0, y0, x1 - x0, y1 - y0);
    }
    public override string ToString() => IsEmpty ? "(empty)" : $"({X:0.#},{Y:0.#} {W:0.#}x{H:0.#})";
}

[AsClass("flash.geom.Point")]
public class AsPoint : AsObject
{
    [As] public double x { get; set; }
    [As] public double y { get; set; }
    [AsCtor] void Ctor(object[] a) { x = a.Length > 0 ? Rt.ToNumber(a[0]) : 0; y = a.Length > 1 ? Rt.ToNumber(a[1]) : 0; }
    public static AsPoint New(Runtime rt, double x, double y) { var p = (AsPoint)rt.GetClass("flash.geom.Point").Construct(); p.x = x; p.y = y; return p; }
    [As] public double length => Math.Sqrt(x * x + y * y);
    [As] public AsPoint add(AsPoint v) => New(Class.Runtime, x + v.x, y + v.y);
    [As] public AsPoint subtract(AsPoint v) => New(Class.Runtime, x - v.x, y - v.y);
    [As] public AsPoint clone() => New(Class.Runtime, x, y);
    [As] public bool equals(AsPoint p) => p != null && p.x == x && p.y == y;
    [As] public void offset(double dx, double dy) { x += dx; y += dy; }
    [As] public void normalize(double len) { var l = length; if (l > 0) { x = x / l * len; y = y / l * len; } }
    [As] public void setTo(double xa, double ya) { x = xa; y = ya; }
    [As] public void copyFrom(AsPoint p) { x = p.x; y = p.y; }
    [As("toString")] public string ToAsString() => $"(x={Rt.NumberToString(x)}, y={Rt.NumberToString(y)})";
    [As] public static double distance(AsPoint a, AsPoint b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    [As] public static AsPoint interpolate(AsPoint a, AsPoint b, double f) => New(a.Class.Runtime, b.x + (a.x - b.x) * f, b.y + (a.y - b.y) * f);
    [As] public static AsPoint polar(double len, double ang) => New(Runtime.Current, len * Math.Cos(ang), len * Math.Sin(ang));
}

[AsClass("flash.geom.Rectangle")]
public class AsRectangle : AsObject
{
    [As] public double x { get; set; }
    [As] public double y { get; set; }
    [As] public double width { get; set; }
    [As] public double height { get; set; }
    [AsCtor] void Ctor(object[] a) { x = N(a, 0); y = N(a, 1); width = N(a, 2); height = N(a, 3); }
    static double N(object[] a, int i) => a.Length > i ? Rt.ToNumber(a[i]) : 0;
    public static AsRectangle New(Runtime rt, double x, double y, double w, double h) { var r = (AsRectangle)rt.GetClass("flash.geom.Rectangle").Construct(); r.x = x; r.y = y; r.width = w; r.height = h; return r; }
    public static AsRectangle New(Runtime rt, RectD r) => r.IsEmpty ? New(rt, 0, 0, 0, 0) : New(rt, r.X, r.Y, r.W, r.H);
    public RectD R => new(x, y, width, height);
    Runtime RT => Class.Runtime;
    [As] public double left { get => x; set { width += x - value; x = value; } }
    [As] public double top { get => y; set { height += y - value; y = value; } }
    [As] public double right { get => x + width; set => width = value - x; }
    [As] public double bottom { get => y + height; set => height = value - y; }
    [As] public AsPoint topLeft { get => AsPoint.New(RT, x, y); set { left = value.x; top = value.y; } }
    [As] public AsPoint bottomRight { get => AsPoint.New(RT, right, bottom); set { right = value.x; bottom = value.y; } }
    [As] public AsPoint size { get => AsPoint.New(RT, width, height); set { width = value.x; height = value.y; } }
    [As] public bool isEmpty() => width <= 0 || height <= 0;
    [As] public void setEmpty() { x = y = width = height = 0; }
    [As] public AsRectangle clone() => New(RT, x, y, width, height);
    [As] public bool contains(double px, double py) => px >= x && px < x + width && py >= y && py < y + height;
    [As] public bool containsPoint(AsPoint p) => contains(p.x, p.y);
    [As] public bool containsRect(AsRectangle r) => r.x >= x && r.y >= y && r.right <= right && r.bottom <= bottom;
    [As] public bool intersects(AsRectangle r) => r.x < right && r.right > x && r.y < bottom && r.bottom > y;
    [As] public AsRectangle intersection(AsRectangle r)
    {
        double x0 = Math.Max(x, r.x), y0 = Math.Max(y, r.y), x1 = Math.Min(right, r.right), y1 = Math.Min(bottom, r.bottom);
        return x1 > x0 && y1 > y0 ? New(RT, x0, y0, x1 - x0, y1 - y0) : New(RT, 0, 0, 0, 0);
    }
    [As] public AsRectangle union(AsRectangle r)
    {
        if (isEmpty()) return r.clone(); if (r.isEmpty()) return clone();
        double x0 = Math.Min(x, r.x), y0 = Math.Min(y, r.y), x1 = Math.Max(right, r.right), y1 = Math.Max(bottom, r.bottom);
        return New(RT, x0, y0, x1 - x0, y1 - y0);
    }
    [As] public void inflate(double dx, double dy) { x -= dx; y -= dy; width += 2 * dx; height += 2 * dy; }
    [As] public void offset(double dx, double dy) { x += dx; y += dy; }
    [As] public void offsetPoint(AsPoint p) { x += p.x; y += p.y; }
    [As] public bool equals(AsRectangle r) => r != null && r.x == x && r.y == y && r.width == width && r.height == height;
    [As] public void setTo(double xa, double ya, double w, double h) { x = xa; y = ya; width = w; height = h; }
    [As] public void copyFrom(AsRectangle r) { x = r.x; y = r.y; width = r.width; height = r.height; }
    [As("toString")] public string ToAsString() => $"(x={Rt.NumberToString(x)}, y={Rt.NumberToString(y)}, w={Rt.NumberToString(width)}, h={Rt.NumberToString(height)})";
}

[AsClass("flash.geom.Matrix")]
public class AsMatrix : AsObject
{
    [As] public double a { get; set; } = 1;
    [As] public double b { get; set; }
    [As] public double c { get; set; }
    [As] public double d { get; set; } = 1;
    [As] public double tx { get; set; }
    [As] public double ty { get; set; }
    [AsCtor] void Ctor(object[] x)
    {
        a = x.Length > 0 ? Rt.ToNumber(x[0]) : 1; b = x.Length > 1 ? Rt.ToNumber(x[1]) : 0; c = x.Length > 2 ? Rt.ToNumber(x[2]) : 0;
        d = x.Length > 3 ? Rt.ToNumber(x[3]) : 1; tx = x.Length > 4 ? Rt.ToNumber(x[4]) : 0; ty = x.Length > 5 ? Rt.ToNumber(x[5]) : 0;
    }
    public Mat M { get => new(a, b, c, d, tx, ty); set { a = value.A; b = value.B; c = value.C; d = value.D; tx = value.Tx; ty = value.Ty; } }
    public static AsMatrix New(Runtime rt, Mat m) { var r = (AsMatrix)rt.GetClass("flash.geom.Matrix").Construct(); r.M = m; return r; }
    [As] public void identity() => M = Mat.Identity;
    [As] public void translate(double dx, double dy) { tx += dx; ty += dy; }
    [As] public void scale(double sx, double sy) => M = M.Then(new Mat(sx, 0, 0, sy, 0, 0));
    [As] public void rotate(double r) => M = M.Then(new Mat(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0));
    [As] public void concat(AsMatrix m) => M = M.Then(m.M);
    [As] public void invert() => M = M.Inverse();
    [As] public AsMatrix clone() => New(Class.Runtime, M);
    [As] public AsPoint transformPoint(AsPoint p) { var (x, y) = M.Apply(p.x, p.y); return AsPoint.New(Class.Runtime, x, y); }
    [As] public AsPoint deltaTransformPoint(AsPoint p) => AsPoint.New(Class.Runtime, a * p.x + c * p.y, b * p.x + d * p.y);
    [As] public void createBox(double sx, double sy, double rot = 0, double dx = 0, double dy = 0)
    {
        double cs = Math.Cos(rot), sn = Math.Sin(rot);
        M = new Mat(sx * cs, sx * sn, -sy * sn, sy * cs, dx, dy);
    }
    [As] public void createGradientBox(double w, double h, double rot = 0, double dx = 0, double dy = 0) => createBox(w / 1638.4, h / 1638.4, rot, dx + w / 2, dy + h / 2);
    [As] public void setTo(double aa, double bb, double cc, double dd, double x, double y) => M = new Mat(aa, bb, cc, dd, x, y);
    [As] public void copyFrom(AsMatrix m) => M = m.M;
    [As("toString")] public string ToAsString() => $"(a={a}, b={b}, c={c}, d={d}, tx={tx}, ty={ty})";
}

[AsClass("flash.geom.ColorTransform")]
public class AsColorTransform : AsObject
{
    [As] public double redMultiplier { get; set; } = 1;
    [As] public double greenMultiplier { get; set; } = 1;
    [As] public double blueMultiplier { get; set; } = 1;
    [As] public double alphaMultiplier { get; set; } = 1;
    [As] public double redOffset { get; set; }
    [As] public double greenOffset { get; set; }
    [As] public double blueOffset { get; set; }
    [As] public double alphaOffset { get; set; }
    [AsCtor] void Ctor(object[] a)
    {
        double N(int i, double d) => a.Length > i ? Rt.ToNumber(a[i]) : d;
        redMultiplier = N(0, 1); greenMultiplier = N(1, 1); blueMultiplier = N(2, 1); alphaMultiplier = N(3, 1);
        redOffset = N(4, 0); greenOffset = N(5, 0); blueOffset = N(6, 0); alphaOffset = N(7, 0);
    }
    [As] public uint color
    {
        get => (uint)(((int)redOffset & 255) << 16 | ((int)greenOffset & 255) << 8 | ((int)blueOffset & 255));
        set { redMultiplier = greenMultiplier = blueMultiplier = 0; redOffset = value >> 16 & 255; greenOffset = value >> 8 & 255; blueOffset = value & 255; }
    }
    [As] public void concat(AsColorTransform o)
    {
        redOffset += redMultiplier * o.redOffset; greenOffset += greenMultiplier * o.greenOffset; blueOffset += blueMultiplier * o.blueOffset; alphaOffset += alphaMultiplier * o.alphaOffset;
        redMultiplier *= o.redMultiplier; greenMultiplier *= o.greenMultiplier; blueMultiplier *= o.blueMultiplier; alphaMultiplier *= o.alphaMultiplier;
    }
    public Cx Value
    {
        get => new(redMultiplier, greenMultiplier, blueMultiplier, alphaMultiplier, redOffset, greenOffset, blueOffset, alphaOffset);
        set { redMultiplier = value.Rm; greenMultiplier = value.Gm; blueMultiplier = value.Bm; alphaMultiplier = value.Am; redOffset = value.Ra; greenOffset = value.Ga; blueOffset = value.Ba; alphaOffset = value.Aa; }
    }
    [As("toString")] public string ToAsString() => $"(redMultiplier={redMultiplier}, greenMultiplier={greenMultiplier}, blueMultiplier={blueMultiplier}, alphaMultiplier={alphaMultiplier}, redOffset={redOffset}, greenOffset={greenOffset}, blueOffset={blueOffset}, alphaOffset={alphaOffset})";
}

/// <summary>A colour transform value (multipliers and offsets, offsets in 0..255).</summary>
public readonly record struct Cx(double Rm, double Gm, double Bm, double Am, double Ra, double Ga, double Ba, double Aa)
{
    public static readonly Cx Identity = new(1, 1, 1, 1, 0, 0, 0, 0);
    public Cx Then(Cx outer) => new(Rm * outer.Rm, Gm * outer.Gm, Bm * outer.Bm, Am * outer.Am,
        Ra * outer.Rm + outer.Ra, Ga * outer.Gm + outer.Ga, Ba * outer.Bm + outer.Ba, Aa * outer.Am + outer.Aa);
}

[AsClass("flash.geom.Transform")]
public class AsTransform : AsObject
{
    internal AsDisplayObject Owner;
    [AsCtor] void Ctor(object[] a) { Owner = a.Length > 0 ? a[0] as AsDisplayObject : null; }
    // Not nulled in 3D: Transform::matrixGet/Set keep working on the 2D matrix of a 3D object (the 3D state sits in matrix3D).
    [As] public AsMatrix matrix { get => AsMatrix.New(Class.Runtime, Owner?.Matrix ?? Mat.Identity); set { if (Owner != null && value != null) { Owner.Matrix = value.M; Owner.ScriptMoved(); } } }
    [As] public AsColorTransform colorTransform
    {
        get { var c = (AsColorTransform)Class.Runtime.GetClass("flash.geom.ColorTransform").Construct(); c.Value = Owner?.Color ?? Cx.Identity; return c; }
        set { if (Owner != null && value != null) { Owner.Color = value.Value; Owner.ScriptColored(); } }
    }
    [As] public AsMatrix concatenatedMatrix => AsMatrix.New(Class.Runtime, Owner?.ToGlobalMatrix() ?? Mat.Identity);
    [As] public AsColorTransform concatenatedColorTransform => colorTransform;
    // The display object's perspective data (PerspectiveDataType): field of view, focal length and projection centre, 0 until set.
    internal sealed class PerspData { public double Fov, Focal, Cx, Cy; }
    static readonly ConditionalWeakTable<AsDisplayObject, PerspData> perspective = new();

    /// <summary>The object's perspective data, made when missing (DisplayObjectBase::SetFOV, SetFocalLength, SetProjectionCenter, sfr_gfx_06.cpp:1045-1645).</summary>
    internal static PerspData PerspectiveOf(AsDisplayObject o) => perspective.GetValue(o, _ => new PerspData());

    /// <summary>SetFOV(55) on the root movie and on a movie loaded by a Loader (sfr_as3_17d.cpp:741, sfr_as3_18.cpp:8135, 8494).</summary>
    public static void InitFov(AsDisplayObject o) { if (o != null) PerspectiveOf(o).Fov = 55; }

    /// <summary>The object's perspective data as a new PerspectiveProjection bound to it, or null when none was set
    /// (Transform::perspectiveProjectionGet, @0x14248bcc0). On the root with no centre the centre is the middle of the
    /// stage and the focal length follows the stage width.</summary>
    [As] public AsPerspectiveProjection perspectiveProjection
    {
        get
        {
            if (Owner == null) return null;
            // The root movie starts with a 55 degree field of view, so it always has perspective data.
            if (!perspective.TryGetValue(Owner, out var d))
            {
                if (Owner != Owner.P?.Root) return null;
                d = PerspectiveOf(Owner); d.Fov = 55;
            }
            var rt = Class.Runtime;
            var pp = (AsPerspectiveProjection)rt.GetClass("flash.geom.PerspectiveProjection").Construct(d.Fov, d.Focal, d.Cx, d.Cy);
            pp.Bound = Owner;
            if (Owner == Owner.P?.Root)
            {
                if (d.Cx == 0 && d.Cy == 0) pp.Center = (Owner.P.StageW / 2, Owner.P.StageH / 2);
                pp.Focal = pp.CalculateFocalLength(Owner.P.StageW);
            }
            return pp;
        }
        // Transform::perspectiveProjectionSet (@0x14248c0d0): the focal length is recomputed from the stage width, then
        // the projection and its data bind to the object.
        set
        {
            if (value == null || Owner == null) return;
            if (Owner.P != null) value.Focal = value.CalculateFocalLength(Owner.P.StageW);
            value.Bound = Owner;
            var d = PerspectiveOf(Owner);
            d.Fov = value.Fov; d.Focal = value.Focal; d.Cx = value.Center.X; d.Cy = value.Center.Y;
        }
    }

    const double TwipsPerPixel = 20;

    /// <summary>The object's 3D matrix (Transform::matrix3DGet, @0x14248ac20), built from the object each read: the 2D
    /// matrix's a, b, c, d (or, with rotationX/Y or scaleZ set, the full rotation and scales) and the position and z in twips (the getter hands the display object's twips over unconverted,
    /// where the setter takes pixels). The runtime has no 3D scene: nothing projects it. The matrix is not bound to the object (Scaleform binds pDispObj, sfr_as3_11.cpp:1939, so its append*/prepend* move the object; logged as a gap).</summary>
    [As] public AsMatrix3D matrix3D
    {
        get
        {
            if (Owner == null) return null;
            var o = Owner.Matrix;
            var m = Mat4.Identity();
            m[0, 0] = o.A; m[1, 0] = o.B; m[0, 1] = o.C; m[1, 1] = o.D;
            if (Owner.Held3x3 is { } held)
            {
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = held[i, j];     // the last matrix3D set, until a setter replaces it
            }
            else if (Owner.rotationX != 0 || Owner.rotationY != 0 || Owner.scaleZ != 1)
            {
                // M34 as UpdateTransform3D builds it from the properties (sfr_gfx_06.cpp:1903-1920): T * (Rz * Ry) * (Rx * S),
                // Matrix3F(a, b) = a * b (MultiplyMatrix, sfr_render_03.cpp:80-95). (Ry * Rz * Rx is only the order
                // GetEulerAngles decodes, render_matrix4x4.h:228-252.)
                double rx = Owner.rotationX * Math.PI / 180, ry = Owner.rotationY * Math.PI / 180, rz = Owner.rotation * Math.PI / 180;
                double cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz);
                double[,] r =
                {
                    { cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx },
                    { sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx },
                    { -sy, cy * sx, cy * cx },
                };
                double[] sc = { Owner.scaleX, Owner.scaleY, Owner.scaleZ };
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = r[i, j] * sc[j];
            }
            m[0, 3] = o.Tx * TwipsPerPixel; m[1, 3] = o.Ty * TwipsPerPixel; m[2, 3] = Owner.z * TwipsPerPixel;
            return AsMatrix3D.New(Class.Runtime, m);
        }
        // Transform::matrix3DSet (@0x14248af20): the translation (pixels, taken as twips), the rotations and the scales go to
        // the object; null drops the 3D state. Either way the object stops following the timeline's moves.
        set
        {
            if (Owner == null) return;
            if (value != null)
            {
                var m = value.Mat;
                // Matrix3F m(m4); m.Tx() = PixelsToTwips(...): float twips, stored as int twips
                double tx = (int)(float)(m[0, 3] * TwipsPerPixel), ty = (int)(float)(m[1, 3] * TwipsPerPixel), tz = (int)(float)(m[2, 3] * TwipsPerPixel);
                double xs = Math.Sqrt(m[0, 0] * m[0, 0] + m[1, 0] * m[1, 0] + m[2, 0] * m[2, 0]);
                double ys = Math.Sqrt(m[0, 1] * m[0, 1] + m[1, 1] * m[1, 1] + m[2, 1] * m[2, 1]);
                double zs = Math.Sqrt(m[0, 2] * m[0, 2] + m[1, 2] * m[1, 2] + m[2, 2] * m[2, 2]);
                Euler(m, xs, ys, zs, out var rx, out var ry, out var rz);
                Owner.x = tx / TwipsPerPixel; Owner.y = ty / TwipsPerPixel; Owner.z = tz / TwipsPerPixel;
                Owner.rotation = rz * 180 / Math.PI;
                Owner.rotationX = rx * 180 / Math.PI; Owner.rotationY = ry * 180 / Math.PI;
                Owner.scaleX = xs; Owner.scaleY = ys; Owner.scaleZ = zs;
                var keep = new double[3, 3];
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) keep[i, j] = m[i, j];
                Owner.Held3x3 = keep; Owner.Is3D = true;      // after the property writes, which clear it
            }
            else if (Owner.Is3D || Owner.z != 0 || Owner.rotationX != 0 || Owner.rotationY != 0 || Owner.scaleZ != 1)
            {
                // Clear3D (sfr_gfx_06.cpp:191-201, sfr_render_03c.cpp:395-399): the geom goes back to its defaults and the
                // M34 to the identity, so the 2D values go too (Flash keeps them)
                Owner.Matrix = Mat.Identity;
                Owner.z = 0; Owner.rotationX = 0; Owner.rotationY = 0; Owner.scaleZ = 1; Owner.Held3x3 = null; Owner.Is3D = false;
            }
            Owner.ScriptMoved();
        }
    }

    /// <summary>Matrix3x4::GetEulerAngles (render_matrix4x4.h): rotations about x, y and z of the unit-scaled matrix; near
    /// M[1][0] = +-1 the z angle is a quarter turn and x is folded into y.</summary>
    static void Euler(Mat4 m, double xs, double ys, double zs, out double ex, out double ey, out double ez)
    {
        double m00 = m[0, 0] / xs, m10 = m[1, 0] / xs, m20 = m[2, 0] / xs;
        double m11 = m[1, 1] / ys, m02 = m[0, 2] / zs, m12 = m[1, 2] / zs, m22 = m[2, 2] / zs;
        if (m10 > 0.998 || m10 < -0.998) { ey = Math.Atan2(m02, m22); ez = m10 > 0 ? Math.PI / 2 : -Math.PI / 2; ex = 0; return; }
        ey = Math.Atan2(-m20, m00); ex = Math.Atan2(-m12, m11); ez = Math.Asin(m10);
    }

    [As] public AsRectangle pixelBounds => AsRectangle.New(Class.Runtime, Owner?.GetBounds(null) ?? RectD.None);
}
