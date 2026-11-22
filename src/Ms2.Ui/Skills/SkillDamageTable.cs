using System.Globalization;
using System.Xml;

namespace Ms2.Ui.Skills;

/// <summary>
/// The &lt;damage&gt; of a skill attack (SkillDamage::Load @0x1407e01c0, commonfiles/skillattackdata.cpp:251, called from
/// SkillAttackData::Load :574 as m_damage): the damage and push attributes. A missing attribute keeps the ctor's value
/// (:175: count 1, pushprob 1, hitSpeedRate 1, damagedVibrateRatio 1, the rest 0); an attack without &lt;damage&gt; keeps
/// them all. Also the attack element's own hitImmuneBreak, attackMethodType and grabTargetType (SkillAttackData::Load
/// :582-595, 0 from the ctor :498-503), which the hit reaction reads with the push. STILL-OPEN, not read yet:
/// IgnoreOptimalHideDamageHitEffect / RemainEffect, isConstDamageValue, constDamageByTargetSP and damageEventDisable.
/// </summary>
public sealed class SkillAttackDamage
{
    public static readonly SkillAttackDamage None = new();

    public int Count { get; internal set; } = 1;
    public int Hand { get; internal set; }
    public float Rate { get; internal set; }
    /// <summary>"value" (m_nValue, read as __int64).</summary>
    public long Value { get; internal set; }
    public float HitSpeedRate { get; internal set; } = 1f;
    public float HitPauseTime { get; internal set; }
    public float DamagedVibrateRatio { get; internal set; } = 1f;
    public string EffectHit { get; internal set; } = "";
    public string EffectRemain { get; internal set; } = "";
    public float DamageByTargetMaxHP { get; internal set; }
    public long ConstDamageByTargetHP { get; internal set; }
    /// <summary>"push" (SkillPushType, XmlValue: an enum read as a number).</summary>
    public int Push { get; internal set; }
    public float PushDistance { get; internal set; }
    public float PushDuration { get; internal set; }
    public float PushProb { get; internal set; } = 1f;
    /// <summary>"pushEaseType", 0 outside 0..2 (:274).</summary>
    public int PushEaseType { get; internal set; }
    /// <summary>"pushApplyField" (SkillPushApplyField): 1 no push on the ground states, 2 none off them (GetPush :393).</summary>
    public int PushApplyField { get; internal set; }
    public float PushUpDistance { get; internal set; }
    public int PushDown { get; internal set; }
    public int PushFall { get; internal set; }
    public int PushPriority { get; internal set; }
    public int PushPriorityHitImmune { get; internal set; }
    public string PushAnimation { get; internal set; } = "";
    /// <summary>"stateHitIgnore" / "stateHitOnly" (SkillStateHit enums).</summary>
    public int StateHitIgnore { get; internal set; }
    public int StateHitOnly { get; internal set; }
    public int SuperArmorBreak { get; internal set; }
    /// <summary>"attackMaterial" (m_nAttackMaterial :285): the hit sound's material (CDamageRecord::GetAttackMaterial,
    /// skillrecord.cpp:580).</summary>
    public int AttackMaterial { get; internal set; }
    /// <summary>"invokeEffectDisable" (:289): PC::vfunc_33 (pc.cpp:885) skips the buffs' hit invoke effects.</summary>
    public int InvokeEffectDisable { get; internal set; }
    /// <summary>The attack's "hitImmuneBreak" (m_nHitImmuneBreak :583, CDamageRecord::vfunc_1).</summary>
    public int HitImmuneBreak { get; internal set; }
    /// <summary>The attack's "attackMethodType" (SkillAttackMethodType :582; FUN_14175acc0 tests 1).</summary>
    public int AttackMethodType { get; internal set; }
    /// <summary>The attack's "grabTargetType" (SkillGrabTargetType :595).</summary>
    public int GrabTargetType { get; internal set; }

    /// <summary>
    /// SkillDamage::GetPush @0x1407e0f90 (skillattackdata.cpp:393): no push on a ground field (map type 9 or 3,
    /// CFieldData::FUN_1403744a0 / FUN_140374410) when pushApplyField is 1, none off one when it is 2, else "push".
    /// </summary>
    public int GetPush(bool ground)
    {
        if (ground && PushApplyField == 1) return 0;
        if (!ground && PushApplyField == 2) return 0;
        return Push;
    }

    /// <summary>HasDamage @0x1407e0f40 (:384): value, constDamageByTargetHP, rate or damageByTargetMaxHP above 0.</summary>
    public bool HasDamage => Value > 0 || ConstDamageByTargetHP > 0 || Rate > 0f || DamageByTargetMaxHP > 0f;

    /// <summary>The attack at the reader (an &lt;attack&gt; element), read to its end: its own hitImmuneBreak,
    /// attackMethodType and grabTargetType and its first &lt;damage&gt; child's attributes (the first XmlGetChild, as
    /// attack.Element("damage")); <see cref="None"/> when it has none of them.</summary>
    internal static SkillAttackDamage Read(XmlReader r, AttributeValues values, Shared shared)
    {
        int methodType = 0, hitImmuneBreak = 0, grabType = 0;
        while (r.MoveToNextAttribute())
            switch (r.LocalName)
            {
                case "attackMethodType": methodType = (int)Atoll(values.Of(r)); break;
                case "hitImmuneBreak": hitImmuneBreak = (int)Atoll(values.Of(r)); break;
                case "grabTargetType": grabType = (int)Atoll(values.Of(r)); break;
            }
        r.MoveToElement();
        bool damage = false;
        var d = shared.Scratch;
        d.Reset();
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (SkillDamageTable.Child(r, depth))
            {
                if (damage || !SkillDamageTable.Is(r, "damage")) continue;
                damage = true;
                while (r.MoveToNextAttribute()) d.Set(r.LocalName, r, values);
                r.MoveToElement();
                if (d.PushEaseType < 0 || d.PushEaseType > 2) d.PushEaseType = 0;
            }
        }
        if (!damage && hitImmuneBreak == 0 && methodType == 0 && grabType == 0) return None;
        d.HitImmuneBreak = hitImmuneBreak;
        d.AttackMethodType = methodType;
        d.GrabTargetType = grabType;
        return shared.Get();
    }

    /// <summary>The read's attacks by value: equal ones are one object (the table is read only), so the many levels of a
    /// skill whose attacks differ in nothing take one each.</summary>
    internal sealed class Shared : IEqualityComparer<SkillAttackDamage>
    {
        readonly HashSet<SkillAttackDamage> kept;
        /// <summary>The attack being read.</summary>
        public readonly SkillAttackDamage Scratch = new();

        public Shared() => kept = new HashSet<SkillAttackDamage>(this);

        /// <summary>The kept attack equal to <see cref="Scratch"/>, kept as a copy when there is none yet.</summary>
        public SkillAttackDamage Get()
        {
            if (kept.TryGetValue(Scratch, out var same)) return same;
            var copy = (SkillAttackDamage)Scratch.MemberwiseClone();
            kept.Add(copy);
            return copy;
        }

        public bool Equals(SkillAttackDamage a, SkillAttackDamage b) =>
            a.Count == b.Count && a.Hand == b.Hand && a.Rate.Equals(b.Rate) && a.Value == b.Value
            && a.HitSpeedRate.Equals(b.HitSpeedRate) && a.HitPauseTime.Equals(b.HitPauseTime)
            && a.DamagedVibrateRatio.Equals(b.DamagedVibrateRatio) && a.EffectHit == b.EffectHit && a.EffectRemain == b.EffectRemain
            && a.DamageByTargetMaxHP.Equals(b.DamageByTargetMaxHP) && a.ConstDamageByTargetHP == b.ConstDamageByTargetHP
            && a.Push == b.Push && a.PushDistance.Equals(b.PushDistance) && a.PushDuration.Equals(b.PushDuration)
            && a.PushProb.Equals(b.PushProb) && a.PushEaseType == b.PushEaseType && a.PushApplyField == b.PushApplyField
            && a.PushUpDistance.Equals(b.PushUpDistance) && a.PushDown == b.PushDown && a.PushFall == b.PushFall
            && a.PushPriority == b.PushPriority && a.PushPriorityHitImmune == b.PushPriorityHitImmune
            && a.PushAnimation == b.PushAnimation && a.StateHitIgnore == b.StateHitIgnore && a.StateHitOnly == b.StateHitOnly
            && a.SuperArmorBreak == b.SuperArmorBreak && a.AttackMaterial == b.AttackMaterial
            && a.InvokeEffectDisable == b.InvokeEffectDisable && a.HitImmuneBreak == b.HitImmuneBreak
            && a.AttackMethodType == b.AttackMethodType && a.GrabTargetType == b.GrabTargetType;

        public int GetHashCode(SkillAttackDamage a) =>
            HashCode.Combine(a.Value, a.Rate, a.Count, a.Push, a.PushDistance, a.EffectHit, a.HitSpeedRate, a.PushProb);
    }

    /// <summary>The ctor's values (:175).</summary>
    void Reset()
    {
        Count = 1; Hand = 0; Rate = 0f; Value = 0; HitSpeedRate = 1f; HitPauseTime = 0f; DamagedVibrateRatio = 1f;
        EffectHit = ""; EffectRemain = ""; DamageByTargetMaxHP = 0f; ConstDamageByTargetHP = 0;
        Push = 0; PushDistance = 0f; PushDuration = 0f; PushProb = 1f; PushEaseType = 0; PushApplyField = 0; PushUpDistance = 0f;
        PushDown = 0; PushFall = 0; PushPriority = 0; PushPriorityHitImmune = 0; PushAnimation = "";
        StateHitIgnore = 0; StateHitOnly = 0; SuperArmorBreak = 0; AttackMaterial = 0; InvokeEffectDisable = 0;
        HitImmuneBreak = 0; AttackMethodType = 0; GrabTargetType = 0;
    }

    // XmlGetAttr: a missing attribute leaves the value; only the attributes read here take their value
    void Set(string name, XmlReader r, AttributeValues v)
    {
        switch (name)
        {
            case "count": Count = (int)Atoll(v.Of(r)); break;
            case "hand": Hand = (int)Atoll(v.Of(r)); break;
            case "rate": Rate = Float(v.Of(r)); break;
            case "hitSpeedRate": HitSpeedRate = Float(v.Of(r)); break;
            case "hitPauseTime": HitPauseTime = Float(v.Of(r)); break;
            case "damagedVibrateRatio": DamagedVibrateRatio = Float(v.Of(r)); break;
            case "value": Value = Atoll(v.Of(r)); break;
            case "effectRemain": EffectRemain = v.String(r); break;
            case "effectHit": EffectHit = v.String(r); break;
            case "damageByTargetMaxHP": DamageByTargetMaxHP = Float(v.Of(r)); break;
            case "constDamageByTargetHP": ConstDamageByTargetHP = Atoll(v.Of(r)); break;
            case "push": Push = (int)Atoll(v.Of(r)); break;
            case "pushdistance": PushDistance = Float(v.Of(r)); break;
            case "pushduration": PushDuration = Float(v.Of(r)); break;
            case "pushprob": PushProb = Float(v.Of(r)); break;
            case "pushEaseType": PushEaseType = (int)Atoll(v.Of(r)); break;
            case "pushApplyField": PushApplyField = (int)Atoll(v.Of(r)); break;
            case "pushUpDistance": PushUpDistance = Float(v.Of(r)); break;
            case "pushDown": PushDown = (int)Atoll(v.Of(r)); break;
            case "pushFall": PushFall = (int)Atoll(v.Of(r)); break;
            case "pushPriority": PushPriority = (int)Atoll(v.Of(r)); break;
            case "pushPriorityHitImmune": PushPriorityHitImmune = (int)Atoll(v.Of(r)); break;
            case "pushAnimation": PushAnimation = v.String(r); break;
            case "stateHitIgnore": StateHitIgnore = (int)Atoll(v.Of(r)); break;
            case "stateHitOnly": StateHitOnly = (int)Atoll(v.Of(r)); break;
            case "superArmorBreak": SuperArmorBreak = (int)Atoll(v.Of(r)); break;
            case "attackMaterial": AttackMaterial = (int)Atoll(v.Of(r)); break;
            case "invokeEffectDisable": InvokeEffectDisable = (int)Atoll(v.Of(r)); break;
        }
    }

    static float Float(ReadOnlySpan<char> s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

    internal static long Atoll(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        return neg ? -v : v;
    }
}

/// <summary>The value of the attribute at a reader, read into a buffer kept for the read (XmlReader.ReadValueChunk), so a
/// number is parsed without a string and a repeated text value is one string (atomized in the read's name table).</summary>
internal sealed class AttributeValues
{
    readonly XmlNameTable names;
    char[] buffer = new char[64];
    int length;

    public AttributeValues(XmlNameTable names) => this.names = names;

    /// <summary>The value's characters, valid until the next read.</summary>
    public ReadOnlySpan<char> Of(XmlReader r)
    {
        if (!r.CanReadValueChunk) return r.Value;
        length = 0;
        int n;
        while ((n = r.ReadValueChunk(buffer, length, buffer.Length - length)) > 0)
            if ((length += n) == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
        return buffer.AsSpan(0, length);
    }

    /// <summary>The value as a string: "" when empty, else the name table's string for those characters.</summary>
    public string String(XmlReader r)
    {
        if (!r.CanReadValueChunk) return names.Add(r.Value);
        return Of(r).Length == 0 ? "" : names.Add(buffer, 0, length);
    }
}

/// <summary>
/// The attacks' &lt;damage&gt; blocks of every skill, by skill, level, motion and attack: the same files and the same walk
/// as <see cref="SkillDataTable"/> (a skill without &lt;basic&gt; has no levels, the &lt;level&gt; rows by FUN_14028faa0,
/// a level below 1 ends the skill's rows, a later skill with the same id replacing). Read on first use; read only.
/// The files are read with a forward-only reader that keeps the &lt;damage&gt; attributes and the level rows' keys, not a
/// document per file: the field entry reads every skilldata file.
/// </summary>
public sealed class SkillDamageTable
{
    /// <summary>A &lt;level&gt; element as FUN_14028faa0 picks among them: its "value", "feature" and "locale" ("" when
    /// missing) and the attacks of its &lt;motion&gt; children.</summary>
    public sealed class LevelRow
    {
        public string Value = "", Feature = "", Locale = "";
        internal SkillAttackDamage[][] Motions;
    }

    readonly Func<IEnumerable<TextReader>> files;
    readonly Func<int, TextReader> whole;
    readonly Func<List<LevelRow>, IEnumerable<LevelRow>> pick;
    /// <summary>The read: every skill id with its highest level kept (0 for none), and each kept level's motions by
    /// <see cref="Key"/>; one map for all the skills' levels, not one per skill.</summary>
    sealed class Loaded
    {
        public readonly Dictionary<uint, ushort> Skills = new();
        public readonly Dictionary<long, SkillAttackDamage[][]> Levels = new();
    }

    static long Key(uint skillId, ushort level) => (long)skillId << 16 | level;

    Loaded skills;
    readonly object loadLock = new();

    public static readonly SkillDamageTable Empty = new(() => Array.Empty<TextReader>(), rows => rows);

    /// <summary>A file's text opened on its first read, so that an open that throws (a bad zlib header, the mark sniff of the
    /// first bytes) throws inside the table's read of that file and is retried by the whole-entry route like a read that
    /// throws; a file the retry reads whole is disposed without being opened.</summary>
    public static TextReader Deferred(Func<TextReader> open) => new DeferredReader(open);

    /// <summary>The files' text in <paramref name="names"/>' order: <see cref="Deferred"/> for a file that exists and null for
    /// one that does not, so the read skips a missing file (an empty deferred text would throw, the whole-entry retry would
    /// throw on the missing entry, and the table would be stored empty).</summary>
    public static IEnumerable<TextReader> DeferExisting(IEnumerable<string> names, Func<string, bool> exists, Func<string, TextReader> open) =>
        names.Select(n => exists(n) ? Deferred(() => open(n)) : null);

    sealed class DeferredReader(Func<TextReader> open) : TextReader
    {
        TextReader text;
        bool opened;
        TextReader Text()
        {
            if (!opened) { opened = true; text = open(); }
            return text;
        }
        public override int Peek() => Text()?.Peek() ?? -1;
        public override int Read() => Text()?.Read() ?? -1;
        public override int Read(char[] buffer, int index, int count) => Text()?.Read(buffer, index, count) ?? 0;
        public override int Read(Span<char> buffer) => Text()?.Read(buffer) ?? 0;
        protected override void Dispose(bool disposing)
        {
            if (disposing) { opened = true; text?.Dispose(); text = null; }
            base.Dispose(disposing);
        }
    }

    /// <param name="files">The skilldata files' text, in load order (each reader is disposed when read; null for a missing
    /// file).</param>
    /// <param name="pick">FUN_14028faa0 over a skill's &lt;level&gt; rows, in the elements' order.</param>
    /// <param name="whole">The i-th file's text by the whole-entry read, for a file whose streamed read failed (the pack
    /// read that falls back to the package's reader); null for none.</param>
    public SkillDamageTable(Func<IEnumerable<TextReader>> files, Func<List<LevelRow>, IEnumerable<LevelRow>> pick,
        Func<int, TextReader> whole = null)
    {
        this.files = files;
        this.pick = pick;
        this.whole = whole;
    }

    /// <summary>Reads the files now (on the calling thread), so the first <see cref="Get"/> finds them read; the field entry
    /// runs it on a worker thread. A reader that comes while it runs waits for it instead of reading them a second time.</summary>
    public void Load() => All();

    static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true, IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    Loaded All()
    {
        var read = System.Threading.Volatile.Read(ref skills);
        if (read != null) return read;
        lock (loadLock)
        {
            if (skills != null) return skills;
            // a file whose read throws is read again by the whole-entry route, from the start (the read so far holds part of
            // it); when that fails too the table stays empty, stored, so no later hit reads the files again on the frame
            var retry = new HashSet<int>();
            Loaded d;
            for (;;)
            {
                int at = -1;
                try { d = Read(retry, ref at); break; }
                catch (Exception) when (whole != null && at >= 0 && retry.Add(at)) { }
                catch (Exception) { d = new Loaded(); break; }
            }
            System.Threading.Volatile.Write(ref skills, d);
            return d;
        }
    }

    /// <summary>One read of every file, the files in <paramref name="retry"/> by the whole-entry route;
    /// <paramref name="at"/> is the file being read when it throws.</summary>
    Loaded Read(HashSet<int> retry, ref int at)
    {
        var d = new Loaded();
        // one name table for the read's files: each file's reader atomizes the same element and attribute names
        var settings = Settings.Clone();
        settings.NameTable = new NameTable();
        var values = new AttributeValues(settings.NameTable);
        var shared = new SkillAttackDamage.Shared();
        var rows = new List<LevelRow>();
        var motions = new List<SkillAttackDamage[]>();
        var attacks = new List<SkillAttackDamage>();
        foreach (var streamed in files())
        {
            at++;
            var text = streamed;
            if (retry.Contains(at)) { streamed?.Dispose(); text = whole(at); }
            if (text == null) continue;
            using var r = XmlReader.Create(text, settings);
            if (r.MoveToContent() != XmlNodeType.Element || !Is(r, "ms2") || r.IsEmptyElement) continue;
            while (Child(r, 0))
                if (Is(r, "skill")) Levels(r, values, shared, d, SkillId(r, values), rows, motions, attacks);
        }
        return d;
    }

    /// <summary>The skill's "id" (0 when missing), the reader left on the element.</summary>
    static uint SkillId(XmlReader r, AttributeValues values)
    {
        uint id = 0;
        while (r.MoveToNextAttribute())
            if (r.LocalName == "id") { id = (uint)SkillAttackDamage.Atoll(values.Of(r)); break; }
        r.MoveToElement();
        return id;
    }

    /// <summary>The skill at the reader, read to its end into <paramref name="d"/>, replacing the levels of an earlier skill
    /// with the same id (the lists are the read's, reused from skill to skill).</summary>
    void Levels(XmlReader r, AttributeValues values, SkillAttackDamage.Shared shared, Loaded d, uint id, List<LevelRow> rows, List<SkillAttackDamage[]> motions, List<SkillAttackDamage> attacks)
    {
        if (d.Skills.TryGetValue(id, out ushort old))
            for (int l = 1; l <= old; l++) d.Levels.Remove(Key(id, (ushort)l));
        d.Skills[id] = 0;
        if (r.IsEmptyElement) return;
        rows.Clear();
        bool basic = false;
        int depth = r.Depth;
        while (Child(r, depth))
        {
            if (Is(r, "basic")) { basic = true; continue; }
            if (!Is(r, "level")) continue;
            var row = new LevelRow();
            while (r.MoveToNextAttribute())
                switch (r.LocalName)
                {
                    case "value": row.Value = values.String(r); break;
                    case "feature": row.Feature = values.String(r); break;
                    case "locale": row.Locale = values.String(r); break;
                }
            r.MoveToElement();
            motions.Clear();
            if (!r.IsEmptyElement)
            {
                int levelDepth = r.Depth;
                while (Child(r, levelDepth))
                {
                    if (!Is(r, "motion")) continue;
                    attacks.Clear();
                    if (!r.IsEmptyElement)
                    {
                        int motionDepth = r.Depth;
                        while (Child(r, motionDepth))
                            if (Is(r, "attack")) attacks.Add(SkillAttackDamage.Read(r, values, shared));
                    }
                    motions.Add(attacks.ToArray());
                }
            }
            row.Motions = motions.ToArray();
            rows.Add(row);
        }
        if (!basic) return;
        ushort max = 0;
        foreach (var l in pick(rows))
        {
            var level = (ushort)Xml.Atoi(l.Value);
            if (level < 1) break;
            d.Levels[Key(id, level)] = l.Motions;
            if (level > max) max = level;
        }
        d.Skills[id] = max;
    }

    /// <summary>Moves to the next child element of the element at <paramref name="parentDepth"/> (its descendants'
    /// nodes skipped); false at the parent's end.</summary>
    internal static bool Child(XmlReader r, int parentDepth)
    {
        while (r.Read())
        {
            if (r.Depth <= parentDepth) return false;
            if (r.NodeType == XmlNodeType.Element && r.Depth == parentDepth + 1) return true;
        }
        return false;
    }

    /// <summary>An element named <paramref name="name"/> without a namespace (XName <paramref name="name"/>).</summary>
    internal static bool Is(XmlReader r, string name) => r.LocalName == name && r.NamespaceURI.Length == 0;

    /// <summary>
    /// CDamageRecord::GetAttackData (@0x14175a8e0, skillrecord.cpp:501) then m_damage: SkillDataTable::GetSkillData,
    /// SkillData::GetLevelData, SkillLevelData::GetAttack (skillleveldata.cpp:750: GetMotion :742 and
    /// SkillMotionData::GetAttack :1080, null past the rows). Null for an unknown skill, level, motion or attack.
    /// </summary>
    public SkillAttackDamage Get(int skillId, int level, int motion, int attack)
    {
        if (skillId == 0 || level < 0 || level > ushort.MaxValue || motion < 0 || attack < 0) return null;
        if (!All().Levels.TryGetValue(Key((uint)skillId, (ushort)level), out var motions)) return null;
        if (motion >= motions.Length || attack >= motions[motion].Length) return null;
        return motions[motion][attack];
    }

    /// <summary>The skill ids read.</summary>
    public IEnumerable<uint> SkillIds => All().Skills.Keys;

    /// <summary>CDamageRecord::Init's test (@0x14175a610, skillrecord.cpp:465): the skill and its level exist.</summary>
    public bool HasLevel(int skillId, int level) =>
        skillId != 0 && level is >= 0 and <= ushort.MaxValue && All().Levels.ContainsKey(Key((uint)skillId, (ushort)level));
}
