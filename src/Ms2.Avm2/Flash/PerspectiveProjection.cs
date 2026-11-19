using System.Runtime.CompilerServices;

namespace Ms2.Avm2.Flash;

/// <summary>flash.geom.PerspectiveProjection after Scaleform's Instances::fl_geom::PerspectiveProjection (sfr_as3_11.cpp).
/// Values the display object keeps (field of view, focal length, projection centre) are stored per object by
/// <see cref="AsTransform"/>; there is no 3D renderer behind them.</summary>
[AsClass("flash.geom.PerspectiveProjection")]
public class AsPerspectiveProjection : AsObject
{
    // The defaults: 55 degrees, centre (250, 250), focal length 250 / tan(fov / 2) (the inline ctor).
    double fov = 55, focal = 250.0 / Math.Tan(55 * Math.PI / 180 / 2), cx = 250, cy = 250;
    internal AsDisplayObject Bound;

    // A zero argument keeps the default; the centre only takes when both coordinates are non-zero.
    [AsCtor] void Ctor(object[] a)
    {
        if (a.Length == 0) return;
        double v = Rt.ToNumber(a[0]); if (v != 0) fov = v;
        if (a.Length < 2) return;
        v = Rt.ToNumber(a[1]); if (v != 0) focal = v;
        if (a.Length < 3) return;
        double x = Rt.ToNumber(a[2]), y = a.Length > 3 ? Rt.ToNumber(a[3]) : double.NaN;
        if (x != 0 && y != 0) { cx = (float)x; cy = (float)y; }
    }

    [As] public double fieldOfView { get => fov; set => fov = (float)value; }
    [As] public double focalLength { get => focal; set => focal = (float)value; }
    [As] public AsPoint projectionCenter
    {
        get => AsPoint.New(Class.Runtime, cx, cy);
        set { cx = (float)value.x; cy = (float)value.y; }
    }

    internal double Fov { get => fov; set => fov = value; }
    internal double Focal { get => focal; set => focal = value; }
    internal (double X, double Y) Center { get => (cx, cy); set { cx = value.X; cy = value.Y; } }

    /// <summary>The focal length that shows a stage <paramref name="stageWidth"/> pixels wide at the field of view.</summary>
    internal double CalculateFocalLength(double stageWidth) => (float)stageWidth * 0.5f / Math.Tan(fov * 0.5 * (Math.PI / 180));

    /// <summary>The projection as a Matrix3D: the focal length on the x/y diagonal, z copied into w. Without an explicit focal
    /// length it follows the field of view over the stage width (500 pixels when not on a stage).</summary>
    [As] public AsMatrix3D toMatrix3D()
    {
        float width = Bound?.P != null ? (float)Bound.P.StageW : 500f;
        float f = focal == 0 ? width * 0.5f / MathF.Tan((float)(fov * Math.PI / 180) * 0.5f) : (float)focal;
        var m = Mat4.Identity();
        m[0, 0] = f; m[1, 1] = f; m[3, 2] = 1; m[3, 3] = 0;
        return AsMatrix3D.New(Class.Runtime, m);
    }
}
