namespace Ms2.Avm2.Flash;

/// <summary>Scaleform's Render::Matrix4x4&lt;double&gt; (render_matrix4x4.h): 16 values row by row, translation in column 3.</summary>
public struct Mat4
{
    public double[] M; // 16, row-major; null reads as the identity
    public static Mat4 Identity() { var m = new double[16]; m[0] = m[5] = m[10] = m[15] = 1; return new Mat4 { M = m }; }
    public static Mat4 Zero() => new() { M = new double[16] };
    public double this[int r, int c] { get => M[r * 4 + c]; set => M[r * 4 + c] = value; }

    /// <summary>m1 * m2 (the portable product the exe uses for Matrix4x4&lt;double&gt;).</summary>
    public static Mat4 Mul(Mat4 m1, Mat4 m2)
    {
        var r = Zero();
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                r[i, j] = m1[i, 0] * m2[0, j] + m1[i, 1] * m2[1, j] + m1[i, 2] * m2[2, j] + m1[i, 3] * m2[3, j];
        return r;
    }
    public Mat4 Clone() => new() { M = (double[])M.Clone() };
    /// <summary>this = this * m (m applied first).</summary>
    public Mat4 Prepend(Mat4 m) => Mul(this, m);
    /// <summary>this = m * this.</summary>
    public Mat4 Append(Mat4 m) => Mul(m, this);

    public static Mat4 Translation(double x, double y, double z) { var r = Identity(); r[0, 3] = x; r[1, 3] = y; r[2, 3] = z; return r; }
    public static Mat4 Scaling(double sx, double sy, double sz) { var r = Identity(); r[0, 0] = sx; r[1, 1] = sy; r[2, 2] = sz; return r; }
    /// <summary>The axis is used as given (not normalised) and the angle goes through float cos/sin, as the SDK does.</summary>
    public static Mat4 Rotation(double angle, (double X, double Y, double Z) axis)
    {
        var r = Identity();
        double c = MathF.Cos((float)angle), s = MathF.Sin((float)angle), ic = 1.0f - c;
        double xx = axis.X * axis.X, yy = axis.Y * axis.Y, zz = axis.Z * axis.Z;
        double xy = axis.X * axis.Y * ic, xz = axis.X * axis.Z * ic, yz = axis.Y * axis.Z * ic;
        double xs = axis.X * s, ys = axis.Y * s, zs = axis.Z * s;
        r[0, 0] = xx * ic + c; r[0, 1] = xy - zs; r[0, 2] = xz + ys;
        r[1, 0] = xy + zs; r[1, 1] = yy * ic + c; r[1, 2] = yz - xs;
        r[2, 0] = xz - ys; r[2, 1] = yz + xs; r[2, 2] = zz * ic + c;
        return r;
    }
    public static Mat4 Rotation(double angle, (double X, double Y, double Z) axis, (double X, double Y, double Z) pivot) =>
        Mul(Translation(-pivot.X, -pivot.Y, -pivot.Z), Mul(Rotation(angle, axis), Translation(pivot.X, pivot.Y, pivot.Z)));

    /// <summary>The affine product (row 3 ignored).</summary>
    public (double X, double Y, double Z) Transform(double x, double y, double z) =>
        (this[0, 0] * x + this[0, 1] * y + this[0, 2] * z + this[0, 3],
         this[1, 0] * x + this[1, 1] * y + this[1, 2] * z + this[1, 3],
         this[2, 0] * x + this[2, 1] * y + this[2, 2] * z + this[2, 3]);

    public Mat4 Transposed() { var r = Zero(); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[j, i] = this[i, j]; return r; }

    static double Minor(Mat4 m, int r0, int r1, int r2, int c0, int c1, int c2) =>
        m[r0, c0] * (m[r1, c1] * m[r2, c2] - m[r2, c1] * m[r1, c2]) -
        m[r0, c1] * (m[r1, c0] * m[r2, c2] - m[r2, c0] * m[r1, c2]) +
        m[r0, c2] * (m[r1, c0] * m[r2, c1] - m[r2, c0] * m[r1, c1]);
    /// <summary>Cofactor expansion along row 0.</summary>
    public double Determinant() =>
        this[0, 0] * Minor(this, 1, 2, 3, 1, 2, 3) - this[0, 1] * Minor(this, 1, 2, 3, 0, 2, 3) +
        this[0, 2] * Minor(this, 1, 2, 3, 0, 1, 3) - this[0, 3] * Minor(this, 1, 2, 3, 0, 1, 2);

    /// <summary>The adjugate inverse of Matrix4x4::GetInverse (0x142602c20); a singular matrix gives the identity with the
    /// translation negated.</summary>
    public Mat4 Inverse()
    {
        double a00 = this[0, 0], a01 = this[0, 1], a02 = this[0, 2], a03 = this[0, 3];
        double a10 = this[1, 0], a11 = this[1, 1], a12 = this[1, 2], a13 = this[1, 3];
        double a20 = this[2, 0], a21 = this[2, 1], a22 = this[2, 2], a23 = this[2, 3];
        double a30 = this[3, 0], a31 = this[3, 1], a32 = this[3, 2], a33 = this[3, 3];
        double s0 = a20 * a31 - a21 * a30, s1 = a20 * a32 - a22 * a30, s2 = a20 * a33 - a23 * a30;
        double s3 = a21 * a32 - a22 * a31, s4 = a21 * a33 - a23 * a31, s5 = a22 * a33 - a23 * a32;
        double c00 = +(s5 * a11 - s4 * a12 + s3 * a13), c10 = -(s5 * a10 - s2 * a12 + s1 * a13);
        double c20 = +(s4 * a10 - s2 * a11 + s0 * a13), c30 = -(s3 * a10 - s1 * a11 + s0 * a12);
        double det = c00 * a00 + c10 * a01 + c20 * a02 + c30 * a03;
        if (det == 0)
        {
            var r = Identity();
            r[0, 3] = -this[0, 3]; r[1, 3] = -this[1, 3]; r[2, 3] = -this[2, 3];
            return r;
        }
        double inv = 1 / det;
        double r00 = c00 * inv, r10 = c10 * inv, r20 = c20 * inv, r30 = c30 * inv;
        double r01 = -(s5 * a01 - s4 * a02 + s3 * a03) * inv, r11 = +(s5 * a00 - s2 * a02 + s1 * a03) * inv;
        double r21 = -(s4 * a00 - s2 * a01 + s0 * a03) * inv, r31 = +(s3 * a00 - s1 * a01 + s0 * a02) * inv;
        s0 = a10 * a31 - a11 * a30; s1 = a10 * a32 - a12 * a30; s2 = a10 * a33 - a13 * a30;
        s3 = a11 * a32 - a12 * a31; s4 = a11 * a33 - a13 * a31; s5 = a12 * a33 - a13 * a32;
        double r02 = +(s5 * a01 - s4 * a02 + s3 * a03) * inv, r12 = -(s5 * a00 - s2 * a02 + s1 * a03) * inv;
        double r22 = +(s4 * a00 - s2 * a01 + s0 * a03) * inv, r32 = -(s3 * a00 - s1 * a01 + s0 * a02) * inv;
        s0 = a21 * a10 - a20 * a11; s1 = a22 * a10 - a20 * a12; s2 = a23 * a10 - a20 * a13;
        s3 = a22 * a11 - a21 * a12; s4 = a23 * a11 - a21 * a13; s5 = a23 * a12 - a22 * a13;
        double r03 = -(s5 * a01 - s4 * a02 + s3 * a03) * inv, r13 = +(s5 * a00 - s2 * a02 + s1 * a03) * inv;
        double r23 = -(s4 * a00 - s2 * a01 + s0 * a03) * inv, r33 = +(s3 * a00 - s1 * a01 + s0 * a02) * inv;
        return new Mat4 { M = new[] { r00, r01, r02, r03, r10, r11, r12, r13, r20, r21, r22, r23, r30, r31, r32, r33 } };
    }
}

/// <summary>flash.geom.Matrix3D after Scaleform's Instances::fl_geom::Matrix3D (sfr_as3_11.cpp). The matrix keeps its
/// translation in twips (the display object's unit): the 16-number constructor and clone take the values as they are,
/// rawData and position read the translation back divided by 20, and rawData's setter multiplies it by 20. Display-object
/// binding (a Matrix3D that writes back to its object) is not modelled: the runtime has no 3D scene.</summary>
[AsClass("flash.geom.Matrix3D")]
public class AsMatrix3D : AsObject
{
    internal Mat4 Mat = Mat4.Identity();
    const double TwipsPerPixel = 20;
    Runtime RT => Class.Runtime;

    // 16 arguments are the elements as they are; one Vector.<Number> is read column by column (missing elements read as
    // 0) and transposed; any other argument count leaves the identity.
    [AsCtor] void Ctor(object[] a)
    {
        if (a.Length == 16)
        {
            for (int i = 0; i < 16; i++) Mat.M[i] = Rt.ToNumber(a[i]);
        }
        else if (a.Length == 1)
        {
            if (a[0] is not AsArray v || v.Class?.TypeArg != RT.NumberClass) return;
            for (int i = 0; i < 16; i++) Mat.M[i] = i < v.Items.Count ? Rt.ToNumber(v.Items[i]) : 0;
            Mat = Mat.Transposed();
        }
    }

    public static AsMatrix3D New(Runtime rt, Mat4 m) { var r = (AsMatrix3D)rt.GetClass("flash.geom.Matrix3D").Construct(); r.Mat = m; return r; }

    static (double, double, double) P(AsVector3D v, double d0, double d1, double d2) => v == null ? (d0, d1, d2) : (v.x, v.y, v.z);

    [As] public void append(AsMatrix3D m) => Mat = Mat.Append(m.Mat);
    [As] public void prepend(AsMatrix3D m) => Mat = Mat.Prepend(m.Mat);
    [As] public void appendRotation(double degrees, AsVector3D axis, AsVector3D pivot = null) =>
        Mat = Mat.Append(Mat4.Rotation(Deg(degrees), P(axis, 0, 0, 0), P(pivot, 0, 0, 0)));
    [As] public void prependRotation(double degrees, AsVector3D axis, AsVector3D pivot = null) =>
        Mat = Mat.Prepend(Mat4.Rotation(Deg(degrees), P(axis, 0, 0, 0), P(pivot, 0, 0, 0)));
    static double Deg(double d) => d * Math.PI / 180.0;
    [As] public void appendScale(double sx, double sy, double sz) => Mat = Mat.Append(Mat4.Scaling(sx, sy, sz));
    [As] public void prependScale(double sx, double sy, double sz) => Mat = Mat.Prepend(Mat4.Scaling(sx, sy, sz));
    [As] public void appendTranslation(double x, double y, double z) => Mat = Mat.Append(Mat4.Translation(x, y, z));
    [As] public void prependTranslation(double x, double y, double z) => Mat = Mat.Prepend(Mat4.Translation(x, y, z));
    [As] public AsMatrix3D clone() => New(RT, Mat.Clone());
    [As] public void identity() => Mat = Mat4.Identity();
    [As] public bool invert() { Mat = Mat.Inverse(); return true; }
    [As] public void transpose() => Mat = Mat.Transposed();
    [As] public double determinant => Mat.Determinant();

    [As] public AsVector3D position
    {
        get => AsVector3D.New(RT, Mat[0, 3] / TwipsPerPixel, Mat[1, 3] / TwipsPerPixel, Mat[2, 3] / TwipsPerPixel, 0);
        set { Mat[0, 3] = value.x * TwipsPerPixel; Mat[1, 3] = value.y * TwipsPerPixel; Mat[2, 3] = value.z * TwipsPerPixel; }
    }

    AsClass NumberVector => (AsClass)RT.ApplyType(RT.VectorClass, new object[] { RT.NumberClass });

    /// <summary>The 16 elements column by column (the transpose of the stored matrix), the translation in pixels.</summary>
    [As] public AsArray rawData
    {
        get
        {
            var m = Mat.Clone();
            m[0, 3] /= TwipsPerPixel; m[1, 3] /= TwipsPerPixel; m[2, 3] /= TwipsPerPixel;
            return new AsArray(RT, m.Transposed().M.Select(d => (object)d), NumberVector);
        }
        set
        {
            if (value == null) throw new NullReferenceException();
            var m = Mat4.Zero();
            for (int i = 0; i < 16; i++) m.M[i] = i < value.Items.Count ? Rt.ToNumber(value.Items[i]) : 0;
            m = m.Transposed();
            m[0, 3] *= TwipsPerPixel; m[1, 3] *= TwipsPerPixel; m[2, 3] *= TwipsPerPixel;
            Mat = m;
        }
    }

    [As] public AsVector3D transformVector(AsVector3D v) { var (x, y, z) = Mat.Transform(v.x, v.y, v.z); return AsVector3D.New(RT, x, y, z); }
    /// <summary>The vector through the matrix without its translation.</summary>
    [As] public AsVector3D deltaTransformVector(AsVector3D v)
    {
        var m = Mat.Clone(); m[0, 3] = m[1, 3] = m[2, 3] = 0;
        var (x, y, z) = m.Transform(v.x, v.y, v.z);
        return AsVector3D.New(RT, x, y, z);
    }
    /// <summary>vout gets mat * each (x, y, z) triple of vin; an element that does not convert reads as 0.</summary>
    [As] public void transformVectors(AsArray vin, AsArray vout)
    {
        for (int i = 0; i < vin.Items.Count; i += 3)
        {
            double Get(int k) => k < vin.Items.Count ? Rt.ToNumber(vin.Items[k]) is var d && !double.IsNaN(d) ? d : 0 : 0;
            var (x, y, z) = Mat.Transform(Get(i), Get(i + 1), Get(i + 2));
            vout.Length = Math.Max(vout.Length, i + 3);
            vout.Items[i] = x; vout.Items[i + 1] = y; vout.Items[i + 2] = z;
        }
    }

    /// <summary>A view matrix at pos looking along pos - at: rows are the right, up and look axes, the translation each
    /// axis's negated dot with pos (the SDK's ViewRH). at defaults to (0, 1, 0), up to (0, 0, 1).</summary>
    [As] public void pointAt(AsVector3D pos, AsVector3D at = null, AsVector3D up = null)
    {
        var (px, py, pz) = (pos.x, pos.y, pos.z);
        var (ax, ay, az) = P(at, 0, 1, 0);
        var (ux, uy, uz) = P(up, 0, 0, 1);
        double zx = px - ax, zy = py - ay, zz = pz - az, zl = Math.Sqrt(zx * zx + zy * zy + zz * zz);
        zx /= zl; zy /= zl; zz /= zl;
        double xx = uy * zz - uz * zy, xy = uz * zx - ux * zz, xz = ux * zy - uy * zx, xl = Math.Sqrt(xx * xx + xy * xy + xz * xz);
        xx /= xl; xy /= xl; xz /= xl;
        double yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
        var m = Mat4.Zero();
        m[0, 0] = xx; m[0, 1] = xy; m[0, 2] = xz; m[0, 3] = -(xx * px + xy * py + xz * pz);
        m[1, 0] = yx; m[1, 1] = yy; m[1, 2] = yz; m[1, 3] = -(yx * px + yy * py + yz * pz);
        m[2, 0] = zx; m[2, 1] = zy; m[2, 2] = zz; m[2, 3] = -(zx * px + zy * py + zz * pz);
        m[3, 3] = 1;
        Mat = m;
    }

    // decompose, interpolate, interpolateTo and recompose are unimplemented in this GFx version: they throw a VerifyError.
    AsThrow NotImplemented(string what) => RT.MakeError("VerifyError", "Error #1001: instance::Matrix3D::" + what + "() is not implemented yet", 1001);
    [As] public object decompose(string orientationStyle = "eulerAngles") => throw NotImplemented("decompose");
    [As] public bool recompose(object components, string orientationStyle = "eulerAngles") => throw NotImplemented("recompose");
    [As] public void interpolateTo(AsMatrix3D toMat, double percent) => throw NotImplemented("interpolateTo");
    [As] public static AsMatrix3D interpolate(AsMatrix3D thisMat, AsMatrix3D toMat, double percent) => throw thisMat.NotImplemented("interpolate");
}
