using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>One &lt;nodeCollisions&gt; row of an npc (CNpcData::NodeCollision): the model node, the box's volume and its offset.</summary>
public readonly record struct NpcNodeCollision(string TargetNode, System.Numerics.Vector3 Volume, System.Numerics.Vector3 Offset);

/// <summary>One row of the npc table (Xml pack: npcdata/*.xml, npc/environment): the parts the client's packet readers and the model loader need.</summary>
public sealed class NpcRow
{
    public int Id;
    public int Friendly, Class = 1;
    public string[] PropertyTags = Array.Empty<string>();
    public string Kfm;                       // model name, e.g. 02010001_Stan
    public float Scale = 1f;
    public float ShadowScale;                // <model shadowScale>: radius of the simple ground shadow (0 = none)
    public float WalkSpeed, RunSpeed;
    public float AniSpeed = 1f;              // <model anispeed>: CNpcData m_model.m_fAniSpeed, 1 when absent (Unk_NpcModel ctor)
    public float RotationSpeed;              // <model rotationSpeed>: CNpcData m_fRotationSpeed, the turn cap in degrees per second, 0 when absent
    public string[] IdleActions = Array.Empty<string>();
    public string[] MoveActions = Array.Empty<string>();
    public int Gender;
    public float CapsuleHeight = 150f;
    public float CapsuleRadius = 25f;            // capsule radius (the client's default capsule is 25 x 105)
    public bool CapsuleIgnore;                   // capsule ignore: the npc's body does not block characters
    /// <summary>
    /// The environment's &lt;collision shape width depth height&gt; (CNpcData::LoadXml, npcdata.cpp:1604-1610; 0 when absent):
    /// a "box" shape is the body box the skill area tests (CNpc::BuildCollision).
    /// </summary>
    public string CollisionShape = "";
    public float CollisionWidth, CollisionDepth, CollisionHeight;
    /// <summary>The collision's widthOffset, depthOffset and heightOffset (npcdata.cpp:1611-1613, ints; 0 when absent): CNpc::vfunc_51's
    /// offset (unit_14108e1e0_npcactorandvibrate.cpp:2215).</summary>
    public int CollisionWidthOffset, CollisionDepthOffset, CollisionHeightOffset;
    /// <summary>
    /// The environment's &lt;nodeCollisions&gt;&lt;collision targetNode volume offset&gt; rows (CNpcData::LoadXml, npcdata.cpp:1615-1626,
    /// m_aNodeCollision): CNpc::BuildCollision makes one box per row bound to the model node (m_arrE68, CNpc::vfunc_27's anchors).
    /// </summary>
    public NpcNodeCollision[] NodeCollisions = Array.Empty<NpcNodeCollision>();
    /// <summary>CNpcData::HasPropertyTag(0x1d) "collision_position" (npcdata.cpp:1387): a hit on the npc is placed by its anchors.</summary>
    public bool CollisionPosition => HasTag("collision_position");
    /// <summary>
    /// &lt;corpse hitAble&gt; (CNpcData::LoadXml, npcdata.cpp:1717; read by npcdata.cpp:814): a dead npc's corpse a hostile
    /// attack still hits (FilterTarget through FUN_1410930a0).
    /// </summary>
    public bool CorpseHitAble;
    /// <summary>The environment's "defenceMaterial" (CNpcData m_nDefenceMaterial +0x70, npcdata.cpp:1455, 0 by the ctor): the
    /// hit sound's defence material (CEffectService::PlayHitSound).</summary>
    public int DefenceMaterial;
    /// <summary>
    /// The environment's damagedVibrateDuration and damagedVibrateAmp (CNpcData::LoadXml, npcdata.cpp:1465-1466; 0 when absent):
    /// how long and how far the npc's body shakes when the own PC hits it (CNpc::vfunc_33 @0x141097c40).
    /// </summary>
    public float DamagedVibrateDuration, DamagedVibrateAmp;
    /// <summary>
    /// The environment's damagedColorScale (CNpcData::LoadXml, npcdata.cpp:1464; 2 when absent, the ctor's m_fDamagedColorScale,
    /// :651): the colour boost the npc takes when the own PC hits it (CNpc::vfunc_33's AddColorBoost).
    /// </summary>
    public float DamagedColorScale = 2f;
    /// <summary>
    /// The environment's lookAtMyPCWhenTalking and useTalkMotion (CNpcData::LoadXml, npcdata.cpp:1496-1497, XmlGetAttr's bool;
    /// true when absent, the ctor's, :653): a talking npc turns to the own PC (CNpc::ApplySyncBlock's talk branch,
    /// unit_14108e1e0_npcactorandvibrate.cpp:998-1005) and plays its talk motion. GMS2's npc files keep them on
    /// &lt;lookattarget&gt;; the environment's own attribute wins.
    /// </summary>
    public bool LookAtMyPcWhenTalking = true, UseTalkMotion = true;
    /// <summary>The environment's talkAni (CNpcData m_bTalkAni +0x8a, CNpcData::LoadXml npcdata.cpp:1463, XmlGetAttr's bool;
    /// true when absent, the ctor's :650): StateTalk's enter plays the own PC's TALK_UP only with a talk npc that has it
    /// (statetalk.cpp:107).</summary>
    public bool TalkAni = true;
    /// <summary>The environment's npcAttackGroup and npcDefenseGroup (CNpcData m_nAttackGroup +0x34 / m_nDefenseGroup +0x38,
    /// CNpcData::LoadXml npcdata.cpp:1450-1451, 0 when absent; GetAttackGroup @0x1406ccfe0, GetDefenseGroup @0x1406cd000).
    /// STILL-OPEN: no caller of either getter is in the restored client.</summary>
    public int NpcAttackGroup, NpcDefenseGroup;
    /// <summary>The environment's hitImmune (CNpcData m_nHitImmune +0x74, npcdata.cpp:1456, 0 when absent).
    /// STILL-OPEN: no reader of the field is in the restored client.</summary>
    public int HitImmune;
    /// <summary>The environment's rotationDisabled (CNpcData m_bRotationDisabled +0x88, npcdata.cpp:1460, false when absent).
    /// STILL-OPEN: no reader of the field is in the restored client.</summary>
    public bool RotationDisabled;
    /// <summary>The environment's &lt;distance&gt; (CNpcData m_distance +0x210, npcdata.cpp:1553-1563, each a float, 0 when the
    /// element or the attribute is absent): avoid, sight, sightHeightUP, sightHeightDown and the custom last sight
    /// (customLastSightRadius / HeightUp / HeightDown). STILL-OPEN: no reader of m_distance outside the loader and the row
    /// compare (npcdata.cpp:736) is in the restored client; Unk_Vec7's own default is taken as 0.</summary>
    public float Avoid, Sight, SightHeightUp, SightHeightDown, CustomLastSightRadius, CustomLastSightHeightUp, CustomLastSightHeightDown;
    /// <summary>
    /// &lt;dead defaultaction upaction&gt; (CNpcData::LoadXml, npcdata.cpp:1651-1659): XmlTokenList, the attribute split at ','
    /// by CString::Tokenize (achievedata.cpp:501), which skips empty tokens and trims nothing.
    /// </summary>
    public string[] DeadDefaultActions = Array.Empty<string>(), DeadUpActions = Array.Empty<string>();
    public bool Nametag;                         // environment nametag: the npc shows a name tag
    public int Kind;                             // environment kind: function npcs (shop, storage, ...) are not 0
    public bool Minimap = true;                  // environment minimap: shown on the minimap
    public string MinimapIcon;                   // environment minimapIconName: the minimap icon class suffix, when set
    public string Icon = "";                     // environment iconName (NpcDataCli m_strIcon, npcdatacli.cpp:169)
    public string ShopIcon = "";                 // environment shopIconName (NpcDataCli m_strShopIcon, npcdatacli.cpp:171)
    public string Illust;                        // environment illust: the talk portrait (Resource/Image quest/illust/<illust>.dds)
    public string Portrait;                      // environment portrait (CNpcData m_strPortrait, npcdata.cpp:1467): the dictionary's picture
    public string Title;                         // npctitle.xml, shown above the name
    public string SoundStart, SoundEnd, SoundDead;   // npcSoundStart/End/Dead: sound event paths like ko/Npc/01000022
    /// <summary>The environment's deadEffect and damageEffect (NpcDataCli::LoadXml, npcdatacli.cpp:176-177; null when absent):
    /// the effect xml CNpc::FUN_141098fb0 plays on the npc when it dies with its sound (0) or takes a hit below 0 (1).</summary>
    public string DeadEffect, DamageEffect;
    /// <summary>The environment's regenEffect (NpcDataCli m_strRegenEffect +0x550, npcdatacli.cpp:175; null when absent): the effect
    /// xml StateSpawn plays at the npc's place when the spawn state shows it (statespawn.cpp:114-127).</summary>
    public string RegenEffect;
    public string Name;                      // from string/en/npcname.xml (or kr), null when absent
    public bool HpBar;                           // environment hpBar (NpcDataCli, default false): the npc has a head hp bar
    public int Level = 1;                        // environment level (CNpcData m_nLevel, 1 when absent)
    public long Hp;                              // environment stat hp (CNpcData m_stat.m_aBase, NpcData::Load FirstChild("stat"); 0 when absent)
    public int Race;                             // environment raceString as CNpcData reads it (ParseRace), 0 when absent or unknown
    // environment <interact> (CNpcData::LoadXml m_interact, npcdata.cpp:1595-1602; defaults npcdata.cpp:593)
    public string InteractFunction = "";         // interactFunction: InteractNpcAction offers the npc only when set
    public string InteractCastingAnimation = ""; // interactCastingAnimation: StateInteractionNpc's player animation
    public int InteractCastingTime;              // interactCastingTime (ms): the casting bar duration
    public int InteractCoolTime;                 // interactCoolTime
    public bool InteractShowCastingBar = true;   // interactIsShowCastingBar: the casting gauge is drawn
    /// <summary>The npc's effect dummies: the default rows with the npc's &lt;effectdummy&gt; merged in (EffectDummyTable.MergeNpc).</summary>
    public IReadOnlyList<EffectDummy> EffectDummies = Array.Empty<EffectDummy>();

    /// <summary>
    /// Unk_NpcDead::GetRandomDefaultAction (@0x1406c95b0, npcdata.cpp:442): "Dead_A" when the list is empty, else
    /// list[rand() % count]; <paramref name="rand"/> is drawn only for a non-empty list.
    /// </summary>
    public string RandomDeadDefaultAction(Func<int> rand) =>
        DeadDefaultActions.Length == 0 ? "Dead_A" : DeadDefaultActions[rand() % DeadDefaultActions.Length];

    /// <summary>
    /// The sequence StateDeadNPC::vfunc_4 (statedead.cpp:224) plays: the boss flag of StateDeadNPCData, which CNpc::vfunc_125
    /// (@0x141091210) sets for a boss that dies without <paramref name="show"/>, plays "Dead_Idle_A"; otherwise
    /// CNpc::vfunc_123(0x34) (:760), the action <paramref name="picked"/> at spawn (m_strED0, :539).
    /// STILL-OPEN: MakeDeadPhysXActor (:1693-1788) repicks m_strED0 (the up action when pushed up) only for a hit that pushes
    /// the body; the push is not ported, so the spawn pick stays.
    /// </summary>
    public string DeadSequence(bool show, string picked) => !show && IsBoss ? "Dead_Idle_A" : picked;

    /// <summary>CNpcData::IsBoss: friendly 0 and class 3 or more.</summary>
    public bool IsBoss => Friendly == 0 && Class >= 3;
    /// <summary>Property tag 5.</summary>
    public bool HasDamagedHpBarTag => HasTag("damaged_hp_upper_hp_bar");
    /// <summary>Property tag 1: the head hp bar uses rank 10.</summary>
    public bool BigHpBar => HasTag("big_hp_bar");
    /// <summary>Property tag 2: the head hp bar is shown from spawn.</summary>
    public bool AlwaysDisplayHpBar => HasTag("always_display_hp_bar");
    /// <summary>A property tag, matched as the client's tag parser does (CompareNoCase).</summary>
    public bool HasTag(string tag) => PropertyTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
    /// <summary>Property tag 3: the target frame shows "?" for the level.</summary>
    public bool HideHpBarLevel => HasTag("hide_hpbar_level");
    /// <summary>CNpc::vfunc_79, the level on the target frame: 0 (shown as "?") with <see cref="HideHpBarLevel"/>, else the level.</summary>
    public int HpBarLevel => HideHpBarLevel ? 0 : Level;
    /// <summary>
    /// NpcDataCli::GetClassConstant, the name tag colour of the row: NameTagColorFriendly for friendly rows, else by class:
    /// 1 NameTagColorMob, 3 and 7 NameTagColorNamed, 4 and 6 NameTagColorBoss; null for any other class (the colour 0).
    /// </summary>
    public string ClassColorKey => Friendly != 0 ? "NameTagColorFriendly" : Class switch
    {
        1 => "NameTagColorMob",
        3 or 7 => "NameTagColorNamed",
        4 or 6 => "NameTagColorBoss",
        _ => null,
    };

    /// <summary>
    /// CNpc's per-frame name tag colour: an npc hostile to the local player (CNpc::vfunc_122) shows NameTagColorDead once
    /// dead, else <see cref="ClassColorKey"/>; any other npc shows NameTagColorFriendly.
    /// </summary>
    public string TagColorKey(bool hostile, bool dead) => !hostile ? "NameTagColorFriendly" : dead ? "NameTagColorDead" : ClassColorKey;

    /// <summary>The npc's effect dummy of this name (exact case), or null.</summary>
    public EffectDummy Dummy(string name) => EffectDummyTable.Find(EffectDummies, name);
}

/// <summary>All npc rows of the player's data, read lazily from the Xml pack.</summary>
public sealed class NpcTable
{
    readonly Dictionary<int, NpcRow> rows = new();
    public int Count => rows.Count;

    /// <summary>The npc rows, their names and titles read in <paramref name="language"/> (a string folder such as "kr",
    /// the client's g_strLocaleDir2) first, then en and kr as <see cref="GameStrings"/> does.</summary>
    public NpcTable(PackSet packs, string language = null) : this(packs, EffectDummyTable.Load(packs), language) { }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<NpcTable>>> shared = new();

    /// <summary>
    /// The one table of a data set and name language: the client reads its npc table once (the CNpcDataTable singleton,
    /// npcdata.h:385-392, LoadNpcCache @0x1406ce4a0, NeedsLoad @0x1406cedb0), not on each field enter, so the read of every
    /// npc file (GMS2's per-file set above all) is paid once per PackSet and language. Nothing changes a table after its build,
    /// so the users (the field's npcs, the replay, the quest guide, the field contents) read the same one. A null language is
    /// en, as in the constructor; the table belongs to the PackSet object (compared by reference), so another Data folder
    /// has its own.
    /// </summary>
    public static NpcTable For(PackSet packs, string language = null) =>
        shared.GetValue(packs, _ => new(StringComparer.Ordinal))
              .GetOrAdd(language ?? "en", lang => new Lazy<NpcTable>(() => new NpcTable(packs, lang))).Value;

    public NpcTable(PackSet packs, EffectDummyTable effectDummies, string language = null)
    {
        var dummies = effectDummies?.Default ?? Array.Empty<EffectDummy>();
        var files = packs.List("Xml").Where(n => n.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        if (PerFileTable.Choose(files.Count > 0, () => null, () => PerFileTable.Rows(packs, "npc/", "npc")) is { } perFile)      // GMS2: npc/<aa>/<bb>/<id>.xml
            foreach (var n in perFile.Root.Elements("npc"))
                if (ParseRow(n, dummies) is { } row) rows[row.Id] = row;
        foreach (var entry in files)
        {
            XDocument x;
            try { x = PackXml.Parse(packs.Read("Xml", entry)); } catch { continue; }
            foreach (var n in x.Descendants("npc"))
                if (ParseRow(n, dummies) is { } row) rows[row.Id] = row;
        }
        LoadNames(packs, new GameStrings(packs, language ?? "en").Languages);
    }

    /// <summary>
    /// One &lt;npc id&gt; element of an npcdata file: its &lt;environment&gt; read into a row, with the default effect dummies
    /// merged with the npc's own; null without an id or an environment.
    /// </summary>
    public static NpcRow ParseRow(XElement n, IReadOnlyList<EffectDummy> defaultDummies)
    {
        if (!int.TryParse((string)n.Attribute("id"), out int id)) return null;
        var env = n.Element("environment");
        if (env == null) return null;
        // the recorded GMS2 data keeps the npc's basic attributes (friendly, class, hpBar, defenceMaterial, level, portrait ...) in a child
        // <basic>; the npcdata rows carry them on <environment>. The environment's own attributes win.
        if (env.Element("basic") is { } basic)
            foreach (var at in basic.Attributes().ToList()) if (env.Attribute(at.Name) == null) env.SetAttributeValue(at.Name, at.Value);
        var model = env.Element("model");
        var speed = env.Element("speed");
        var shadow = env.Element("shadow");
        var row = new NpcRow
        {
            Id = id,
            Friendly = Int(env, "friendly"),
            Class = (string)env.Attribute("class") is { } cls && int.TryParse(cls, out var c) ? c : 1,     // CNpcData m_nClass, 1 when absent
            Gender = Int(env, "gender"),
            PropertyTags = ((string)env.Attribute("propertyTags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SoundStart = (string)env.Attribute("npcSoundStart"), SoundEnd = (string)env.Attribute("npcSoundEnd"), SoundDead = (string)env.Attribute("npcSoundDead"),
            DeadEffect = (string)env.Attribute("deadEffect"), DamageEffect = (string)env.Attribute("damageEffect"),
            RegenEffect = (string)env.Attribute("regenEffect"),
            Kfm = (string)model?.Attribute("kfm"),
            Scale = Flt(model, "scale", 1f),
            // CNpcData::LoadXml reads these four off <model> (npcdata.cpp:1524-1527, 0 when absent). GMS2's npc files keep them in
            // <shadow scale> and <speed rotation walk run> (no GMS2 file has them on <model>, no KMS row has those children; over the
            // 10,641 npcs both sets hold, rotation agrees on all 10,266 the KMS rows set, walk, run and shadow on all but 2 or 3):
            // those are read when <model> has none.
            // Without them every GMS2 npc has rotationSpeed 0 and never turns toward a sync block's yaw (NpcLayer.Turn).
            // STILL-OPEN: the GMS2 client's npc reader is not in the restored source; the mapping is read from the data.
            ShadowScale = Flt(model, "shadowScale", Flt(shadow, "scale", 0f)),
            WalkSpeed = Flt(model, "walkSpeed", Flt(speed, "walk", 0)),
            RunSpeed = Flt(model, "runSpeed", Flt(speed, "run", 0)),
            AniSpeed = Flt(model, "anispeed", 1f),
            RotationSpeed = Flt(model, "rotationSpeed", Flt(speed, "rotation", 0)),
            CapsuleHeight = Flt(env.Element("capsule"), "height", 150f),
            CapsuleRadius = Flt(env.Element("capsule"), "radius", 25f),
            CapsuleIgnore = Int(env.Element("capsule"), "ignore") != 0,
            CollisionShape = (string)env.Element("collision")?.Attribute("shape") ?? "",
            CollisionWidth = Flt(env.Element("collision"), "width", 0),
            CollisionDepth = Flt(env.Element("collision"), "depth", 0),
            CollisionHeight = Flt(env.Element("collision"), "height", 0),
            CollisionWidthOffset = Int(env.Element("collision"), "widthOffset"),
            CollisionDepthOffset = Int(env.Element("collision"), "depthOffset"),
            CollisionHeightOffset = Int(env.Element("collision"), "heightOffset"),
            NodeCollisions = env.Element("nodeCollisions")?.Elements("collision")
                .Select(c => new NpcNodeCollision((string)c.Attribute("targetNode") ?? "", Vec(c, "volume"), Vec(c, "offset"))).ToArray()
                ?? Array.Empty<NpcNodeCollision>(),
            CorpseHitAble = Int(env.Element("corpse"), "hitAble") != 0,
            DefenceMaterial = Int(env, "defenceMaterial"),
            Nametag = Int(env, "nametag") != 0,
            Kind = Int(env, "kind"),
            Illust = (string)env.Attribute("illust"),
            Portrait = (string)env.Attribute("portrait"),
            Minimap = (string)env.Attribute("minimap") is not ("0" or "false" or "False"),      // CNpcData: true unless set off
            MinimapIcon = (string)env.Attribute("minimapIconName"),
            Icon = (string)env.Attribute("iconName") ?? "",
            ShopIcon = (string)env.Attribute("shopIconName") ?? "",
            HpBar = EffectDummy.Bool(env, "hpBar", false),
            Level = (string)env.Attribute("level") is { } lv && ushort.TryParse(lv, out var l) ? l : 1,
            Hp = long.TryParse((string)env.Element("stat")?.Attribute("hp"), out var hp) ? hp : 0,
            Race = ParseRace((string)env.Attribute("raceString")),
            EffectDummies = EffectDummyTable.MergeNpc(defaultDummies, env),
        };
        if (env.Element("interact") is { } ia)
        {
            row.InteractFunction = (string)ia.Attribute("interactFunction") ?? "";
            row.InteractCastingAnimation = (string)ia.Attribute("interactCastingAnimation") ?? "";
            row.InteractCastingTime = Int(ia, "interactCastingTime");
            row.InteractCoolTime = Int(ia, "interactCoolTime");
            row.InteractShowCastingBar = EffectDummy.Bool(ia, "interactIsShowCastingBar", true);
        }
        row.TalkAni = EffectDummy.Bool(env, "talkAni", true);
        row.NpcAttackGroup = Int(env, "npcAttackGroup");
        row.NpcDefenseGroup = Int(env, "npcDefenseGroup");
        row.HitImmune = Int(env, "hitImmune");
        row.RotationDisabled = EffectDummy.Bool(env, "rotationDisabled", false);
        if (env.Element("distance") is { } distance)                                   // CNpcData::LoadXml FirstChild("distance")
        {
            row.Avoid = Flt(distance, "avoid", 0);
            row.Sight = Flt(distance, "sight", 0);
            row.SightHeightUp = Flt(distance, "sightHeightUP", 0);
            row.SightHeightDown = Flt(distance, "sightHeightDown", 0);
            row.CustomLastSightRadius = Flt(distance, "customLastSightRadius", 0);
            row.CustomLastSightHeightUp = Flt(distance, "customLastSightHeightUp", 0);
            row.CustomLastSightHeightDown = Flt(distance, "customLastSightHeightDown", 0);
        }
        row.DamagedVibrateDuration = Flt(env, "damagedVibrateDuration", 0);
        row.DamagedVibrateAmp = Flt(env, "damagedVibrateAmp", 0);
        row.DamagedColorScale = Flt(env, "damagedColorScale", 2f);
        var look = env.Element("lookattarget");
        row.LookAtMyPcWhenTalking = EffectDummy.Bool(env, "lookAtMyPCWhenTalking", look == null || EffectDummy.Bool(look, "lookAtMyPCWhenTalking", true));
        row.UseTalkMotion = EffectDummy.Bool(env, "useTalkMotion", look == null || EffectDummy.Bool(look, "useTalkMotion", true));
        if (env.Element("dead") is { } dead)
        {
            row.DeadDefaultActions = ((string)dead.Attribute("defaultaction") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            row.DeadUpActions = ((string)dead.Attribute("upaction") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        var normal = env.Element("normal");
        row.IdleActions = ((string)normal?.Attribute("action") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return row;
    }

    /// <summary>The race names by <see cref="NpcRow.Race"/>, also the ids of the race name string table (racename.xml).</summary>
    public static readonly string[] RaceKeys = { null, "unknown", "plant", "animal", "bug", "mystic", "spirit", "fairy", "combine", "undead", "devil", "machine", "creature" };

    /// <summary>The npc data's raceString (FUN_14021c7c0): one of <see cref="RaceKeys"/> in any case, else 0.</summary>
    public static int ParseRace(string value)
    {
        if (value == null) return 0;
        for (int i = 1; i < RaceKeys.Length; i++) if (value.Equals(RaceKeys[i], StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>
    /// The target frame's type text (UIUpperHpBar, string table 17 "raceName" by the npc's race): the race's name in
    /// racename.xml, or null for race 0 or a race the table lacks.
    /// </summary>
    public static string RaceName(IReadOnlyDictionary<string, string> raceNames, int race) =>
        race > 0 && race < RaceKeys.Length && raceNames != null && raceNames.TryGetValue(RaceKeys[race], out var n) ? n : null;

    /// <summary>Fills the row names from the string table.</summary>
    void LoadNames(PackSet packs, string[] languages)
    {
        foreach (var lang in languages)
        {
            string entry = $"string/{lang}/npcname.xml";
            if (!packs.Exists("Xml", entry)) continue;
            var x = PackXml.Parse(packs.Read("Xml", entry));
            foreach (var k in x.Descendants("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id) && rows.TryGetValue(id, out var row) && row.Name == null) row.Name = (string)k.Attribute("name");
            entry = $"string/{lang}/npctitle.xml";
            if (!packs.Exists("Xml", entry)) continue;
            x = PackXml.Parse(packs.Read("Xml", entry));
            foreach (var k in x.Descendants("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id) && rows.TryGetValue(id, out var row) && row.Title == null) row.Title = (string)k.Attribute("name");
        }
    }

    /// <summary>XmlGetAttr of an efd::Point3: "x,y,z", zero when absent (the NodeCollision ctor's zero).</summary>
    static System.Numerics.Vector3 Vec(XElement e, string a)
    {
        var p = ((string)e?.Attribute(a) ?? "").Split(',', StringSplitOptions.TrimEntries);
        float F(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        return new System.Numerics.Vector3(F(0), F(1), F(2));
    }
    static int Int(XElement e, string a) => int.TryParse((string)e?.Attribute(a), out int v) ? v : 0;
    static float Flt(XElement e, string a, float def) => float.TryParse((string)e?.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

    public NpcRow Find(int id) => rows.GetValueOrDefault(id);

    /// <summary>Every row, in no set order.</summary>
    public IEnumerable<NpcRow> Rows => rows.Values;
}
