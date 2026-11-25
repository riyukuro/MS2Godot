using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// CObjectData (commonfiles/objectdata.h, 0x2e0 bytes): one object's Xml.m2d : Object/%08d.xml as GetObjectData
/// (@0x140704ec0, objectdata.cpp:91-378) reads it. &lt;ms2&gt; is required (no root: no data). &lt;FunctionCube&gt; fills
/// <see cref="Cube"/> (m_cube, +0x30) and sets its Enabled; &lt;ConfigurableCube&gt; fills <see cref="Config"/> (m_config, +0x2d0).
/// <para>The function cube objects take this block as their row (CFunctionCubeObject::vfunc_2, functioncubeobject.cpp:507,
/// is passed &amp;m_cube): the row's kind (+0x10) is <see cref="FunctionCube.ControlType"/>, its sub-kind (+0x14)
/// <see cref="FunctionCube.UiFunction"/>, its sub (+0x08) <see cref="FunctionCube.StateChangeAction"/>, its first state (+0x0c)
/// <see cref="FunctionCube.DefaultState"/> and its popup flag <see cref="AdditionalEffect.Popup"/>.</para>
/// <para>Attributes go through XmlGetAttr (xmlattr.h): a missing attribute leaves the field's default, an int reads like
/// QueryIntAttribute's sscanf "%d", a float like "%f"; string reads (FUN_140188f50(attr, 0)) give "" for a missing one.
/// Lists (XmlSplit / XmlTokenList) split with CString::Tokenize, which skips empty tokens, and atoi each one.</para>
/// <para>STILL-OPEN: the fitting doll skin colours go through FUN_14028c0d0 (a colour string to int, not recovered); the raw
/// strings are kept in <see cref="FittingDoll.SkinColorText"/>.</para>
/// </summary>
public sealed class ObjectData
{
    public uint Id;
    public FunctionCube Cube = new();
    public ConfigurableCube Config = new();

    public sealed class FunctionCube
    {
        /// <summary>m_bEnabled: the file has a &lt;FunctionCube&gt; (HasFunctionCube, objectdata.cpp:381).</summary>
        public bool Enabled;
        public int UseKfm;
        /// <summary>StateChangeAction: 1 "ActionKey", 2 "Attack", else 0.</summary>
        public int StateChangeAction;
        public int DefaultState;
        /// <summary>ControlType (:117-147): 2 Sensor, 3 Switch / FunctionUI / Ride / OpenWeb, 4 SpawnNPC, 5 Skill, 6 Portal,
        /// 7 Breeding, 8 Farming, 9 PVP, 10 SpawnPoint, 0xb InstallNPC, 0xc Notice, 0xd Nurturing, 1 anything else.</summary>
        public int ControlType;
        /// <summary>UseUiFunction for a FunctionUI cube only (:154-170): 1 TriggerTool, 2 FittingDoll, 3 itemFusion, 4 bank,
        /// 5 BeautyMirror, 6 Password, 7 Summon; 0 otherwise.</summary>
        public int UiFunction;
        /// <summary>camera SetCamera: 1 TPSCamera, 2 FPSCamera, 3 2DCamera, 4 RPGCamera, 5 PVPCamera, 6 ConsoleCamera.</summary>
        public int Camera;
        public int ForbidJump, ForbidClimb, ForbidSkill, ForbidPlayInstrument, ForbidExceptOwner;
        public int EnableTriggerControl;
        public int StateChangePermission;
        public string ControlTypeName = "";
        /// <summary>m_sequences: 0 ZeroSeq, 1 FirstSeq, 2 SecondSeq, 3 ZeroIngSeq, 4 FirstIngSeq, 5 SecondIngSeq.</summary>
        public readonly string[] Sequences = { "", "", "", "", "", "" };
        /// <summary>m_effects, in the same order as <see cref="Sequences"/>.</summary>
        public readonly string[] Effects = { "", "", "", "", "", "" };
        /// <summary>ZeroPhysics / FirstPhysics / SecondPhysics.</summary>
        public readonly int[] Physics = new int[3];
        public int SkillId; public short SkillLevel; public int SkillFriendly;
        public float SpawnRadius; public int SpawnNpcCount; public int SpawnNpcId; public float SpawnRegenCheckTime;
        public int FusionTemperature, FusionSacrifice;
        public FittingDoll Doll;
        /// <summary>AutoStateChange "A-B" (only a two-part value sets them) and AutoStateChangeTime.</summary>
        public int AutoStateA, AutoStateB, AutoStateTime;
        public FirstPcAnimation FirstAnimation;
        public AdditionalEffect FirstEffect = new();
        public int RecipeId; public string ReactableEffect = "";
        public Nurturing Nurture;
        public bool PvpEnabled; public int PvpTeam;
        public string WebUrl = "", WebTarget = ""; public int WebOpenType;
        public string NoticeType = "";
    }

    public sealed class FittingDoll
    {
        public int Gender, Type;
        /// <summary>fittingdollAsset, else FittingDoll_F_Default01 for gender 1 and FittingDoll_M_Default01 otherwise.</summary>
        public string Asset = "";
        public int[] DefaultItems = Array.Empty<int>();
        public readonly string[] SkinColorText = { "", "", "" };
    }

    public sealed class FirstPcAnimation
    {
        public string Animation = "", Bone = "", EmotionId = "", EffectStart = "", EffectEnd = "";
        public int UsePcMove;
        /// <summary>Set only from a three-part value.</summary>
        public float[] Translation = { 0, 0, 0 }, Rotation = { 0, 0, 0 };
    }

    /// <summary>firstAdditionalEffect (m_firstEffect): Enabled once the element is there.</summary>
    public sealed class AdditionalEffect
    {
        public bool Enabled;
        public int EffectId; public short Level; public int Target; public float Range;
        /// <summary>firstAdditionalPopup: the cube object shows a UIBootyPopup (functioncubeobject.cpp:575).</summary>
        public int Popup;
    }

    public sealed class Nurturing
    {
        public string QuestTag = "";
        public (int Item, int Rarity, int Count) RewardByFeeding, Reward;
        /// <summary>Keyed from 1 in list order.</summary>
        public readonly Dictionary<short, ulong> RequiredGrowth = new();
        public readonly Dictionary<short, int> PluralNpcIds = new();
        public readonly Dictionary<short, (int Item, int Rarity, int Count)> RewardByGrowth = new();
    }

    public sealed class ConfigurableCube
    {
        public int Enabled; public string Id = "";
    }

    /// <summary>GetObjectData(id) from the packs: null when Object/%08d.xml is missing or has no &lt;ms2&gt; root.</summary>
    public static ObjectData Load(PackSet packs, uint id)
    {
        string path = string.Format(CultureInfo.InvariantCulture, "object/{0:D8}.xml", id);
        if (packs == null || !packs.Exists("Xml", path)) return null;
        return Parse(PackXml.Parse(packs.Read("Xml", path)), id);
    }

    /// <summary>The object from its xml (tests and <see cref="Load"/>).</summary>
    public static ObjectData Parse(XDocument doc, uint id)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return null;
        var d = new ObjectData { Id = id };
        if (root.Element("FunctionCube") is { } f) ReadCube(f, d.Cube);
        // a file without <ConfigurableCube> keeps m_bEnabled 0 (:369-376)
        if (root.Element("ConfigurableCube") is { } c)
        {
            d.Config.Enabled = Int(c, "enable", d.Config.Enabled);
            d.Config.Id = (string)c.Attribute("id") ?? d.Config.Id;
        }
        return d;
    }

    static void ReadCube(XElement f, FunctionCube c)
    {
        c.Enabled = true;
        c.UseKfm = Int(f, "UseKfm", c.UseKfm);
        c.DefaultState = Int(f, "DefaultState", c.DefaultState);
        string type = Str(f, "ControlType");
        c.ControlTypeName = type;
        c.ControlType = type switch
        {
            "Sensor" => 2,
            "Switch" or "FunctionUI" or "Ride" or "OpenWeb" => 3,
            "SpawnNPC" => 4,
            "InstallNPC" => 0xb,
            "Skill" => 5,
            "Portal" => 6,
            "Breeding" => 7,
            "Farming" => 8,
            "PVP" => 9,
            "SpawnPoint" => 10,
            "Notice" => 0xc,
            "Nurturing" => 0xd,
            _ => 1,
        };
        c.StateChangeAction = Str(f, "StateChangeAction") switch { "ActionKey" => 1, "Attack" => 2, _ => 0 };
        c.StateChangePermission = Int(f, "StateChangeActionPermissionType", 0);
        string ui = Str(f, "UseUiFunction");
        if (type == "FunctionUI")
            c.UiFunction = ui switch
            {
                "TriggerTool" => 1, "FittingDoll" => 2, "itemFusion" => 3, "bank" => 4,
                "BeautyMirror" => 5, "Password" => 6, "Summon" => 7, _ => c.UiFunction,
            };
        // Attribute() of a missing name is NULL in the CString assignment: an empty string
        string[] seq = { "ZeroSeq", "FirstSeq", "SecondSeq", "ZeroIngSeq", "FirstIngSeq", "SecondIngSeq" };
        string[] fx = { "ZeroEffect", "FirstEffect", "SecondEffect", "ZeroIngEffect", "FirstIngEffect", "SecondIngEffect" };
        for (int i = 0; i < 6; i++) { c.Sequences[i] = Str(f, seq[i]); c.Effects[i] = Str(f, fx[i]); }
        c.Physics[0] = Int(f, "ZeroPhysics", 0); c.Physics[1] = Int(f, "FirstPhysics", 0); c.Physics[2] = Int(f, "SecondPhysics", 0);
        var auto = Split(Str(f, "AutoStateChange"), '-');
        if (auto.Length == 2) { c.AutoStateA = auto[0]; c.AutoStateB = auto[1]; }
        c.AutoStateTime = Int(f, "AutoStateChangeTime", c.AutoStateTime);
        c.EnableTriggerControl = Int(f, "enableTriggerControl", c.EnableTriggerControl);
        c.RecipeId = Int(f, "receipeID", c.RecipeId);
        c.ReactableEffect = (string)f.Attribute("reactableEffect") ?? c.ReactableEffect;
        if (f.Element("camera") is { } cam)
            c.Camera = Str(cam, "SetCamera") switch
            {
                "TPSCamera" => 1, "FPSCamera" => 2, "2DCamera" => 3, "RPGCamera" => 4, "PVPCamera" => 5, "ConsoleCamera" => 6, _ => c.Camera,
            };
        if (f.Element("spawner") is { } sp)
        {
            c.SpawnRadius = Float(sp, "radius", c.SpawnRadius);
            c.SpawnNpcCount = Int(sp, "npcCount", c.SpawnNpcCount);
            c.SpawnNpcId = Int(sp, "npcID", c.SpawnNpcId);
            c.SpawnRegenCheckTime = Float(sp, "regenCheckTime", c.SpawnRegenCheckTime);
        }
        if (f.Element("fusioner") is { } fu)
        {
            c.FusionTemperature = Int(fu, "temperature", c.FusionTemperature);
            c.FusionSacrifice = Int(fu, "sacrifice", c.FusionSacrifice);
        }
        if (f.Element("fittingdoll") is { } fd)
        {
            var doll = c.Doll = new FittingDoll();
            doll.Gender = Int(fd, "fittingdollGender", 0);
            doll.Type = Int(fd, "fittingdollType", 0);
            string asset = (string)fd.Attribute("fittingdollAsset") ?? "";
            doll.Asset = asset.Length != 0 ? asset : doll.Gender == 1 ? "FittingDoll_F_Default01" : "FittingDoll_M_Default01";
            doll.DefaultItems = Split(Str(fd, "fittingdollDefaultItem"), ',');
            for (int i = 0; i < 3; i++) doll.SkinColorText[i] = (string)fd.Attribute("fittingdollSkinColor" + i) ?? "";
        }
        if (f.Element("skillobject") is { } sk)
        {
            c.SkillId = Int(sk, "skillID", c.SkillId);
            c.SkillLevel = (short)Int(sk, "skillLevel", c.SkillLevel);
            c.SkillFriendly = Int(sk, "friendly", c.SkillFriendly);
        }
        if (f.Element("firstPCAnimation") is { } fa)
        {
            var a = c.FirstAnimation = new FirstPcAnimation();
            a.Animation = Str(fa, "firstAnimation");
            a.UsePcMove = Int(fa, "firstUsePCMove", 0);
            a.Bone = Str(fa, "firstPCBone");
            var t = Floats(Str(fa, "firstPCTranslation"));
            if (t.Length == 3) a.Translation = t;
            var r = Floats(Str(fa, "firstPCRotation"));
            if (r.Length == 3) a.Rotation = r;
            a.EmotionId = (string)fa.Attribute("firstEmotionID") ?? "";
            a.EffectStart = (string)fa.Attribute("firstEffectStart") ?? "";
            a.EffectEnd = (string)fa.Attribute("firstEffectEnd") ?? "";
        }
        if (f.Element("firstAdditionalEffect") is { } ae)
        {
            var e = c.FirstEffect;
            e.Enabled = true;
            e.EffectId = Int(ae, "firstAdditionalEffectID", e.EffectId);
            e.Level = (short)Int(ae, "firstAdditionalEffectLevel", e.Level);
            e.Target = Int(ae, "firstAdditionalTarget", e.Target);
            e.Range = Float(ae, "firstAdditionalRange", e.Range);
            e.Popup = Int(ae, "firstAdditionalPopup", e.Popup);
        }
        if (f.Element("forbidden") is { } fb)
        {
            c.ForbidJump = Int(fb, "forbidJump", c.ForbidJump);
            c.ForbidClimb = Int(fb, "forbidClimb", c.ForbidClimb);
            c.ForbidSkill = Int(fb, "forbidSkill", c.ForbidSkill);
            c.ForbidPlayInstrument = Int(fb, "forbidPlayInstrument", c.ForbidPlayInstrument);
            c.ForbidExceptOwner = Int(fb, "forbidExceptOwner", c.ForbidExceptOwner);
        }
        if (f.Element("pvp") is { } pv)
        {
            c.PvpEnabled = Int(pv, "enable", 0) != 0;
            c.PvpTeam = Int(pv, "team", c.PvpTeam);
        }
        if (f.Element("openWeb") is { } w)
        {
            c.WebOpenType = Int(w, "opentype", c.WebOpenType);
            c.WebTarget = (string)w.Attribute("target") ?? c.WebTarget;
            c.WebUrl = (string)w.Attribute("webURL") ?? c.WebUrl;
        }
        if (f.Element("noticePopup") is { } np) c.NoticeType = (string)np.Attribute("type") ?? c.NoticeType;
        if (f.Element("nurturing") is { } nu)
        {
            var n = c.Nurture = new Nurturing();
            if (nu.Attribute("pluralNpcID") is { } plural)
            {
                var l = Split(plural.Value, ',');
                for (int i = 0; i < l.Length; i++) n.PluralNpcIds[(short)(i + 1)] = l[i];
            }
            n.QuestTag = (string)nu.Attribute("nurturingQuestTag") ?? n.QuestTag;
            if (nu.Attribute("rewardItemByFeeding") is { } bf && Split(bf.Value, ',') is { Length: 3 } f3) n.RewardByFeeding = (f3[0], f3[1], f3[2]);
            if (nu.Attribute("rewardItem") is { } ri && Split(ri.Value, ',') is { Length: 3 } r3) n.Reward = (r3[0], r3[1], r3[2]);
            if (nu.Attribute("requiredGrowth") is { } rg)
            {
                // the unsigned list: atoi per token, widened to the map's u64
                var l = Split(rg.Value, ',');
                for (int i = 0; i < l.Length; i++) n.RequiredGrowth[(short)(i + 1)] = (uint)l[i];
            }
            if (nu.Attribute("rewardItemByGrowth") is { } rb && Split(rb.Value, ',') is { Length: >= 3 } g)
            {
                // :366-376: groups of three from the start, as long as i < count (a short last group reads past the end in
                // the client; the data's lists are whole groups)
                short stage = 1;
                for (int i = 0; i + 2 < g.Length; i += 3) n.RewardByGrowth[stage++] = (g[i], g[i + 1], g[i + 2]);
            }
        }
    }

    static string Str(XElement e, string name) => (string)e.Attribute(name) ?? "";

    /// <summary>XmlGetAttr(int): QueryIntAttribute's sscanf "%d"; no digits keep the default.</summary>
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

    /// <summary>XmlGetAttr(float): QueryFloatAttribute's sscanf "%f"; no number keeps the default.</summary>
    static float Float(XElement e, string name, float def)
    {
        string s = (string)e.Attribute(name);
        if (string.IsNullOrEmpty(s)) return def;
        string t = s.TrimStart();
        if (t.Length == 0 || !(char.IsAsciiDigit(t[0]) || ((t[0] is '+' or '-' or '.') && t.Length > 1))) return def;
        return (float)ArgumentFormat.Atof(s);
    }

    /// <summary>XmlSplit into ints: CString::Tokenize on the delimiter (empty tokens skipped), atoi each token.</summary>
    static int[] Split(string s, char delim) =>
        s.Split(delim, StringSplitOptions.RemoveEmptyEntries).Select(t => ReservedWords.Atoi(t)).ToArray();

    /// <summary>XmlSplit into floats (XmlConvert&lt;float&gt;: atof each token).</summary>
    static float[] Floats(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => (float)ArgumentFormat.Atof(t)).ToArray();
}
