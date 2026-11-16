namespace Ms2.Formats.Model;

/// <summary>Small double-precision 3-vector.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
}

/// <summary>Quaternion (W, X, Y, Z), Gamebryo order.</summary>
public readonly record struct Quat(double W, double X, double Y, double Z)
{
    public static Quat Identity => new(1, 0, 0, 0);
    public double Dot(Quat o) => W * o.W + X * o.X + Y * o.Y + Z * o.Z;
    public Quat Normalized()
    {
        double n = Math.Sqrt(Dot(this));
        return n == 0 ? Identity : new(W / n, X / n, Y / n, Z / n);
    }
    public static Quat operator -(Quat q) => new(-q.W, -q.X, -q.Y, -q.Z);
    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public static Quat FromAxis(int axis, double a)
    {
        double c = Math.Cos(a / 2), s = Math.Sin(a / 2);
        return axis switch { 0 => new(c, s, 0, 0), 1 => new(c, 0, s, 0), _ => new(c, 0, 0, s) };
    }

    /// <summary>3x3 rotation, row-major, column-vector convention.</summary>
    public double[] ToMatrix()
    {
        var q = Normalized();
        double w = q.W, x = q.X, y = q.Y, z = q.Z;
        return new[]
        {
            1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
            2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
            2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y),
        };
    }

    public static Quat FromMatrix(double[] m)
    {
        double t = m[0] + m[4] + m[8];
        Quat q;
        if (t > 0)
        {
            double s = Math.Sqrt(t + 1.0) * 2;
            q = new(0.25 * s, (m[7] - m[5]) / s, (m[2] - m[6]) / s, (m[3] - m[1]) / s);
        }
        else if (m[0] > m[4] && m[0] > m[8])
        {
            double s = Math.Sqrt(1.0 + m[0] - m[4] - m[8]) * 2;
            q = new((m[7] - m[5]) / s, 0.25 * s, (m[1] + m[3]) / s, (m[2] + m[6]) / s);
        }
        else if (m[4] > m[8])
        {
            double s = Math.Sqrt(1.0 + m[4] - m[0] - m[8]) * 2;
            q = new((m[2] - m[6]) / s, (m[1] + m[3]) / s, 0.25 * s, (m[5] + m[7]) / s);
        }
        else
        {
            double s = Math.Sqrt(1.0 + m[8] - m[0] - m[4]) * 2;
            q = new((m[3] - m[1]) / s, (m[2] + m[6]) / s, (m[5] + m[7]) / s, 0.25 * s);
        }
        return q.Normalized();
    }

    public static Quat Slerp(Quat q0, Quat q1, double u)
    {
        if (q0.Dot(q1) < 0) q1 = -q1;
        double d = Math.Min(1.0, q0.Dot(q1));
        double th = Math.Acos(d);
        Quat q;
        if (th < 1e-6)
            q = new(q0.W * (1 - u) + q1.W * u, q0.X * (1 - u) + q1.X * u, q0.Y * (1 - u) + q1.Y * u, q0.Z * (1 - u) + q1.Z * u);
        else
        {
            double a = Math.Sin((1 - u) * th) / Math.Sin(th), b = Math.Sin(u * th) / Math.Sin(th);
            q = new(q0.W * a + q1.W * b, q0.X * a + q1.X * b, q0.Y * a + q1.Y * b, q0.Z * a + q1.Z * b);
        }
        return q.Normalized();
    }
}

/// <summary>Rigid transform with uniform scale: p_parent = R * (S * p) + T (Gamebryo NiTransform).</summary>
public readonly record struct Xform(Vec3 T, double[] R, double S)
{
    public static Xform Identity => new(new Vec3(0, 0, 0), new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 1);

    public Vec3 Apply(Vec3 p)
    {
        var r = R;
        return new Vec3(
            S * (r[0] * p.X + r[1] * p.Y + r[2] * p.Z) + T.X,
            S * (r[3] * p.X + r[4] * p.Y + r[5] * p.Z) + T.Y,
            S * (r[6] * p.X + r[7] * p.Y + r[8] * p.Z) + T.Z);
    }

    static double[] Mul(double[] a, double[] b)
    {
        var o = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                o[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return o;
    }

    /// <summary>this * child (apply child first).</summary>
    public Xform Compose(Xform c) => new(Apply(c.T), Mul(R, c.R), S * c.S);

    public Xform Inverse()
    {
        var rt = new[] { R[0], R[3], R[6], R[1], R[4], R[7], R[2], R[5], R[8] };
        double si = 1 / S;
        var inv = new Xform(new Vec3(0, 0, 0), rt, si);
        var t = inv.Apply(T);
        return new Xform(new Vec3(-t.X, -t.Y, -t.Z), rt, si);
    }

    public Quat Rotation => Quat.FromMatrix(R);
}
