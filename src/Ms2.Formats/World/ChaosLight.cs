using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Formats.World;

/// <summary>The six values a chaos light layer hands the chaos material (ChaosLightEffectLayer::FUN_141046410,
/// chaoslighttransitiondata.cpp:277-288): the extra data ChaosFresnelBoost, ChaosFresnelExponent, ChaosDimmer and
/// ChaosLightColor.</summary>
public struct ChaosLightValues : IEquatable<ChaosLightValues>
{
    public float Boost, Exponent, Dimmer, R, G, B;

    public bool Equals(ChaosLightValues o) =>
        Boost == o.Boost && Exponent == o.Exponent && Dimmer == o.Dimmer && R == o.R && G == o.G && B == o.B;
    public override bool Equals(object o) => o is ChaosLightValues v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Boost, Exponent, Dimmer, R, G, B);
}

/// <summary>One &lt;transition&gt; of a chaoslight part (ChaosLightTransitionData, TransitionData; ReadTransitionData
/// @0x14185f0b0, transitioneffectparam.cpp:213-226).</summary>
public sealed class ChaosLightKey
{
    /// <summary>type: FastInEaseOut 1, EaseInFastOut 2, EaseInEaseOut 3 (CompareNoCase), else 0 (linear).</summary>
    public int Interpolation;
    /// <summary>duration (0 when absent); the start is the sum of the earlier keys' durations from the part's start delay.</summary>
    public float Duration, Start;
    public ChaosLightValues Values;
}

/// <summary>
/// A chaoslight part (type 0xe, ChaosLightEffectParam::vfunc_1 @0x141045a60, chaoslighttransitiondata.cpp:114-170): a fresnel
/// rim light put on the owner's character meshes. The defaults are the ChaosLightTransitionData constructor's
/// (chaoslighttransitiondata.cpp:108-110).
/// </summary>
public sealed class EffectChaosLightParam
{
    public string Node = "";
    public ChaosLightValues Values = new() { Boost = 26.09f, Exponent = 5.36f, Dimmer = 0f, R = 0.95f, G = 0.57f, B = 0.22f };
    public bool Blink;
    public int BlinkBoostStart, Layer;
    /// <summary>The &lt;transition&gt; keys. The param's own values are not a key: they only seed the first transition.</summary>
    public ChaosLightKey[] Keys = Array.Empty<ChaosLightKey>();
    /// <summary>m_nLoopIndex: 0 when the part's loop is "true" (CompareNoCase, not the bool parse) and it has a transition,
    /// else -1.</summary>
    public int LoopIndex = -1;

    public static EffectChaosLightParam Read(XElement e, float startDelay)
    {
        var p = new EffectChaosLightParam();
        p.Node = (string)e.Attribute("node") ?? "";
        ref var v = ref p.Values;
        v.Boost = F(e, "fresnelBoost", v.Boost);
        v.Exponent = F(e, "fresnelExponent", v.Exponent);
        v.Dimmer = F(e, "dimmer", v.Dimmer);
        p.Blink = EffectTable.B((string)e.Attribute("blink"), false);
        p.BlinkBoostStart = I(e, "blinkBoostStart", 0);
        p.Layer = I(e, "layer", 0);
        Rgb((string)e.Attribute("rgb"), ref v);
        bool loop = string.Equals((string)e.Attribute("loop") ?? "", "true", StringComparison.OrdinalIgnoreCase);
        var keys = new List<ChaosLightKey>();
        var prev = p.Values;
        float time = startDelay;
        foreach (var k in e.Elements("transition"))
        {
            var key = new ChaosLightKey();
            string type = (string)k.Attribute("type") ?? "";
            key.Interpolation = type.Equals("FastInEaseOut", StringComparison.OrdinalIgnoreCase) ? 1
                : type.Equals("EaseInFastOut", StringComparison.OrdinalIgnoreCase) ? 2
                : type.Equals("EaseInEaseOut", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
            key.Duration = F(k, "duration", 0f);
            key.Start = time;
            time += key.Duration;
            if (loop && p.LoopIndex == -1) p.LoopIndex = keys.Count;
            var kv = prev;
            kv.Boost = F(k, "fresnelBoost", kv.Boost);
            kv.Exponent = F(k, "fresnelExponent", kv.Exponent);
            kv.Dimmer = F(k, "dimmer", kv.Dimmer);
            Rgb((string)k.Attribute("rgb"), ref kv);
            key.Values = kv;
            keys.Add(key);
            prev = kv;
        }
        p.Keys = keys.ToArray();
        return p;
    }

    /// <summary>ParseVector3 @0x141b6ed40 (util.cpp:475-483): "r,g,b" split on ',' up to three, atof each.
    /// STILL-OPEN: fewer than three parts leave the client's local vector unset; kept as the previous colour here.</summary>
    static void Rgb(string s, ref ChaosLightValues v)
    {
        if (s == null) return;
        var parts = s.Split(',');
        if (parts.Length < 3) return;
        v.R = Atof(parts[0]); v.G = Atof(parts[1]); v.B = Atof(parts[2]);
    }

    /// <summary>atof: the longest float prefix after white space, 0 when none.</summary>
    internal static float Atof(string s)
    {
        var span = s.AsSpan().TrimStart();
        for (int n = span.Length; n > 0; n--)
            if (float.TryParse(span.Slice(0, n), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
        return 0f;
    }

    /// <summary>XmlGetAttr(float) = QueryFloatAttribute: the default when absent or not a number.</summary>
    static float F(XElement e, string name, float def) =>
        float.TryParse((string)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

    /// <summary>XmlGetAttr(int) = QueryIntAttribute (sscanf "%d").</summary>
    static int I(XElement e, string name, int def)
    {
        string s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return def;
        var span = s.AsSpan().TrimStart();
        int end = span.Length > 0 && (span[0] == '+' || span[0] == '-') ? 1 : 0, digits = 0;
        while (end < span.Length && span[end] >= '0' && span[end] <= '9') { end++; digits++; }
        return digits > 0 && int.TryParse(span.Slice(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    }
}

/// <summary>
/// One chaoslight part's clock, without the engine: ChaosLightEffect (uichaoslightdialog.cpp:349-513) on TransitionEffect
/// (transitioneffectparam.cpp:100-199) and CEffect::vfunc_9 (effectservice.cpp:405-418). The caller puts the layers on at
/// <see cref="Started"/> with <see cref="Layer"/>, and sets <see cref="Layer"/> on them after each <see cref="Tick"/>.
/// No allocation after <see cref="Begin"/>.
/// </summary>
public sealed class ChaosLightRun
{
    public EffectChaosLightParam Param;
    /// <summary>m_dStartTime: the request's time plus the start delay; the keys' times count from here too, so the delay
    /// counts twice for them, as in the client.</summary>
    public double StartTime;
    public float Duration;
    public bool Loop;
    /// <summary>m_b2e: the layers are on.</summary>
    public bool Started;
    /// <summary>m_param: the values the keys last set (the param's own values before).</summary>
    public ChaosLightValues Current;
    /// <summary>The values the layers hold: the last push (Current when the layers were put on).</summary>
    public ChaosLightValues Layer;
    double startTime2, lastTick, phase;
    bool blinkOn;

    /// <summary>ChaosLightEffect::vfunc_3 @0x141041760: true when the layers go on at once (a start delay of 0).</summary>
    public bool Begin(EffectChaosLightParam param, float startDelay, float duration, bool loop, double now)
    {
        Param = param;
        StartTime = now + startDelay;
        Duration = duration;
        Loop = loop;
        Current = param.Values;
        Layer = Current;
        Started = false;
        startTime2 = 0; lastTick = 0; phase = 0; blinkOn = false;
        if (startDelay == 0f) { Start(now); return true; }
        return false;
    }

    /// <summary>vfunc_4 @0x1410419e0: the layers go on with the current values; the blink clock starts.</summary>
    void Start(double now)
    {
        Started = true;
        Layer = Current;
        startTime2 = now;
    }

    /// <summary>
    /// ChaosLightEffect::vfunc_9 @0x141041e50: the blink when set, then the keys. Returns false when the part is over (the
    /// caller takes the layers off, vfunc_7). <paramref name="startedNow"/> is true on the frame the layers go on.
    /// </summary>
    public bool Tick(double now, out bool startedNow)
    {
        startedNow = false;
        if (Param.Blink) UpdateBlink(now);
        // CEffect::vfunc_9
        if (!Started && StartTime <= now) { Start(now); startedNow = true; }
        if (now > StartTime + Duration && !Loop) return false;
        if (KeyAt(now, out int index, out float t))
        {
            var keys = Param.Keys;
            if (index >= keys.Length) index = keys.Length - 1;
            var from = keys[index];
            var to = index != keys.Length - 1 ? keys[index + 1] : Param.LoopIndex != -1 ? keys[Param.LoopIndex] : keys[index];
            t = Ease(from.Interpolation, t);
            ref readonly var a = ref from.Values;
            ref readonly var b = ref to.Values;
            Current.Boost = a.Boost + t * (b.Boost - a.Boost);
            Current.Exponent = a.Exponent + t * (b.Exponent - a.Exponent);
            Current.Dimmer = a.Dimmer + t * (b.Dimmer - a.Dimmer);
            Current.R = a.R + t * (b.R - a.R);
            Current.G = a.G + t * (b.G - a.G);
            Current.B = a.B + t * (b.B - a.B);
            if (Started) Layer = Current;
        }
        return true;
    }

    /// <summary>UpdateBlink @0x1410420d0: the colour goes on and off, the phase speeding up once past blinkBoostStart
    /// seconds from the start. The last tick starts at 0, so the first one turns it on.</summary>
    void UpdateBlink(double now)
    {
        float delta = (float)(now - lastTick);
        lastTick = now;
        int boostStart = Param.BlinkBoostStart;
        if (boostStart > 0 && boostStart < now - startTime2)
        {
            float boost = (float)((now - startTime2) - (float)boostStart) + 3f;
            phase += delta * boost;
        }
        else phase += delta * 3f;
        if (phase > 1.0) { blinkOn = !blinkOn; phase = 0; }
        if (!Started) return;
        Layer = Current;
        if (!blinkOn) Layer.R = Layer.G = Layer.B = 0f;
    }

    /// <summary>FUN_14185f6f0 @0x14185f6f0: the key and its t at <paramref name="now"/>; false with no keys.</summary>
    bool KeyAt(double now, out int index, out float t)
    {
        index = 0; t = 0f;
        var keys = Param.Keys;
        int count = keys.Length;
        if (count == 0) return false;
        float elapsed = (float)(now - StartTime);
        int loopIndex = Param.LoopIndex;
        int i;
        float end = 0f, loopStart = 0f;
        for (i = 0; i < count; i++)
        {
            float start = keys[i].Start, duration = keys[i].Duration;
            if (i == loopIndex) loopStart = start;
            end = start + duration;
            if (end > elapsed) { t = NearlyZero(duration) ? 1f : (elapsed - start) / duration; break; }
        }
        if (i == count && loopIndex != -1)
        {
            float loopLength = end - loopStart;
            if (loopLength <= 0f) { index = count; t = 1f; return true; }
            elapsed -= loopStart;
            if (!NearlyZero(loopLength))
                while (elapsed > loopLength) elapsed -= loopLength;
            // The wrapped time counts from the loop key's start while the keys' times do not, as in the client.
            for (i = loopIndex; i < count; i++)
            {
                float start = keys[i].Start, duration = keys[i].Duration;
                end = start + duration;
                if (end > elapsed) { t = NearlyZero(duration) ? 1f : (elapsed - start) / duration; break; }
            }
        }
        index = i;
        return true;
    }

    static bool NearlyZero(float f) => MathF.Abs(f) <= 1e-4f;

    /// <summary>The interpolations (transitioneffectparam.cpp:20-50): 0 linear, 1 Unk_EaseOut::Evaluate (fishingmanager.cpp:312),
    /// 2 Unk_C2Ease::FUN_1410af700 (cubecraft_2.cpp:1650), 3 the sine; each clamps t to 0..1 first.</summary>
    public static float Ease(int kind, float t)
    {
        switch (kind)
        {
            case 0: return t < 0f ? 0f : t > 1f ? 1f : t;
            case 1: return t < 0f ? 0f : t > 1f ? 1f : MathF.Sqrt(1f - (t - 1f) * (t - 1f));
            case 2: return !(0f <= t) ? 0f : t > 1f ? 1f : 1f - MathF.Sqrt(1f - t * t);
            case 3: return t < 0f ? 0f : t > 1f ? 1f : MathF.Sin((t - 0.5f) * 3.1415927f) * 0.5f + 0.5f;
        }
        return t;
    }
}
