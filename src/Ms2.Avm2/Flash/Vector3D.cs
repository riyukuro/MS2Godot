using System.Runtime.CompilerServices;

namespace Ms2.Avm2.Flash;

/// <summary>flash.geom.Vector3D after Scaleform's Instances::fl_geom::Vector3D (sfr_as3_11.cpp). A null argument is the
/// null-object TypeError the interpreter already raises for a NullReferenceException.</summary>
[AsClass("flash.geom.Vector3D")]
public class AsVector3D : AsObject
{
    [As] public double x { get; set; }
    [As] public double y { get; set; }
    [As] public double z { get; set; }
    [As] public double w { get; set; }

    // AS3 constructor: arguments convert in order and the first failing one stops the rest (they stay 0).
    [AsCtor] void Ctor(object[] a)
    {
        if (a.Length > 0) { x = Rt.ToNumber(a[0]); if (a.Length > 1) { y = Rt.ToNumber(a[1]); if (a.Length > 2) { z = Rt.ToNumber(a[2]); if (a.Length > 3) w = Rt.ToNumber(a[3]); } } }
    }

    public static AsVector3D New(Runtime rt, double x, double y, double z, double w = 0)
    {
        var v = (AsVector3D)rt.GetClass("flash.geom.Vector3D").Construct();
        v.x = x; v.y = y; v.z = z; v.w = w;
        return v;
    }

    Runtime RT => Class.Runtime;

    [As] public double length => Math.Sqrt(x * x + y * y + z * z);
    [As] public double lengthSquared => x * x + y * y + z * z;
    [As] public AsVector3D add(AsVector3D v) => New(RT, v.x + x, v.y + y, v.z + z);
    [As] public AsVector3D subtract(AsVector3D v) => New(RT, x - v.x, y - v.y, z - v.z);
    [As] public AsVector3D clone() => New(RT, x, y, z, w);
    [As] public AsVector3D crossProduct(AsVector3D v) => New(RT, y * v.z - z * v.y, z * v.x - x * v.z, x * v.y - y * v.x);
    [As] public double dotProduct(AsVector3D v) => x * v.x + y * v.y + z * v.z;
    [As] public void incrementBy(AsVector3D v) { x += v.x; y += v.y; z += v.z; }
    [As] public void decrementBy(AsVector3D v) { x -= v.x; y -= v.y; z -= v.z; }
    [As] public bool equals(AsVector3D v, bool allFour = false) => x == v.x && y == v.y && z == v.z && (!allFour || w == v.w);
    [As] public bool nearEquals(AsVector3D v, double tolerance, bool allFour = false) =>
        Near(x, v.x, tolerance) && Near(y, v.y, tolerance) && Near(z, v.z, tolerance) && (!allFour || Near(w, v.w, tolerance));
    static bool Near(double a, double b, double tol) => Math.Abs(a - b) < tol;
    /// <summary>Divides by the old length and returns it (no guard for a zero length: the components become NaN).</summary>
    [As] public double normalize() { var l = length; x /= l; y /= l; z /= l; return l; }
    [As] public void negate() { x = -x; y = -y; z = -z; }
    /// <summary>The perspective divide by w.</summary>
    [As] public void project() { x /= w; y /= w; z /= w; }
    [As] public void scaleBy(double s) { x *= s; y *= s; z *= s; }
    [As("toString")] public string ToAsString() => $"(x={Rt.NumberToString(x)}, y={Rt.NumberToString(y)}, z={Rt.NumberToString(z)})";

    [As] public static double angleBetween(AsVector3D a, AsVector3D b)
    {
        double dot = a.x * b.x + a.y * b.y + a.z * b.z;
        double la = Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z), lb = Math.Sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
        return Math.Acos(dot / (la * lb));
    }
    [As] public static double distance(AsVector3D a, AsVector3D b) =>
        Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));

    // The class keeps one shared instance per axis (ClassTraits::Vector3D, sfr_as3_10.cpp:2234).
    static readonly ConditionalWeakTable<Runtime, AsVector3D[]> axes = new();
    static AsVector3D Axis(int i)
    {
        var rt = Runtime.Current;
        var set = axes.GetValue(rt, r => new[] { New(r, 1, 0, 0), New(r, 0, 1, 0), New(r, 0, 0, 1) });
        return set[i];
    }
    [As] public static AsVector3D X_AXIS => Axis(0);
    [As] public static AsVector3D Y_AXIS => Axis(1);
    [As] public static AsVector3D Z_AXIS => Axis(2);
}
