using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

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
    public string Name;                      // from string/en/npcname.xml (or kr), null when absent
    public bool HpBar;                           // environment hpBar (NpcDataCli, default false): the npc has a head hp bar
    public int Level = 1;                        // environment level (CNpcData m_nLevel, 1 when absent)
    public long Hp;                              // environment stat hp (CNpcData m_stat.m_aBase, NpcData::Load FirstChild("stat"); 0 when absent)
    public int Race;                             // environment raceString as CNpcData reads it (ParseRace), 0 when absent or unknown
    /// <summary>The npc's effect dummies: the default rows with the npc's &lt;effectdummy&gt; merged in (EffectDummyTable.MergeNpc).</summary>
    public IReadOnlyList<EffectDummy> EffectDummies = Array.Empty<EffectDummy>();

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

    public NpcTable(PackSet packs, EffectDummyTable effectDummies, string language = null)
    {
        var dummies = effectDummies?.Default ?? Array.Empty<EffectDummy>();
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument x;
            try { x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')); } catch { continue; }
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
        var model = env.Element("model");
        var row = new NpcRow
        {
            Id = id,
            Friendly = Int(env, "friendly"),
            Class = (string)env.Attribute("class") is { } cls && int.TryParse(cls, out var c) ? c : 1,     // CNpcData m_nClass, 1 when absent
            Gender = Int(env, "gender"),
            PropertyTags = ((string)env.Attribute("propertyTags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SoundStart = (string)env.Attribute("npcSoundStart"), SoundEnd = (string)env.Attribute("npcSoundEnd"), SoundDead = (string)env.Attribute("npcSoundDead"),
            Kfm = (string)model?.Attribute("kfm"),
            Scale = Flt(model, "scale", 1f),
            ShadowScale = Flt(model, "shadowScale", 0f),
            WalkSpeed = Flt(model, "walkSpeed", 0),
            RunSpeed = Flt(model, "runSpeed", 0),
            CapsuleHeight = Flt(env.Element("capsule"), "height", 150f),
            CapsuleRadius = Flt(env.Element("capsule"), "radius", 25f),
            CapsuleIgnore = Int(env.Element("capsule"), "ignore") != 0,
            CollisionShape = (string)env.Element("collision")?.Attribute("shape") ?? "",
            CollisionWidth = Flt(env.Element("collision"), "width", 0),
            CollisionDepth = Flt(env.Element("collision"), "depth", 0),
            CollisionHeight = Flt(env.Element("collision"), "height", 0),
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
            var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF'));
            foreach (var k in x.Descendants("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id) && rows.TryGetValue(id, out var row) && row.Name == null) row.Name = (string)k.Attribute("name");
            entry = $"string/{lang}/npctitle.xml";
            if (!packs.Exists("Xml", entry)) continue;
            x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF'));
            foreach (var k in x.Descendants("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id) && rows.TryGetValue(id, out var row) && row.Title == null) row.Title = (string)k.Attribute("name");
        }
    }

    static int Int(XElement e, string a) => int.TryParse((string)e?.Attribute(a), out int v) ? v : 0;
    static float Flt(XElement e, string a, float def) => float.TryParse((string)e?.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

    public NpcRow Find(int id) => rows.GetValueOrDefault(id);
}
