using Ms2.Formats.Nif;

namespace Ms2.Formats.Model;

/// <summary>One node's animated channels. A null channel means the sequence leaves that part of the pose alone.</summary>
public sealed class KfChannel
{
    public Func<double, Vec3> T;
    public Func<double, Quat> R;
    public Func<double, double> S;
}

/// <summary>A KF sequence (NiSequenceData) with evaluators for keyed and B-spline transform channels.</summary>
public sealed class KfAnimation
{
    public string Name;
    public double Duration;
    public Dictionary<string, KfChannel> Channels = new();
    public List<(double time, string text)> TextKeys = new();

    const long Invalid = 0xFFFF;
    const double FltMax = 3.0e38;

    sealed class Key
    {
        public double Time; public double[] V, Fwd, Bwd;
    }

    static double[] Lerp(List<Key> keys, double t)
    {
        if (t <= keys[0].Time) return keys[0].V;
        if (t >= keys[^1].Time) return keys[^1].V;
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            var a = keys[i]; var b = keys[i + 1];
            if (a.Time <= t && t <= b.Time)
            {
                double span = b.Time - a.Time;
                double u = (t - a.Time) / (span == 0 ? 1.0 : span);
                var o = new double[a.V.Length];
                if (a.Fwd != null)
                {
                    // quadratic keys: a span uses key0's Backward and key1's Forward (stream order is in, out)
                    double h00 = 2 * u * u * u - 3 * u * u + 1, h10 = u * u * u - 2 * u * u + u;
                    double h01 = -2 * u * u * u + 3 * u * u, h11 = u * u * u - u * u;
                    for (int k = 0; k < o.Length; k++) o[k] = h00 * a.V[k] + h10 * a.Bwd[k] + h01 * b.V[k] + h11 * b.Fwd[k];
                }
                else
                    for (int k = 0; k < o.Length; k++) o[k] = a.V[k] * (1 - u) + b.V[k] * u;
                return o;
            }
        }
        return keys[^1].V;
    }

    static Quat SlerpKeys(List<(double t, Quat q)> keys, double t)
    {
        if (t <= keys[0].t) return keys[0].q;
        if (t >= keys[^1].t) return keys[^1].q;
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            var a = keys[i]; var b = keys[i + 1];
            if (a.t <= t && t <= b.t)
            {
                double span = b.t - a.t;
                return Quat.Slerp(a.q, b.q, (t - a.t) / (span == 0 ? 1.0 : span));
            }
        }
        return keys[^1].q;
    }

    static double[] V3(NifStruct v) => new double[] { v.Float("x"), v.Float("y"), v.Float("z") };
    static double[] Scalar(object o) => new double[] { o is float f ? f : Convert.ToDouble(o) };

    /// <summary>Keys of a KeyGroup (float or vector), or null when it has none.</summary>
    static List<Key> KeyGroup(NifStruct g, Func<object, double[]> conv)
    {
        if (g == null || g.IntOr("Num Keys", 0) == 0) return null;
        long it = g.Int("Interpolation");
        if (it != 1 && it != 2 && it != 5) throw new NotSupportedException("key interpolation " + it);
        var keys = new List<Key>();
        foreach (var k in g.Structs("Keys"))
        {
            var key = new Key { Time = k.Float("Time"), V = conv(k["Value"]) };
            if (it == 2) { key.Fwd = conv(k["Forward"]); key.Bwd = conv(k["Backward"]); }
            keys.Add(key);
        }
        return keys;
    }

    static double[] VecConv(object o) => V3((NifStruct)o);

    /// <summary>Open uniform cubic B-spline with nq control points (rows of cps, dim wide), t in [0,1].</summary>
    static double[] BSpline(double[] cps, int nq, double t01, int dim)
    {
        const int deg = 3;
        int qmd = nq - deg;
        t01 = Math.Clamp(t01, 0.0, 1.0);
        double T; int imin;
        if (t01 >= 1.0) { T = qmd; imin = nq - 1 - deg; }
        else { T = qmd * t01; imin = (int)T; }
        int imax = imin + deg;
        var knot = new List<double>();
        for (int i1 = imax + 1 - deg; i1 < imax + 1 + deg; i1++)
            knot.Add(i1 <= deg ? 0.0 : (i1 >= nq ? qmd : i1 - deg));
        var val = new double[deg + 1];
        val[deg] = 1.0;
        for (int row = deg - 1; row >= 0; row--)
        {
            int k0 = deg, k1 = row;
            double inv = 1.0 / (knot[k0] - knot[k1]);
            double c1 = (knot[k0] - T) * inv;
            val[row] = c1 * val[row + 1];
            for (int col = row + 1; col < deg; col++)
            {
                double c0 = (T - knot[k1]) * inv;
                val[col] *= c0;
                k0++; k1++;
                inv = 1.0 / (knot[k0] - knot[k1]);
                c1 = (knot[k0] - T) * inv;
                val[col] += c1 * val[col + 1];
            }
            double c0b = (T - knot[k1]) * inv;
            val[deg] *= c0b;
        }
        var o = new double[dim];
        for (int k = 0; k <= deg; k++)
            for (int d = 0; d < dim; d++) o[d] += val[k] * cps[(imin + k) * dim + d];
        return o;
    }

    static Quat QuatOf(NifStruct q) => new(q.Float("w"), q.Float("x"), q.Float("y"), q.Float("z"));

    /// <summary>A keyed transform channel (NiTransformData keys, or the constant pose value when a channel has no keys).</summary>
    static void KeyedChannel(List<NifStruct> B, NifStruct v, long di, KfChannel ch, bool tOn, bool rOn, bool sOn)
    {
        var data = di >= 0 ? B[(int)di] : null;
        List<Key> keysT = null, keysS = null;
        List<(double, Quat)> keysR = null;
        List<Key>[] euler = null;
        if (data != null)
        {
            if (data.IntOr("Num Rotation Keys", 0) != 0)
            {
                long rt = data.Int("Rotation Type");
                if (rt == 4)
                    euler = data.Structs("XYZ Rotations").Select(g => KeyGroup(g, Scalar)).ToArray();
                else
                {
                    if (rt != 1 && rt != 5) throw new NotSupportedException("rotation key type " + rt);
                    keysR = data.Structs("Quaternion Keys").Select(k => ((double)k.Float("Time"), QuatOf(k.Struct("Value")))).ToList();
                }
            }
            keysT = KeyGroup(data.TryGetValue("Translations", out var tg) ? tg as NifStruct : null, VecConv);
            keysS = KeyGroup(data.TryGetValue("Scales", out var sg) ? sg as NifStruct : null, Scalar);
        }
        if (tOn)
        {
            if (keysT != null) ch.T = t => { var a = Lerp(keysT, t); return new Vec3(a[0], a[1], a[2]); };
            else
            {
                var c = V3(v.Struct("Translation"));
                if (Math.Abs(c[0]) < FltMax) { var cv = new Vec3(c[0], c[1], c[2]); ch.T = _ => cv; }
            }
        }
        if (rOn)
        {
            if (euler != null)
                ch.R = t =>
                {
                    double Ang(int i) => euler[i] != null ? Lerp(euler[i], t)[0] : 0.0;
                    // Gamebryo XYZ Euler keys: q = qZ * qY * qX
                    return Quat.FromAxis(2, Ang(2)) * Quat.FromAxis(1, Ang(1)) * Quat.FromAxis(0, Ang(0));
                };
            else if (keysR != null) ch.R = t => SlerpKeys(keysR, t);
            else
            {
                var r = v.Struct("Rotation");
                double w = r.Float("w");
                if (w < FltMax && w > -FltMax) { var q = QuatOf(r); ch.R = _ => q; }
            }
        }
        if (sOn)
        {
            if (keysS != null) ch.S = t => Lerp(keysS, t)[0];
            else
            {
                double s = v.Float("Scale");
                if (Math.Abs(s) < FltMax) ch.S = _ => s;
            }
        }
    }

    public static KfAnimation Load(NifFile kf)
    {
        var B = kf.Blocks;
        var seq = B.First(b => b.Type == "NiSequenceData");
        var anim = new KfAnimation { Name = kf.StrField(seq, "Name"), Duration = seq.Float("Duration") };
        foreach (var ei in seq.Ints("Evaluators"))
        {
            if (ei < 0) continue;
            var e = B[(int)ei];
            var node = kf.StrField(e, "Node Name");
            var ct = e.Ints("Channel Types");
            var ch = new KfChannel();
            if (e.Type == "NiTransformEvaluator")
            {
                KeyedChannel(B, e.Struct("Value"), e.Int("Data"), ch, ct[0] != 0, ct[1] != 0, ct[2] != 0);
            }
            else if (e.Type is "NiBSplineCompTransformEvaluator" or "NiBSplineTransformEvaluator")
            {
                bool comp = e.Type == "NiBSplineCompTransformEvaluator";
                long di = e.Int("Data"), bi = e.Int("Basis Data");
                var data = di >= 0 ? B[(int)di] : null;
                int nq = bi >= 0 ? (int)B[(int)bi].Int("Num Control Points") : 0;
                double t0 = e.Float("Start Time"), t1 = e.Float("End Time");
                double span = t1 - t0 == 0 ? 1.0 : t1 - t0;
                var x = e.Struct("Transform");

                Func<double, double[]> Mk(long handle, int dim, string offKey, string halfKey)
                {
                    var cps = new double[nq * dim];
                    if (comp)
                    {
                        var raw = data.Ints("Compact Control Points");
                        double off = e.Float(offKey), half = e.Float(halfKey);
                        for (int i = 0; i < cps.Length; i++) cps[i] = off + half * raw[handle + i] / 32767.0;
                    }
                    else
                    {
                        var raw = data.Floats("Float Control Points");
                        for (int i = 0; i < cps.Length; i++) cps[i] = raw[handle + i];
                    }
                    return t => BSpline(cps, nq, (t - t0) / span, dim);
                }

                if (ct[0] != 0)
                {
                    long h = e.Int("Translation Handle");
                    if (h != Invalid) { var f = Mk(h, 3, "Translation Offset", "Translation Half Range"); ch.T = t => { var a = f(t); return new Vec3(a[0], a[1], a[2]); }; }
                    else
                    {
                        var c = V3(x.Struct("Translation"));
                        if (Math.Abs(c[0]) < FltMax) { var cv = new Vec3(c[0], c[1], c[2]); ch.T = _ => cv; }
                    }
                }
                if (ct[1] != 0)
                {
                    long h = e.Int("Rotation Handle");
                    if (h != Invalid) { var f = Mk(h, 4, "Rotation Offset", "Rotation Half Range"); ch.R = t => { var a = f(t); return new Quat(a[0], a[1], a[2], a[3]).Normalized(); }; }
                    else
                    {
                        var r = x.Struct("Rotation");
                        if (Math.Abs(r.Float("w")) < FltMax) { var q = QuatOf(r); ch.R = _ => q; }
                    }
                }
                if (ct[2] != 0)
                {
                    long h = e.Int("Scale Handle");
                    if (h != Invalid) { var f = Mk(h, 1, "Scale Offset", "Scale Half Range"); ch.S = t => f(t)[0]; }
                    else
                    {
                        double s = x.Float("Scale");
                        if (Math.Abs(s) < FltMax) ch.S = _ => s;
                    }
                }
            }
            else continue;
            if (ch.T != null || ch.R != null || ch.S != null) anim.Channels[node] = ch;
        }
        long tki = seq.Int("Text Keys");
        if (tki >= 0)
            foreach (var k in B[(int)tki].Structs("Text Keys"))
                anim.TextKeys.Add((k.Float("Time"), kf.StrField(k, "Value")));
        return anim;
    }

    /// <summary>Cycle type of the controllers an animation came from (NiTimeController flags bits 1-2).</summary>
    public int Cycle;          // 0 loop, 1 reverse (back and forth), 2 clamp

    /// <summary>Block names of transform controller targets that are not NiNodes (rigid meshes).</summary>
    public HashSet<string> MeshTargets = new(StringComparer.Ordinal);

    /// <summary>
    /// The transform controllers stored in a model NIF (NiTransformController on its nodes, as on flags, wheels and
    /// effect meshes) as one sequence: the node name -> channel, timed from the earliest controller start to the latest
    /// stop. Returns null when the NIF has no keyed transform controller. Frequency and phase are applied per channel.
    /// </summary>
    public static KfAnimation FromControllers(NifFile nif)
    {
        var B = nif.Blocks;
        var ctrls = new List<(NifStruct c, string target, bool mesh)>();
        foreach (var c in B)
        {
            if (c.Type != "NiTransformController" || !c.Has("Target")) continue;
            long ti = c.Int("Target"), ii = c.IntOr("Interpolator", -1);
            if (ti < 0 || ii < 0 || B[(int)ii].Type != "NiTransformInterpolator") continue;
            if (B[(int)ii].IntOr("Data", -1) < 0) continue;                // a constant pose, nothing moves
            var name = nif.Name((int)ti);
            if (string.IsNullOrEmpty(name)) continue;
            ctrls.Add((c, name, B[(int)ti].Type != "NiNode"));
        }
        if (ctrls.Count == 0) return null;
        double start = ctrls.Min(x => (double)x.c.Float("Start Time")), stop = ctrls.Max(x => (double)x.c.Float("Stop Time"));
        var anim = new KfAnimation { Name = "controllers", Duration = Math.Max(stop - start, 1.0 / 30) };
        anim.Cycle = (int)((ctrls[0].c.IntOr("Flags", 8) >> 1) & 3);
        foreach (var (c, target, mesh) in ctrls)
        {
            var ip = B[(int)c.Int("Interpolator")];
            var ch = new KfChannel();
            KeyedChannel(B, ip.Struct("Transform"), ip.Int("Data"), ch, true, true, true);
            double freq = c.Float("Frequency"), phase = c.Float("Phase");
            if (freq <= 0) freq = 1;
            // the key time for sequence time u (from the earliest start): (u + start) * frequency + phase
            if (freq != 1 || phase != 0 || start != 0)
            {
                var (t0, r0, s0) = (ch.T, ch.R, ch.S);
                double Map(double u) => (u + start) * freq + phase;
                if (t0 != null) ch.T = u => t0(Map(u));
                if (r0 != null) ch.R = u => r0(Map(u));
                if (s0 != null) ch.S = u => s0(Map(u));
            }
            if (ch.T == null && ch.R == null && ch.S == null) continue;
            anim.Channels[target] = ch;
            if (mesh) anim.MeshTargets.Add(target);
        }
        return anim.Channels.Count == 0 ? null : anim;
    }
}
