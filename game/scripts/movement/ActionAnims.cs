using System.Globalization;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2Godot.Movement;

/// <summary>
/// The client's action ids (ActionType, actioncontainer.cpp) and their sequence names from table/pcactiondata.xml
/// (group 0, the default set). PCBase::vfunc_123(id) resolves an action to its sequence the same way.
/// Animations are added to the AnimationPlayer under the lower-case sequence name.
/// </summary>
public sealed class ActionAnims
{
    public enum Act
    {
        Idle = 0, BattleIdle = 1, JumpDown = 2, JumpLand = 3, JumpUp = 4, JumpFall = 5, JumpDashDown = 6, JumpDashLand = 7,
        LadderLDown = 8, LadderRDown = 9, LadderDownLand = 10, LadderDownTake = 11, LadderIdle = 12,
        LadderLUp = 13, LadderRUp = 14, LadderUpLand = 15, LadderUpTake = 16,
        ClimbIdle = 17, ClimbLUp = 18, ClimbLCenter = 19, ClimbLDown = 20, ClimbRUp = 21, ClimbRCenter = 22,
        ClimbRDown = 23, ClimbUpLand = 24, ClimbUpTake = 25, ClimbDownLand = 26, ClimbDownTake = 27,
        Run = 28, Walk = 29, PoleUp = 34, PoleDown = 35, PoleIdle = 36, PoleTurn = 37, PoleTake = 38, SpecialJumpUp = 42, SpecialJumpDown = 43,
        TalkUp = 45, TalkIdle = 46, ListenIdle = 47,
        WaterIdle = 48, WaterSwim = 49, WaterDash = 50, Revival = 51, Dead = 52, Hit = 53,
        UgcCraftIdle = 54, CraftIdle = 55, CraftRun = 56, CraftWalk = 57, CraftJumpUp = 58, CraftJumpDown = 59, CraftJumpLand = 60,
        HoldIdle = 62, FallDamage = 63, ModePvpDead = 64,
    }

    static readonly string[] TypeNames =
    {
        "IDLE", "BATTLE_IDLE", "JUMP_DOWN", "JUMP_LAND", "JUMP_UP", "JUMP_FALL", "JUMP_DASH_DOWN", "JUMP_DASH_LAND",
        "LADDER_L_DOWN", "LADDER_R_DOWN", "LADDER_DOWN_LAND", "LADDER_DOWN_TAKE", "LADDER_IDLE", "LADDER_L_UP",
        "LADDER_R_UP", "LADDER_UP_LAND", "LADDER_UP_TAKE", "CLIMB_IDLE", "CLIMB_L_UP", "CLIMB_L_CENTER", "CLIMB_L_DOWN",
        "CLIMB_R_UP", "CLIMB_R_CENTER", "CLIMB_R_DOWN", "CLIMB_UP_LAND", "CLIMB_UP_TAKE", "CLIMB_DOWN_LAND",
        "CLIMB_DOWN_TAKE", "RUN", "WALK", "STUNT_IDLE", "STUNT_WALK", "CRAWL_IDLE", "CRAWL_CRAWLING", "POLE_UP",
        "POLE_DOWN", "POLE_IDLE", "POLE_TURN", "POLE_TAKE", "BORE", "STUN", "KNOCK_BACK", "SPECIAL_JUMP_UP",
        "SPECIAL_JUMP_DOWN", "REGEN", "TALK_UP", "TALK_IDLE", "LISTEN_IDLE", "WATER_IDLE", "WATER_SWIM", "WATER_DASH",
        "REVIVAL", "DEAD", "HIT", "UGC_CRAFT_IDLE", "CRAFT_IDLE", "CRAFT_RUN", "CRAFT_WALK", "CRAFT_JUMP_UP",
        "CRAFT_JUMP_DOWN", "CRAFT_JUMP_LAND", "FITTING_IDLE", "HOLD_IDLE", "FALL_DAMAGE", "MODEPVP_DEAD",
        // 65-74 (actioncontainer.cpp:547-566): no Act of their own, so the body does not load them; a mount plays the stunt jump
        "MODEPVP_WIN", "HAPPY", "SKILL", "AUTO_REVIVE", "NUTRIENT", "STUNT_JUMP_UP", "STUNT_JUMP_DOWN", "STUNT_JUMP_LAND",
        "CHARACTER_SELECT_BORE", "CHARACTER_SELECT_BORE_IDLE",
    };

    // sequences that cycle; the rest play once and hold their last frame
    static readonly HashSet<Act> Looping = new() { Act.Idle, Act.BattleIdle, Act.Run, Act.Walk, Act.JumpFall, Act.WaterIdle, Act.WaterSwim, Act.WaterDash, Act.LadderIdle, Act.ClimbIdle, Act.PoleIdle, Act.HoldIdle,
        Act.UgcCraftIdle, Act.CraftIdle, Act.CraftRun, Act.CraftWalk, Act.TalkIdle, Act.ListenIdle };

    /// <summary>The pcactiondata type of action <paramref name="act"/> (IDLE, WALK, ...), null past the table.</summary>
    public static string TypeName(int act) => act >= 0 && act < TypeNames.Length ? TypeNames[act] : null;

    /// <summary>Whether action <paramref name="act"/>'s sequence cycles.</summary>
    public static bool Loops(int act) => Looping.Contains((Act)act);

    readonly Dictionary<Act, string> names = new();
    readonly HashSet<string> missing = new(StringComparer.Ordinal);
    public readonly Dictionary<string, KfAnimation> Kf = new();

    public string Name(Act a) => Name(a, null);

    /// <summary>
    /// CAnimationManager::PlayAnimation (@0x14099e1a0, animationmanager.cpp:248-265): while weapons are held in battle
    /// stance, PC::vfunc_60 (@0x141658a30, pc.cpp:1737-1749) supplies the weapon action's prefix (e.g. "Sword_",
    /// "LargeSword_") and the animation manager tries prefix + base before falling back to the unprefixed sequence.
    /// </summary>
    public string Name(Act a, string prefix)
    {
        // the resolved key per (action, prefix), made once: a loaded sequence stays loaded and a sequence the body lacks
        // stays missing (both are remembered in `missing`), so a repeat call costs no allocation (the prefixed key, its
        // lower-casing and HasAnimation's StringName were made on every call, 0.2-0.4 MB/s)
        prefix ??= "";
        if (!resolved.TryGetValue(prefix, out var byAct)) resolved[prefix] = byAct = new string[ActCount];
        int slot = (int)a;
        var hit = byAct[slot];
        if (hit == null) byAct[slot] = hit = Resolve(a, prefix) ?? NoSequence;
        return ReferenceEquals(hit, NoSequence) ? null : hit;
    }

    static readonly string NoSequence = new string('\0', 1);

    static readonly int ActCount = Enum.GetValues<Act>().Max(x => (int)x) + 1;
    readonly Dictionary<string, string[]> resolved = new(StringComparer.Ordinal);

    string Resolve(Act a, string prefix)
    {
        if (!names.TryGetValue(a, out var baseKey)) return null;
        bool loop = Looping.Contains(a);
        if (!string.IsNullOrEmpty(prefix))
        {
            string prefKey = prefix.ToLowerInvariant() + baseKey;
            if (!missing.Contains(prefKey))
            {
                if (Sequence(prefKey, loop) is { } loaded) return loaded;
                missing.Add(prefKey);
            }
        }
        if (player != null && !player.HasAnimation(baseKey))
        {
            if (missing.Contains(baseKey)) return null;
            if (Sequence(baseKey, loop) == null) { missing.Add(baseKey); return null; }
        }
        return baseKey;
    }

    public bool Has(Act a) => names.ContainsKey(a);

    readonly Dictionary<Act, string[]> lists = new();

    /// <summary>
    /// PCBase::FUN_141679170(type, index) (@0x141679170, pcbase.cpp:3508) -> PCActionGroupCustom::Name (actioncontainer.cpp:335):
    /// the action's <paramref name="index"/>-th sequence name, loaded as a one-shot; PCActionList::Name gives "" past the list's
    /// end (actioncontainer.cpp:163-168), and so does a name the body lacks: null.
    /// </summary>
    public string NameAt(Act a, int index)
    {
        if (!lists.TryGetValue(a, out var list) || index < 0 || index >= list.Length || list[index].Length == 0) return null;
        return Sequence(list[index].ToLowerInvariant(), false);
    }

    // the body's actor file (female/female.kfm, male/male.kfm: the KFM whose model is the f_body / m_body we load) and its
    // sequence ids by the lower-case kf name, which is the AnimationPlayer key
    KfmFile kfm;
    readonly Dictionary<string, int> seqIds = new(StringComparer.OrdinalIgnoreCase);
    // companion <bodyDir>/<bodyDir>.xml text keys (NiTextKeyExtraData::LoadBinary, NiTextKeyExtraData.cpp:59-75;
    // FUN_1414a90c0 @0x1414a90c0, ms2animationservice.cpp:349-422; installed at ms2memfile.cpp:333)
    readonly Dictionary<string, List<(double time, string text)>> xmlKeys = new(StringComparer.OrdinalIgnoreCase);

    void AppendXmlKeys(string key, KfAnimation kf)
    {
        if (xmlKeys.TryGetValue(key, out var extra)) kf.TextKeys.AddRange(extra);
    }

    /// <summary>
    /// A sequence played outside the action table (a cast's &lt;sequence&gt;.kf, added to the library as
    /// <paramref name="name"/>): its text keys with the companion xml's keys of <paramref name="seq"/>, in time order as
    /// NiControllerSequence throws them, so <see cref="TextKey"/> and the body's text key pass see them.
    /// </summary>
    public void AddSequenceKeys(string name, string seq, KfAnimation kf)
    {
        AppendXmlKeys(seq, kf);
        var sorted = kf.TextKeys.OrderBy(k => k.time).ToList();   // stable: keys of one time keep their order
        kf.TextKeys.Clear();
        kf.TextKeys.AddRange(sorted);
        Kf[name] = kf;
    }

    /// <summary>
    /// The KFM transition NiActorManager takes from one sequence to the next (NiKFMTool::GetTransition(cur, desired),
    /// NiActorManager.cpp:624), or null when either sequence is not in the body's KFM.
    /// </summary>
    public KfmFile.Transition Transition(string from, string to)
    {
        if (kfm == null || from == null || to == null || !seqIds.TryGetValue(from, out int a) || !seqIds.TryGetValue(to, out int b)) return null;
        return kfm.GetTransition(a, b);
    }

    /// <summary>The KFM's DEFAULT_NONSYNC transition (the one a row without its own transition uses).</summary>
    public KfmFile.Transition DefaultTransition => kfm?.DefaultNonSync;

    /// <summary>Reads the action table and loads each movement sequence the body has (female/&lt;name&gt;.kf).</summary>
    public static ActionAnims Load(PackSet packs, ModelBuilder builder, ModelData md, AnimationPlayer player, string bodyDir)
    {
        var aa = new ActionAnims { builder = builder, md = md, player = player, bodyDir = bodyDir };
        string kfmEntry = $"{bodyDir}/{bodyDir}.kfm";
        if (packs.Exists(ModelBuilder.ModelArchive, kfmEntry))
        {
            aa.kfm = KfmFile.Load(packs.Read(ModelBuilder.ModelArchive, kfmEntry));
            foreach (var sq in aa.kfm.Sequences)
                aa.seqIds.TryAdd(Path.GetFileNameWithoutExtension(sq.KfPath.Replace('\\', '/')), sq.Id);
        }
        string xmlEntry = $"{bodyDir}/{bodyDir}.xml";
        if (packs.Exists(ModelBuilder.ModelArchive, xmlEntry))
        {
            var kx = PackXml.Parse(packs.Read(ModelBuilder.ModelArchive, xmlEntry));
            foreach (var el in kx.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (aa.xmlKeys.ContainsKey(el.Name.LocalName)) continue;   // FirstChild (ms2animationservice.cpp:395)
                List<(double, string)> list = null;
                foreach (var k in el.Elements("key"))
                {
                    string text = (string)k.Attribute("text");
                    if (string.IsNullOrEmpty(text)) continue;
                    double.TryParse((string)k.Attribute("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t);
                    (list ??= new()).Add((t, text));
                }
                if (list != null) aa.xmlKeys[el.Name.LocalName] = list;
            }
        }
        var table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var x = PackXml.Parse(packs.Read("Xml", "table/pcactiondata.xml"));
        var group = x.Root?.Elements("group").FirstOrDefault(g => (string)g.Attribute("groupID") == "0");
        foreach (var v in group?.Elements("v") ?? Enumerable.Empty<XElement>())
            table[(string)v.Attribute("type")] = Array.ConvertAll(((string)v.Attribute("names") ?? "").Split(','), n => n.Trim());

        var lib = player.GetAnimationLibrary("");
        foreach (Act a in Enum.GetValues<Act>())
        {
            int id = (int)a;
            if (id >= TypeNames.Length || !table.TryGetValue(TypeNames[id], out var list) || list[0].Length == 0) continue;
            aa.lists[a] = list;
            string key = list[0].ToLowerInvariant();
            // loaded on demand (battle stance, NameAt, the cube mode's carry and build motions)
            if (a is Act.BattleIdle or Act.ModePvpDead or (>= Act.UgcCraftIdle and <= Act.CraftJumpLand)) { aa.names[a] = key; continue; }
            if (!lib.HasAnimation(key))
            {
                KfAnimation kf;
                try { kf = builder.LoadKf($"{bodyDir}/{key}.kf"); }
                catch (Exception) { continue; }        // the body has no such sequence
                aa.AppendXmlKeys(key, kf);
                var anim = ModelBuilder.BuildAnimation(kf, md);
                if (!Looping.Contains(a)) anim.LoopMode = Animation.LoopModeEnum.None;
                lib.AddAnimation(key, anim);
                aa.Kf[key] = kf;
            }
            aa.names[a] = key;
        }
        return aa;
    }

    ModelBuilder builder;
    ModelData md;
    AnimationPlayer player;
    string bodyDir;
    /// <summary>The body folder the sequences come from ("male" or "female").</summary>
    public string BodyDir => bodyDir;

    /// <summary>A sequence by its name (the body's &lt;name&gt;.kf), loaded on first use; null when the body has none.</summary>
    public string Sequence(string seq, bool loop)
    {
        if (string.IsNullOrEmpty(seq) || player == null) return null;
        string key = seq.ToLowerInvariant();
        var lib = player.GetAnimationLibrary("");
        if (lib.HasAnimation(key)) return key;
        KfAnimation kf;
        try { kf = builder.LoadKf(BodySequences.Entry(bodyDir, key)); }
        catch (Exception) { return null; }
        AppendXmlKeys(key, kf);
        var anim = ModelBuilder.BuildAnimation(kf, md);
        anim.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        lib.AddAnimation(key, anim);
        Kf[key] = kf;
        return key;
    }

    /// <summary>Time of a text key ("begin", "end", ...) in a sequence, or -1.</summary>
    public float TextKey(string anim, string text)
    {
        if (anim == null || !Kf.TryGetValue(anim, out var kf)) return -1;
        foreach (var (t, s) in kf.TextKeys)
            if (string.Equals(s, text, StringComparison.OrdinalIgnoreCase)) return (float)t;
        return -1;
    }

    /// <summary>
    /// PC::vfunc_115: the walk / run animation rate for a speed a, run speed b and walk speed c; isRun is the
    /// RUN (1) or WALK (0) sequence choice.
    /// </summary>
    public static float GaitRate(float a, float b, float c, out bool isRun)
    {
        isRun = true;
        if (a <= c / 3f) { isRun = false; return 0.2f; }
        if (a < c) { isRun = false; return 1f - (c - a) / (c * 0.85f); }
        if (a == c) { isRun = false; return 1f; }
        if (a < (c + b) / 2.5f) { isRun = false; return 1f - (c - a) / (c * 1.35f); }
        if (a < b) return 1f - (b - a) / (b * 1.65f);
        if (a == b) return 1f;
        if (a < 2f * b) return 1f - (b - a) / (b * 1.25f);
        return 1.8f;
    }
}
