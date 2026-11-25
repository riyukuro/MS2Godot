using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace Ms2.Formats.World;

/// <summary>
/// The params of the strip effects: swoosh (type 7), ribbon (0xd), lightning (0x10) and beam (0x15). Each reads its own
/// attributes after CEffectParam::vfunc_1's; an absent attribute keeps the constructor's value.
/// </summary>
public sealed class EffectStripParam
{
    /// <summary>alphaMode (CompareNoCase): SWOOSH 0, STANDARD 1, ADD 2; another value keeps 0. Swoosh and ribbon blend 0 as
    /// src alpha / one, 1 as src alpha / inverse src alpha, 2 as one / one (swoosheffectparam.cpp:448-463).</summary>
    public const int AlphaSwoosh = 0, AlphaStandard = 1, AlphaAdd = 2;

    /// <summary>texturePath (swoosh, ribbon), nifPath (swoosh, ribbon), node1 / node2 (swoosh).</summary>
    public string TexturePath = "", NifPath = "", Node1 = "", Node2 = "";
    /// <summary>swoosh length in seconds of trail (0.1); ribbon, lightning and beam width (10).</summary>
    public float Length = 0.1f, Width = 10f;
    /// <summary>quality: curve samples per segment (4); swoosh sampling: trail points per second (15). A swoosh keeps at
    /// most 4 and 60 (FUN_140ad02f0, a min, swoosheffectparam.cpp:216-217).</summary>
    public int Quality = 4, Sampling = 15, AlphaMode;
    /// <summary>lightning texturePathA / B / C (near, mid, far), beam texturePath: XmlTokenList on "," (no empty token).</summary>
    public string[] TexturesA = System.Array.Empty<string>(), TexturesB = System.Array.Empty<string>(), TexturesC = System.Array.Empty<string>();
    /// <summary>lightning spread (1), jointLen (32), sortingMode (OFF 0, INHERIT 1), textureChangeTick (0 ms),
    /// textureSequence (false).</summary>
    public float Spread = 1f, JointLen = 32f;
    public int SortingMode;
    public uint TextureChangeTick;
    public bool TextureSequence;
    /// <summary>beam &lt;beamInfo type&gt;: 0 none, 1 pointCurve (passThroughPoint), 2 directionCurve (directionRate,
    /// lengthRate, 0.5 when absent).</summary>
    public int BeamCurve;
    public Vector3 PassThroughPoint;
    public float DirectionRate, LengthRate;

    /// <summary>The param of a part of kind <paramref name="type"/> (<see cref="EffectSkipTable"/>'s type ids), null for a kind
    /// that is not a strip.</summary>
    public static EffectStripParam Read(int type, XElement e)
    {
        var p = new EffectStripParam();
        switch (type)
        {
            case EffectSkipTable.TypeSwoosh:
                // CSwooshEffectParam::vfunc_1 @0x14181d010 (swoosheffectparam.cpp:207-229)
                p.TexturePath = Str(e, "texturePath", p.TexturePath);
                p.NifPath = Str(e, "nifPath", p.NifPath);
                p.Node1 = Str(e, "node1", p.Node1);
                p.Node2 = Str(e, "node2", p.Node2);
                p.Length = Float(e, "length", p.Length);
                p.Quality = Math.Min(Int(e, "quality", p.Quality), 4);
                p.Sampling = Math.Min(Int(e, "sampling", p.Sampling), 0x3c);
                p.AlphaMode = Alpha(e, p.AlphaMode);
                return p;
            case EffectSkipTable.TypeRibbon:
                // CRibbonEffectParam::vfunc_1 @0x141701410 (ribboneffectparam.cpp:125-142)
                p.TexturePath = Str(e, "texturePath", p.TexturePath);
                p.NifPath = Str(e, "nifPath", p.NifPath);
                p.Width = Float(e, "width", p.Width);
                p.Quality = Int(e, "quality", p.Quality);
                p.AlphaMode = Alpha(e, p.AlphaMode);
                return p;
            case EffectSkipTable.TypeLightning:
                // LightningEffectParam::vfunc_1 @0x14144b880 (lightningeffectparam.cpp:116-150)
                p.TexturesA = Tokens(e, "texturePathA");
                p.TexturesB = Tokens(e, "texturePathB");
                p.TexturesC = Tokens(e, "texturePathC");
                p.Spread = Float(e, "spread", p.Spread);
                p.JointLen = Float(e, "jointLen", p.JointLen);
                p.Width = Float(e, "width", p.Width);
                p.TextureChangeTick = (uint)Int(e, "textureChangeTick", 0);
                p.TextureSequence = EffectTable.B((string)e.Attribute("textureSequence"), false);
                p.AlphaMode = Alpha(e, p.AlphaMode);
                string sort = (string)e.Attribute("sortingMode");
                if (string.Equals(sort, "INHERIT", StringComparison.OrdinalIgnoreCase)) p.SortingMode = 1;
                else if (string.Equals(sort, "OFF", StringComparison.OrdinalIgnoreCase)) p.SortingMode = 0;
                return p;
            case EffectSkipTable.TypeBeam:
                // CBeamEffectParam::vfunc_1 @0x140a0c3a0 (beameffectparam.cpp:88-129): nothing past the texture list when
                // it is empty
                p.TexturesA = Tokens(e, "texturePath");
                if (p.TexturesA.Length == 0) return p;
                p.Width = Float(e, "width", p.Width);
                p.Quality = Int(e, "quality", p.Quality);
                p.AlphaMode = Alpha(e, p.AlphaMode);
                if (e.Element("beamInfo") is not { } bi) return p;
                // ParseCurveType @0x140a0c890 (CompareNoCase)
                string curve = (string)bi.Attribute("type");
                p.BeamCurve = string.Equals(curve, "pointCurve", StringComparison.OrdinalIgnoreCase) ? 1
                    : string.Equals(curve, "directionCurve", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                // BeamPointCurveInfo::vfunc_1 @0x140a0c790: XmlQuery(Point3) @0x140874760 (tinyxmlhelper.cpp:482-494), XmlSplit
                // on "," with atof per token; anything but three values leaves the point
                if (p.BeamCurve == 1 && (string)bi.Attribute("passThroughPoint") is { } pt
                    && pt.Split(',', StringSplitOptions.RemoveEmptyEntries) is { Length: 3 } v)
                    p.PassThroughPoint = new Vector3(F(v[0].Trim()), F(v[1].Trim()), F(v[2].Trim()));
                if (p.BeamCurve == 2)   // BeamDirectionCurveInfo::vfunc_1 @0x140a0c800
                {
                    p.DirectionRate = Float(bi, "directionRate", 0.5f);
                    p.LengthRate = Float(bi, "lengthRate", 0.5f);
                }
                return p;
        }
        return null;
    }

    /// <summary>The most curve samples a swoosh makes here: the strip's vertex count is a 16-bit count (UpdateTrail).</summary>
    public const int MaxCurveSamples = ushort.MaxValue - 1;

    /// <summary>
    /// The trail a swoosh makes at the effect step <paramref name="effectStep"/> (CEffectService m_unk_188, the graphic
    /// option's effectStep, 1..3), as CSwooshEffect::vfunc_3 @0x14181d760 (swoosheffectparam.cpp:290-326) sizes it:
    /// <list type="bullet">
    /// <item>with the service's trail mode on (m_unk_128, 1 from its constructor) and an owner of kind 3 or 5 (m_unk_18,
    /// the target's m_nKind), the step drops by 3 at 50 fps or less, 2 at 55 or less, 1 at 60 or less;</item>
    /// <item>step 3 takes half the sampling, step 2 a quarter, each at least 5; a lower step makes no swoosh (false);</item>
    /// <item>segments = max(1, (int)(length * sampling)), curve samples = segments * quality, a point every 1/sampling s.</item>
    /// </list>
    /// </summary>
    public bool SwooshShape(int effectStep, bool trailMode, int ownerKind, float fps, out int segments, out int curveSamples, out float interval)
    {
        int level = effectStep;
        if (trailMode && (ownerKind == 3 || ownerKind == 5))
        {
            int penalty = fps <= 50f ? 3 : fps <= 55f ? 2 : fps <= 60f ? 1 : 0;
            level = Math.Max(0, level - penalty);
        }
        uint sampling = unchecked((uint)Sampling);
        segments = curveSamples = 0; interval = 0;
        if (level == 3) sampling = Math.Max(sampling >> 1, 5u);
        else if (level == 2) sampling = Math.Max(sampling >> 2, 5u);
        else return false;
        // a quality below 1, a negative or huge length make no strip here: the client takes them as unsigned counts
        // (m_nCurveSamples and the reserves) and would ask for billions of samples; the data has none
        float span = Length * sampling;
        if (Quality < 1 || !(span >= 0f) || span * Quality > MaxCurveSamples) return false;
        segments = (int)Math.Max(1u, unchecked((uint)(long)span));
        curveSamples = segments * Quality;
        interval = 1f / sampling;
        return true;
    }

    /// <summary>The Effect archive entry of a texture name: FUN_140cc2ba0 (unit_140cc20a0, "%s%s/%s") puts it under the
    /// resource folder's "Effect/Textures"; the file system takes repeated slashes as one.</summary>
    public static string TextureEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : "textures/" + string.Join('/', parts).ToLowerInvariant();
    }

    static string Str(XElement e, string name, string def) => (string)e.Attribute(name) ?? def;
    static float Float(XElement e, string name, float def) =>
        e.Attribute(name) is { } a && float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;
    /// <summary>XmlGetAttr(int): QueryIntAttribute's sscanf "%d", so "4.0" reads 4; no digits keep the default.</summary>
    static int Int(XElement e, string name, int def)
    {
        string s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return def;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        int digits = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        return i > digits && int.TryParse(s.AsSpan(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    }
    static int Alpha(XElement e, int def)
    {
        string s = (string)e.Attribute("alphaMode");
        if (string.IsNullOrEmpty(s)) return def;
        if (string.Equals(s, "SWOOSH", StringComparison.OrdinalIgnoreCase)) return AlphaSwoosh;
        if (string.Equals(s, "STANDARD", StringComparison.OrdinalIgnoreCase)) return AlphaStandard;
        if (string.Equals(s, "ADD", StringComparison.OrdinalIgnoreCase)) return AlphaAdd;
        return def;
    }
    /// <summary>XmlTokenList of strings (xmlattr.h 0x1401788f0, Unk_SplitA): CString::Tokenize on ",", no empty token, no trim.</summary>
    static string[] Tokens(XElement e, string name)
    {
        string s = (string)e.Attribute(name);
        return string.IsNullOrEmpty(s) ? System.Array.Empty<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>
/// An afterImage part (type 0x16, CAfterImageEffectParam::vfunc_1 @0x140987520, afterimageeffectparam.cpp:41-67): an absent
/// value takes the default given there, so the constructor's 250 ms interval is never kept.
/// </summary>
public sealed class EffectAfterImageParam
{
    public int ImageSize = 0x100;
    public bool LightenMode = true;
    /// <summary>color "r, g, b, a" (ParseColorA @0x140db93e0, "%f, %f, %f, %f"): (0,0,0,0) when it does not read four,
    /// white when absent.</summary>
    public Vector4 Color = Vector4.One;
    /// <summary>timeLife and timeInterval in seconds in the file (1, 0.2), kept in ms; moveInterval (100).</summary>
    public uint TimeLifeMs = 1000, TimeIntervalMs = 200;
    public float MoveInterval = 100f;

    public static EffectAfterImageParam Read(XElement e)
    {
        var p = new EffectAfterImageParam();
        if ((string)e.Attribute("imageSize") is { Length: > 0 } size && int.TryParse(size.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) p.ImageSize = n;
        p.LightenMode = EffectTable.B((string)e.Attribute("lightenMode"), true);
        if ((string)e.Attribute("color") is { } c)
        {
            var v = c.Split(',', StringSplitOptions.TrimEntries);
            p.Color = v.Length == 4 && v.All(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                ? new Vector4(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture),
                    float.Parse(v[2], CultureInfo.InvariantCulture), float.Parse(v[3], CultureInfo.InvariantCulture))
                : Vector4.Zero;
        }
        p.TimeLifeMs = (uint)(Sec(e, "timeLife", 1f) * 1000f);
        p.TimeIntervalMs = (uint)(Sec(e, "timeInterval", 0.2f) * 1000f);
        p.MoveInterval = Sec(e, "moveInterval", 100f);
        return p;
    }

    static float Sec(XElement e, string name, float def) =>
        e.Attribute(name) is { } a && float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
}

/// <summary>
/// The strip CSwooshEffect::UpdateTrail @0x14181f1d0 (swoosheffectparam.cpp:529-628) keeps between two trail nodes: every
/// interval it takes both nodes' world points; with 4 or more it lays Catmull-Rom curves through them (FUN_141820e00,
/// D3DXVec3CatmullRom) at quality steps per segment plus the newest point, and drops the oldest point once
/// segments + 3 are kept. Vertex k of row A (node 1) has uv (k / (n - 1), 0), of row B (node 2) uv (k / (n - 1), 1).
/// The buffers are made once; a sample allocates nothing.
/// </summary>
public sealed class SwooshTrail
{
    readonly Vector3[] pointsA, pointsB;
    int points;
    readonly int segments, curveSamples;
    readonly float interval;
    double lastSample;
    /// <summary>The curve rows, <see cref="Count"/> long: row A (node 1) and row B (node 2).</summary>
    public readonly Vector3[] CurveA, CurveB;
    /// <summary>Vertices per row of the last strip; 0 before four points are kept.</summary>
    public int Count { get; private set; }
    public int Points => points;

    public SwooshTrail(int segments, int curveSamples, float interval)
    {
        this.segments = segments; this.curveSamples = curveSamples; this.interval = interval;
        pointsA = new Vector3[segments + 3];
        pointsB = new Vector3[segments + 3];
        // (points - 3) * steps + 1 samples at most, points <= segments + 3
        int steps = (int)((float)curveSamples / segments);
        CurveA = new Vector3[segments * steps + 1];
        CurveB = new Vector3[segments * steps + 1];
    }

    /// <summary>One update at game time <paramref name="now"/> (s) with the nodes' world points: true when the strip was
    /// laid again.</summary>
    /// <summary>Whether an update at <paramref name="now"/> samples (UpdateTrail's interval test, :554-556): read the nodes only
    /// then.</summary>
    public bool Due(double now) => now - lastSample >= interval;

    public bool Sample(double now, Vector3 a, Vector3 b)
    {
        double elapsed = now - lastSample;
        if (elapsed < interval) return false;
        lastSample += elapsed;
        pointsA[points] = a; pointsB[points] = b; points++;
        if (points < 4) return false;
        float perSegment = (float)(uint)curveSamples / (uint)segments;
        uint steps = (uint)(long)perSegment;
        int n = 0;
        for (int i = 0; i < points - 3; i++)
            for (uint j = 0; j < steps; j++)
            {
                float t = j / perSegment;
                CurveA[n] = CatmullRom(pointsA[i], pointsA[i + 1], pointsA[i + 2], pointsA[i + 3], t);
                CurveB[n] = CatmullRom(pointsB[i], pointsB[i + 1], pointsB[i + 2], pointsB[i + 3], t);
                n++;
            }
        CurveA[n] = pointsA[points - 1];
        CurveB[n] = pointsB[points - 1];
        Count = (ushort)(n + 1);
        if (points >= segments + 3)
        {
            System.Array.Copy(pointsA, 1, pointsA, 0, points - 1);
            System.Array.Copy(pointsB, 1, pointsB, 0, points - 1);
            points--;
        }
        return true;
    }

    /// <summary>D3DXVec3CatmullRom: the curve through v1 (t 0) and v2 (t 1).</summary>
    public static Vector3 CatmullRom(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * v1 + (v2 - v0) * t + (2f * v0 - 5f * v1 + 4f * v2 - v3) * t2 + (3f * v1 - v0 - 3f * v2 + v3) * t3);
    }
}
