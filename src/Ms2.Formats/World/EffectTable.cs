using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The owner an effect is made for, as CEffectParam::vfunc_7 reads it from the effect target (NoticeMessage): the object
/// kind (Unk_EffectTarget m_nKind; 1 = the own PC), GetOwnerGender (0 male, 1 female; -1 when not known here),
/// GetOwnerJobCode (0 when not known) and IsMyPC of the target.
/// </summary>
public readonly record struct EffectOwner(int Kind, int Gender, int JobCode, bool TargetIsMyPc)
{
    /// <summary>The own PC's object kind (WorldService::GetObjectKind).</summary>
    public const int KindMyPc = 1;
    /// <summary>An owner of <paramref name="kind"/> whose gender and job code are not known.</summary>
    public static EffectOwner OfKind(int kind, bool targetIsMyPc = false) => new(kind, -1, 0, targetIsMyPc);
}

/// <summary>One effect entry of an effect xml (Xml pack effect/...): a nif model, or another kind (sound, particle file...) that is only recorded.</summary>
public sealed class EffectPart
{
    public string Type;                  // nif, sound, ...
    public string FilePath;              // for nif: path in Resource/Model/Effect
    public string SoundGroup, SoundEvent;   // for soundEvent: FEV group path and event name
    /// <summary>soundMusic effect type 3 attributes (CSoundMusicEffectParam::vfunc_1 @0x14178fdb0, soundmusiceffectparam.cpp:69-84).</summary>
    public int MusicCueId = -1, MusicParamId = -1, MusicPriority = 0x80;
    public float MusicParamValue, MusicVolume = -1f;
    public string MusicReverb = "";
    public float StartDelay, Duration;
    public float[] Translate = new float[3], Rotation = new float[3];
    public float Scale = 1f;
    /// <summary>loop: the effect stays past its duration (CEffectParam::vfunc_1, CEffect::vfunc_9).</summary>
    public bool Loop;
    /// <summary>CEffectParam::vfunc_1's owner filters (@0x1411a0290, effectservice.cpp:649-662): gender (2 = any), jobCode
    /// (0 = any), onlyCasterMyPC, onlyTargetMyPC. <see cref="SkippedFor"/> applies them.</summary>
    public int Gender = 2, JobCode;
    public bool OnlyCasterMyPC, OnlyTargetMyPC;
    /// <summary>offSkip (CEffectParam::vfunc_1, effectservice.cpp:649-662, m_bOffSkip +0x31): CreateEffect @0x14119eae0
    /// (effectservice.cpp:318-345), CNifEffect::vfunc_3 (nifeffectparam.cpp:763) and EffectSkipMgr::ShouldSkipByView make no
    /// view test for the part.</summary>
    public bool OffSkip;
    /// <summary>soundEvent forceHearDistance (CSoundEventEffectParam::vfunc_1, soundeventmgr.cpp:297): when not 0, the view
    /// distance CreateEffect tests a monster's or a boss's sound against (effectservice.cpp:301-306), in place of the
    /// effectOption sound row's.</summary>
    public float ForceHearDistance;
    /// <summary>soundEvent doNotStop (CSoundEventEffectParam::vfunc_1, soundeventmgr.cpp:298): the sound plays on for 10 s
    /// after its effect is stopped (CSoundEventEffect::vfunc_5, :612-626).</summary>
    public bool DoNotStop;
    /// <summary>soundEvent cooltimeID (CSoundEventEffectParam::vfunc_1, soundeventmgr.cpp:299): the sound is made only when
    /// <see cref="CooltimeTable.Use"/> lets it (FUN_1417760d0, :368-375); 0 has no cooldown.</summary>
    public int CooltimeId;
    /// <summary>soundEvent isSkipMusicConcert (CSoundEventEffectParam::vfunc_1, soundeventmgr.cpp:266-299): CreateEffect makes
    /// no such part in the music concert (CSoundEventEffectParam::vfunc_7, :360-363; <see cref="IsMusicConcert"/>).</summary>
    public bool SkipMusicConcert;
    /// <summary>CFieldData::FUN_1403756f0 (fielddata.cpp:849-852), which WorldService::FUN_141bcb150 asks of the current
    /// field: the concert is map 2000064 (0x1e84c0) with feature 0x60 (MusicConcert) on.</summary>
    public static bool IsMusicConcert(int fieldId, bool musicConcertFeature) => fieldId == 0x1e84c0 && musicConcertFeature;
    /// <summary>soundEvent byJob (CSoundEventEffectParam::vfunc_1 @0x141775af0, soundeventmgr.cpp:283-307): the event and start
    /// delay per owner job code, from jobEvent="job:event;..." (FUN_141776330) and byJobStartDelay="job:delay;..."
    /// (FUN_1417764c0). Null when byJob is off.</summary>
    public Dictionary<int, string> JobEvents;
    public Dictionary<int, float> JobStartDelays;

    /// <summary>
    /// CEffectParam::vfunc_7 @0x14119e940 (effectservice.cpp:245-259): true when the part is not made for this owner: a gender
    /// other than 2 that is not the owner's, a job code that is not the owner's, onlyCasterMyPC when the caster kind is not
    /// 1 (the own PC; CSoundEventEffect::vfunc_3 makes the same test, soundeventmgr.cpp:476), onlyTargetMyPC when the target is
    /// not the own PC. The first test (m_bIgnoreOptimize and NoticeMessage::CanShow, EffectSkipMgr) is not made here.
    /// An owner whose gender or job code is not known (<see cref="EffectOwner.Gender"/> -1, JobCode 0) is not tested on it.
    /// </summary>
    public bool SkippedFor(in EffectOwner owner)
    {
        if (Gender != 2 && owner.Gender >= 0 && Gender != owner.Gender) return true;
        if (JobCode != 0 && owner.JobCode != 0 && JobCode != owner.JobCode) return true;
        if (OnlyCasterMyPC && owner.Kind != EffectOwner.KindMyPc) return true;
        if (OnlyTargetMyPC && !owner.TargetIsMyPc) return true;
        return false;
    }

    /// <summary>
    /// CSoundEventEffectParam::FUN_141776120 @0x141776120 (soundeventmgr.cpp:378-391): the event a soundEvent part plays for an
    /// owner of job code <paramref name="ownerJobCode"/>: the plain event when the character's disableJobSound option is set
    /// (IsJobSoundDisabled), byJob is off or the job code is 0; else the job's entry of jobEvent, empty when the job has none
    /// (CSoundEventEffect::vfunc_3 then plays nothing, :480-494).
    /// </summary>
    public string SoundEventFor(int ownerJobCode, bool jobSoundDisabled)
    {
        if (jobSoundDisabled || JobEvents == null || ownerJobCode == 0) return SoundEvent;
        return JobEvents.TryGetValue(ownerJobCode, out var ev) ? ev : "";
    }

    /// <summary>CSoundEventEffectParam::FUN_1417762a0 @0x1417762a0 (soundeventmgr.cpp:393-405): the start delay for an owner's
    /// job code: the job's byJobStartDelay entry when byJob is on and the job has one, else startDelay.</summary>
    public float SoundStartDelayFor(int ownerJobCode) =>
        JobEvents != null && ownerJobCode != 0 && JobStartDelays != null && JobStartDelays.TryGetValue(ownerJobCode, out float d) ? d : StartDelay;
    /// <summary>
    /// attachInfo, as CNifEffectParam::vfunc_1 reads it (nifeffectparam.cpp:388-401): attachType (text, e.g. "Mesh"),
    /// attachToNode="true" (attach type 1), applyNodeTransform (false when absent) and parentNode.
    /// </summary>
    public string AttachType, ParentNode;
    public bool AttachToNode, ApplyNodeTransform;
    /// <summary>
    /// m_nAttachType: ParseAttachType of attachType (nifeffectparam.cpp:275: Node 1, Mesh 2, WeaponL 3, WeaponR 4, else 0,
    /// CompareNoCase), set to 1 by attachToNode="true" (:398). CNifEffect::vfunc_3 switches on it (:717): 0 puts the part
    /// under the world effect root (AttachToOwnerNode), the others under a node of the actor.
    /// </summary>
    public int AttachKind => AttachToNode ? 1 : AttachTypeOf(AttachType);
    /// <summary>&lt;applyNodeTransform translate rotation scale&gt; (true when absent, nifeffectparam.cpp:366, :408-414):
    /// m_bitsApply, the parts of the parent node's world transform a type 0 part takes at its start (:1058-1072).</summary>
    public bool ApplyTranslate = true, ApplyRotation = true, ApplyScale = true;

    /// <summary>ParseAttachType (nifeffectparam.cpp:275).</summary>
    public static int AttachTypeOf(string s) =>
        string.Equals(s, "Node", StringComparison.OrdinalIgnoreCase) ? 1
        : string.Equals(s, "Mesh", StringComparison.OrdinalIgnoreCase) ? 2
        : string.Equals(s, "WeaponL", StringComparison.OrdinalIgnoreCase) ? 3
        : string.Equals(s, "WeaponR", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
    /// <summary>update translate / rotation / scale: the parts of the parent's world the effect follows (true when absent).</summary>
    /// <summary>The light a type="light" part makes (null for other parts).</summary>
    public EffectLightParam Light;
    public bool UpdateTranslate = true, UpdateRotation = true, UpdateScale = true;
    /// <summary>The type attribute's kind, CEffectParam::m_nType (ParseEffectType @0x1411a0f80, effectdatamanager.cpp; 0 for an
    /// unknown or absent type): EffectSkipMgr's row for the part.</summary>
    public int TypeId;
    /// <summary>A vibrate part's freq, amp, damping (the shake's min scale, 1 when absent) and dampingTime (from the start, 0
    /// when absent; CVibrateEffectParam::vfunc_1 @0x141b72690, vibrateeffectparam.cpp:66-74). See <see cref="CameraVibrate"/>.</summary>
    public float VibrateFrequency, VibrateAmplitude, VibrateDamping = 1f, VibrateDampingTime;
    /// <summary>A direct child of the xml's root, one of the effect data's params (effectservice.cpp:614-617). The children of a
    /// random part (randomeffectparam.cpp:88) are listed too, with this false.</summary>
    public bool TopLevel;
    /// <summary>A random part's child: the index in the effect's part list of its random part (the &lt;effect&gt; it sits in,
    /// CRandomEffectParam::vfunc_1, randomeffectparam.cpp:88-93); -1 for any other part.</summary>
    public int RandomParent = -1;
    /// <summary>A random part's direct &lt;effect&gt; children, as indices in the effect's part list, in the file's order
    /// (FirstChild / NextSibling "effect", randomeffectparam.cpp:88); null for any other part.</summary>
    public int[] RandomChildren;
    /// <summary>A random part's prob: XmlSplit of the text on ", " (CString::Tokenize, no empty token), each token through
    /// atoi (placeholder/xmlattr.h, XmlConvert) as m_arrWeights (randomeffectparam.cpp:86-87). Child i (of the children
    /// made) takes weight i, 0 past the list's end (:162-165). Empty for no prob.</summary>
    public uint[] RandomWeights;
    /// <summary>A nif part's viewDistance (CNifEffectParam::vfunc_1, nifeffectparam.cpp:376, m_fViewDistance +0xac): its own
    /// view distance, -1 (absent) for the table's.</summary>
    public float ViewDistance = -1;
    /// <summary>A swoosh, ribbon, lightning or beam part's own values (<see cref="EffectStripParam.Read"/>); null for other parts.</summary>
    public EffectStripParam Strip;
    /// <summary>An afterImage part's own values; null for other parts.</summary>
    public EffectAfterImageParam AfterImage;
    /// <summary>A chaoslight part's values and keys (<see cref="EffectChaosLightParam"/>); null for other parts.</summary>
    public EffectChaosLightParam ChaosLight;
    /// <summary>A status part's category (statuseffectparam.cpp:101-105); null for other parts.</summary>
    public string StatusCategory;
    /// <summary>type="weather" fallType: the sky object WeatherEffect::vfunc_4 hands CEffectService::SetSkyObject
    /// (0 clear, 1 snow, 2 heavy snow, 3 rain, 4 heavy rain, 5 sandstorm, 6 cherry blossom, 7 leaf fall).</summary>
    public int FallType;
}

/// <summary>
/// The values of a type="light" effect part (CLightEffectParam::vfunc_1, lighteffectparam.cpp:192-250): the colours are 0..255
/// in the file (divided by 255; white when fewer than 3 components), dimmer 1, range 1000 and attenuation 1 / 0 / 0 when absent.
/// </summary>
public sealed class EffectLightParam
{
    public float[] Diffuse = { 1, 1, 1 }, Specular = { 1, 1, 1 };
    public float Dimmer = 1, Range = 1000, Constant = 1, Linear = 0, Quadratic = 0;
}

/// <summary>Effect definitions on demand from the Xml pack.</summary>
public sealed class EffectTable
{
    readonly PackSet packs;
    readonly Dictionary<string, List<EffectPart>> cache = new(StringComparer.OrdinalIgnoreCase);
    public EffectTable(PackSet packs) { this.packs = packs; }

    public List<EffectPart> Get(string xmlPath)
    {
        if (string.IsNullOrEmpty(xmlPath)) return null;
        if (cache.TryGetValue(xmlPath, out var l)) return l;
        string entry = "effect/" + xmlPath.Replace('\\', '/').ToLowerInvariant();
        List<EffectPart> parts = null;
        if (packs.Exists("Xml", entry))
        {
            try
            {
                parts = Parse(PackXml.Parse(packs.Read("Xml", entry)));
            }
            catch { parts = null; }
        }
        return cache[xmlPath] = parts;
    }

    /// <summary>The parts of one effect xml: every &lt;effect&gt; in document order, a random part's children linked to it.</summary>
    internal static List<EffectPart> Parse(XDocument x)
    {
        var parts = new List<EffectPart>();
        var index = new Dictionary<XElement, int>();
        foreach (var e in x.Descendants("effect"))
        {
            var p = new EffectPart { Type = (string)e.Attribute("type"), FilePath = (string)e.Attribute("filePath"), SoundGroup = (string)e.Attribute("group"), SoundEvent = (string)e.Attribute("event"), StartDelay = F((string)e.Attribute("startDelay")), Duration = F((string)e.Attribute("duration")) };
            if (string.Equals(p.Type, "soundMusic", StringComparison.OrdinalIgnoreCase))
            {
                p.MusicCueId = I((string)e.Attribute("cueID"), -1);
                p.MusicParamId = I((string)e.Attribute("paramID"), -1);
                p.MusicParamValue = F((string)e.Attribute("paramValue"), 0f);
                p.MusicPriority = I((string)e.Attribute("priority"), 0x80);
                p.MusicVolume = F((string)e.Attribute("volume"), -1f);
                p.MusicReverb = (string)e.Attribute("reverb") ?? "";
            }
            var t = e.Element("transform");
            if (t != null) { p.Translate = V((string)t.Attribute("translate")); p.Rotation = V((string)t.Attribute("rotation")); p.Scale = F((string)t.Attribute("scale"), 1f); }
            p.Loop = B((string)e.Attribute("loop"), false);
            p.Gender = I((string)e.Attribute("gender"), 2);
            p.JobCode = I((string)e.Attribute("jobCode"), 0);
            p.OnlyCasterMyPC = B((string)e.Attribute("onlyCasterMyPC"), false);
            p.OnlyTargetMyPC = B((string)e.Attribute("onlyTargetMyPC"), false);
            p.OffSkip = B((string)e.Attribute("offSkip"), false);
            if (string.Equals(p.Type, "soundEvent", StringComparison.Ordinal)) p.ForceHearDistance = F((string)e.Attribute("forceHearDistance"), 0f);
            if (string.Equals(p.Type, "soundEvent", StringComparison.Ordinal)) p.DoNotStop = B((string)e.Attribute("doNotStop"), false);
            if (string.Equals(p.Type, "soundEvent", StringComparison.Ordinal)) p.CooltimeId = I((string)e.Attribute("cooltimeID"), 0);
            if (string.Equals(p.Type, "soundEvent", StringComparison.Ordinal)) p.SkipMusicConcert = B((string)e.Attribute("isSkipMusicConcert"), false);
            if (string.Equals(p.Type, "soundEvent", StringComparison.Ordinal) && B((string)e.Attribute("byJob"), false))
            {
                p.JobEvents = new Dictionary<int, string>();
                p.JobStartDelays = new Dictionary<int, float>();
                foreach (var (job, ev) in JobPairs((string)e.Attribute("jobEvent"))) p.JobEvents[I(job, 0)] = ev;
                foreach (var (job, d) in JobPairs((string)e.Attribute("byJobStartDelay"))) p.JobStartDelays[I(job, 0)] = F(d);
            }
            p.FallType = (int)F((string)e.Attribute("fallType"));
            p.TypeId = EffectSkipTable.ParseEffectType(p.Type);
            p.TopLevel = e.Parent == x.Root;
            if (p.TypeId == EffectSkipTable.TypeRandom) p.RandomWeights = Weights((string)e.Attribute("prob"));
            if (p.TypeId == EffectSkipTable.RowNif) p.ViewDistance = F((string)e.Attribute("viewDistance"), -1f);
            if (p.TypeId is EffectSkipTable.TypeSwoosh or EffectSkipTable.TypeRibbon or EffectSkipTable.TypeLightning or EffectSkipTable.TypeBeam)
                p.Strip = EffectStripParam.Read(p.TypeId, e);
            else if (p.TypeId == EffectSkipTable.TypeAfterImage) p.AfterImage = EffectAfterImageParam.Read(e);
            else if (p.TypeId == EffectSkipTable.TypeChaosLight) p.ChaosLight = EffectChaosLightParam.Read(e, p.StartDelay);
            else if (p.TypeId == EffectSkipTable.TypeVibrate)
            {
                p.VibrateFrequency = F((string)e.Attribute("freq"));
                p.VibrateAmplitude = F((string)e.Attribute("amp"));
                p.VibrateDamping = F((string)e.Attribute("damping"), 1f);
                p.VibrateDampingTime = F((string)e.Attribute("dampingTime"));
            }
            else if (p.TypeId == EffectSkipTable.TypeStatus) p.StatusCategory = (string)e.Attribute("category") ?? "";   // CStatusEffectParam::vfunc_1 @0x1418010a0
            if (e.Element("attachInfo") is { } a)
            {
                p.AttachType = (string)a.Attribute("attachType");
                p.ParentNode = (string)a.Attribute("parentNode");
                p.AttachToNode = B((string)a.Attribute("attachToNode"), false);
                p.ApplyNodeTransform = B((string)a.Attribute("applyNodeTransform"), false);
            }
            if (e.Element("applyNodeTransform") is { } an)
            {
                p.ApplyTranslate = B((string)an.Attribute("translate"), true);
                p.ApplyRotation = B((string)an.Attribute("rotation"), true);
                p.ApplyScale = B((string)an.Attribute("scale"), true);
            }
            if (e.Element("update") is { } u)
            {
                p.UpdateTranslate = B((string)u.Attribute("translate"), true);
                p.UpdateRotation = B((string)u.Attribute("rotation"), true);
                p.UpdateScale = B((string)u.Attribute("scale"), true);
            }
            if (p.Type == "light")
            {
                var lp = new EffectLightParam();
                if (e.Element("light") is { } li)
                {
                    lp.Diffuse = Color((string)li.Attribute("diffuse"));
                    lp.Specular = Color((string)li.Attribute("specular"));
                    lp.Dimmer = F((string)li.Attribute("dimmer"), 1f);
                    lp.Range = F((string)li.Attribute("range"), 1000f);
                }
                if (e.Element("attenuation") is { } at)
                {
                    lp.Constant = F((string)at.Attribute("constant"), 1f);
                    lp.Linear = F((string)at.Attribute("linear"), 0f);
                    lp.Quadratic = F((string)at.Attribute("quadratic"), 0f);
                }
                p.Light = lp;
            }
            if (e.Parent != null && index.TryGetValue(e.Parent, out int up) && parts[up].RandomWeights != null) p.RandomParent = up;
            index[e] = parts.Count;
            parts.Add(p);
        }
        for (int i = 0; i < parts.Count; i++)
            if (parts[i].RandomWeights != null)
            {
                int n = 0;
                foreach (var c in parts) if (c.RandomParent == i) n++;
                var kids = new int[n];
                n = 0;
                for (int j = 0; j < parts.Count; j++) if (parts[j].RandomParent == i) kids[n++] = j;
                parts[i].RandomChildren = kids;
            }
        return parts;
    }

    /// <summary>XmlGetAttr(bool) = TiXmlElement::QueryBoolAttribute (tinyxmlhelper.cpp:211-214, tinyxml.cpp): a value that
    /// starts with "true", "yes" or "1" in any case is true, one that starts with "false", "no" or "0" is false, and an empty
    /// or other value keeps the default.</summary>
    internal static bool B(string s, bool def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        if (s.StartsWith("true", StringComparison.OrdinalIgnoreCase) || s.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || s[0] == '1') return true;
        if (s.StartsWith("false", StringComparison.OrdinalIgnoreCase) || s.StartsWith("no", StringComparison.OrdinalIgnoreCase) || s[0] == '0') return false;
        return def;
    }
    /// <summary>XmlSplit (placeholder/xmlattr.h) of a list on ", " with atoi per token: CRandomEffectParam's prob.</summary>
    internal static uint[] Weights(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<uint>();
        var tokens = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var w = new uint[tokens.Length];
        for (int i = 0; i < tokens.Length; i++) w[i] = unchecked((uint)Atoi(tokens[i]));
        return w;
    }

    /// <summary>The CRT atoi: leading white space, a sign, then digits up to the first other character; 0 for none.</summary>
    internal static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) v = Math.Min(v * 10 + (s[i] - '0'), (long)int.MaxValue + 1);
        if (neg) v = -v;
        return (int)Math.Clamp(v, int.MinValue, int.MaxValue);
    }

    static float F(string s, float def = 0) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    /// <summary>"r, g, b" in 0..255 as 0..1; white when absent or short.</summary>
    static float[] Color(string s)
    {
        var p = s?.Split(',', StringSplitOptions.TrimEntries);
        if (p == null || p.Length < 3) return new[] { 1f, 1f, 1f };
        return new[] { F(p[0]) / 255f, F(p[1]) / 255f, F(p[2]) / 255f };
    }

    /// <summary>TinyXML QueryIntAttribute (sscanf "%d") semantics: accepts leading int prefix such as "1.0" -> 1.</summary>
    static int I(string s, int def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        var span = s.AsSpan().TrimStart();
        if (span.Length == 0) return def;
        int end = (span[0] == '+' || span[0] == '-') ? 1 : 0;
        int digits = 0;
        while (end < span.Length && span[end] >= '0' && span[end] <= '9') { end++; digits++; }
        if (digits == 0) return def;
        return int.TryParse(span.Slice(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
    }
    /// <summary>The "job:value;job:value" lists of FUN_141776330 / FUN_1417764c0 (soundeventmgr.cpp:408-442): split on ';', then
    /// on ':'; an item that does not split into exactly two is skipped.</summary>
    static IEnumerable<(string Job, string Value)> JobPairs(string list)
    {
        if (string.IsNullOrEmpty(list)) yield break;
        foreach (var item in list.Split(';'))
        {
            var pair = item.Split(':');
            if (pair.Length == 2) yield return (pair[0], pair[1]);
        }
    }

    static float[] V(string s)
    {
        var r = new float[3];
        if (s == null) return r;
        var p = s.Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < 3 && i < p.Length; i++) r[i] = F(p[i]);
        return r;
    }
}
