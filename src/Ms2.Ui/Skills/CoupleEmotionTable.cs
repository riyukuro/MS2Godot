using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Skills;

/// <summary>
/// CCoupleEmotionTable (commonfiles/coupleemotiontable.cpp), the animations two players play together in a couple emotion,
/// per gender pairing. Load (@0x1402c6f50, :109-218) reads Table/coupleEmotion.xml (GetXmlPath: the locale folder first)
/// on first use: every &lt;coupleEmotion&gt; child of &lt;ms2&gt; (no feature check), its "code" (GetUIntAttr), and three
/// animation sets: genders (0, 1) from animationDiffGender_female (the request list), _male (the target list), _pathNif
/// and _loop; (0, 0) from animationSameGenderMale_requestUser, _targetUser, _pathNif and _loop; (1, 1) the same with
/// animationSameGenderFemale_. Each list attribute loses its '[', ']' and ' ' (CString::Remove) and is split at ","
/// (FUN_140275080, CString::Tokenize: empty parts skipped). teleport_effect is read as it is. A row whose sets fail
/// Validate (@0x1402c7ed0, ValidateAni @0x1402c8100: the nif count equals the request count, the request count equals
/// the target count) is left out; a later row with a code replaces an earlier one (SetAt).
/// </summary>
public sealed class CoupleEmotionTable
{
    /// <summary>CCoupleEmotionData::AnimationData: the request and target animation lists, the path nifs (one per
    /// animation, under Data/Resource/Model/Path/) and the loop flag (Unk_CoupleEmotionBool, false by default).</summary>
    public sealed record AnimationData(IReadOnlyList<string> RequestAnis, IReadOnlyList<string> TargetAnis,
        IReadOnlyList<string> NifPaths, bool Loop);

    /// <summary>CCoupleEmotionData: the code, the sets keyed by MAKELONG(g1, g2) with g1 &lt;= g2, the teleport effect
    /// (null when the attribute is absent).</summary>
    public sealed class Data
    {
        public uint Id { get; init; }
        public string TeleportEffect { get; init; }
        internal readonly Dictionary<int, AnimationData> Anis = new();

        /// <summary>CCoupleEmotionData::GetAni (@0x1402c6d70): the set of a gender pairing, the order of the two genders
        /// does not matter; null when none.</summary>
        public AnimationData GetAni(int gender1, int gender2)
        {
            if (gender1 > gender2) (gender1, gender2) = (gender2, gender1);
            return Anis.TryGetValue(MakeLong(gender1, gender2), out var a) ? a : null;
        }

        /// <summary>The three pairings Load adds, in its order.</summary>
        public IEnumerable<(int Gender1, int Gender2, AnimationData Ani)> All()
        {
            foreach (var (g1, g2) in Pairs)
                if (GetAni(g1, g2) is { } a) yield return (g1, g2, a);
        }
    }

    /// <summary>The pairings Load adds (:131, :156, :181): different genders, two males, two females.</summary>
    public static readonly (int, int)[] Pairs = { (0, 1), (0, 0), (1, 1) };

    /// <summary>Load's attribute prefix and request / target suffixes of each pairing.</summary>
    static readonly (int G1, int G2, string Prefix, string Request, string Target)[] Sets =
    {
        (0, 1, "animationDiffGender_", "female", "male"),
        (0, 0, "animationSameGenderMale_", "requestUser", "targetUser"),
        (1, 1, "animationSameGenderFemale_", "requestUser", "targetUser"),
    };

    public static readonly CoupleEmotionTable Empty = new(null);

    readonly PackSet packs;
    Dictionary<uint, Data> rows;
    int invalid;

    public CoupleEmotionTable(PackSet packs) { this.packs = packs; }

    /// <summary>CCoupleEmotionTable::GetData (@0x1402c7e10): the row of a code, null when none (or left out by Validate).</summary>
    public Data GetData(uint id)
    {
        Load();
        return rows.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>The kept rows.</summary>
    public IReadOnlyCollection<Data> Rows { get { Load(); return rows.Values; } }

    /// <summary>The rows Validate left out.</summary>
    public int InvalidCount { get { Load(); return invalid; } }

    void Load()
    {
        if (rows != null) return;
        rows = new();
        if (packs == null || TablePath.Resolve(packs, "coupleemotion.xml") is not { } path) return;
        rows = Parse(GameStrings.Parse(packs.Read("Xml", path)).Root, out invalid);
    }

    /// <summary>Load's body over the document root (no &lt;ms2&gt;: no rows).</summary>
    public static Dictionary<uint, Data> Parse(XElement root, out int invalid)
    {
        var rows = new Dictionary<uint, Data>();
        invalid = 0;
        if (root == null || root.Name.LocalName != "ms2") return rows;
        foreach (var e in root.Elements("coupleEmotion"))
        {
            var d = new Data
            {
                Id = unchecked((uint)ReservedWords.Atoi((string)e.Attribute("code") ?? "")),
                TeleportEffect = (string)e.Attribute("teleport_effect"),
            };
            foreach (var s in Sets)
                d.Anis[MakeLong(s.G1, s.G2)] = new AnimationData(List(e, s.Prefix + s.Request), List(e, s.Prefix + s.Target),
                    List(e, s.Prefix + "pathNif"), Bool(e, s.Prefix + "loop", false));
            if (!Validate(d)) { invalid++; continue; }
            rows[d.Id] = d;
        }
        return rows;
    }

    /// <summary>Validate / ValidateAni (@0x1402c7ed0, @0x1402c8100).</summary>
    public static bool Validate(Data d)
    {
        foreach (var a in d.Anis.Values)
        {
            if (a.NifPaths.Count != a.RequestAnis.Count) return false;
            if (a.RequestAnis.Count != a.TargetAnis.Count) return false;
        }
        return true;
    }

    static int MakeLong(int low, int high) => (ushort)low | (high << 16);

    /// <summary>Unk_ReadString (default ""), Remove('['), Remove(']'), Remove(' '), then Tokenize at ",".</summary>
    static string[] List(XElement e, string name) =>
        ((string)e.Attribute(name) ?? "").Replace("[", "").Replace("]", "").Replace(" ", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Unk_CoupleEmotionBool (@0x1402c8970): XmlGetAttr (TiXmlElement::QueryBoolAttribute: true / yes / 1 and
    /// false / no / 0, any case); anything else or no attribute gives the default.</summary>
    static bool Bool(XElement e, string name, bool def)
    {
        var s = (string)e.Attribute(name);
        if (s == null) return def;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
        return def;
    }

    /// <summary>The node names of statecoupleemotion.cpp (PTR_DAT_1439f4da0 / da8 / PTR_s_Eff_center_1439f4db0).</summary>
    public const string NodeB = "B", NodeA = "A", CenterNode = "Eff_center";

    /// <summary>
    /// CoupleEmotionAnimInfo::Init (@0x140b7fcc0, statecoupleemotion.cpp:217-245) for one actor: the row of the emotion
    /// (null when none, or no set for the pairing), its teleport effect, loop flag and nifs, and the actor's own node, the
    /// partner's node and the actor's animations: when FUN_140b85080 (@0x140b85080: equal genders give swap, otherwise
    /// gender1 != 0) holds, node "B" with the request list and "A" for the partner; otherwise "A" with the target list.
    /// </summary>
    public AnimInfo Init(uint emotion, int gender1, int gender2, bool swap) => Init(GetData(emotion), gender1, gender2, swap);

    /// <summary>Init over a row already looked up (null: null).</summary>
    public static AnimInfo Init(Data d, int gender1, int gender2, bool swap)
    {
        if (d == null || d.GetAni(gender1, gender2) is not { } a) return null;
        bool first = gender1 == gender2 ? swap : gender1 != 0;
        return first
            ? new AnimInfo(d.TeleportEffect, a.Loop, a.NifPaths, NodeB, NodeA, a.RequestAnis)
            : new AnimInfo(d.TeleportEffect, a.Loop, a.NifPaths, NodeA, NodeB, a.TargetAnis);
    }

    /// <summary>CoupleEmotionAnimInfo's table part.</summary>
    public sealed record AnimInfo(string TeleportEffect, bool Loop, IReadOnlyList<string> NifPaths, string OwnerNode,
        string PeerNode, IReadOnlyList<string> AniNames)
    {
        /// <summary>GetAniName (@0x140b806a0): the animation at an index, "" when out of range.</summary>
        public string AniName(int ani) => ani < 0 || ani >= AniNames.Count ? "" : AniNames[ani];

        /// <summary>GetLastAni (@0x140b861f0).</summary>
        public int LastAni => AniNames.Count - 1;

        /// <summary>Load (@0x140b7ff20): "Data/Resource/Model/Path/%s" of the nif at an index, null when out of range.</summary>
        public string NifPath(int ani) => ani < 0 || ani >= NifPaths.Count ? null : "Data/Resource/Model/Path/" + NifPaths[ani];
    }
}
