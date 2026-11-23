using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>
/// One &lt;level&gt; of an additional effect (AdditionalLevelData::Load @0x140172a30, commonfiles/additionalactionadditional.cpp:1045):
/// the parts the buff strip and its tooltip read.
/// </summary>
public sealed class AdditionalLevelData
{
    public ushort Level;
    public string StringParam = "";
    /// <summary>&lt;Basic durationTick keepCondition&gt;.</summary>
    public uint DurationTick;
    public int KeepCondition;
    /// <summary>&lt;Basic maxBuffCount&gt; (m_nMaxBuffCount, additionalactionadditional.cpp:1063; 1 without it, :854): the
    /// overlap cap AdditionalCustomCli's ctor (additionalcli_1.cpp:710) clamps its count to.</summary>
    public uint MaxBuffCount = 1;
    /// <summary>&lt;Basic eventBuffType&gt; (m_nEventBuffType, :1053): 3 marks the adventure level's current ability
    /// (CAdventureLevelTableHelper::FindCurrent).</summary>
    public uint EventBuffType;
    /// <summary>&lt;Basic pvpOnOff&gt; (m_bPvpOnOff +0x64, :1066): AdditionalCliList::FUN_140972a00 (additionalcli_2.cpp:1081)
    /// sets the list's pvp on/off from any enabled, unsuspended effect with it.</summary>
    public bool PvpOnOff;
    /// <summary>&lt;Basic forceApplyTarget&gt; (m_nForceApplyTarget, :1074): the target filter bits FUN_140972a00
    /// (additionalcli_2.cpp:1096) ORs into the list's GetForceApplyTarget.</summary>
    public int ForceApplyTarget;
    /// <summary>&lt;Basic disableRevivalHere&gt; (m_bDisableRevivalHere +0x6b, additionalactionadditional.cpp:1079):
    /// FUN_140972a00 (additionalcli_2.cpp:1100) sets the list's IsDisableRevivalHere from any enabled, unsuspended effect with it.</summary>
    public bool DisableRevivalHere;
    /// <summary>&lt;Motion hide&gt; (m_motion.m_nHide, :1100): FUN_140972a00 (additionalcli_2.cpp:1072) sets the list's
    /// IsHidden from any enabled, unsuspended effect with it.</summary>
    public int Hide;
    /// <summary>&lt;Motion superArmor superArmorApplyFieldType&gt; (m_motion.m_nSuperArmor, m_eSuperArmorApplyFieldType,
    /// additionalactionadditional.cpp:1112-1113): FUN_140972a00 (additionalcli_2.cpp:1077) keeps the largest that
    /// FUN_1409726e0 lets count in the field as the list's super armor.</summary>
    public uint SuperArmor;
    public int SuperArmorApplyFieldType;
    /// <summary>&lt;Defensive invincible&gt; (m_nInvincible +0x29c, additionalactionadditional.cpp:1256): AdditionalCliList::FUN_140972a00
    /// (additionalcli_2.cpp:1075) takes the list's GetInvincible from it.</summary>
    public int Invincible;
    /// <summary>&lt;SpecialEffect type probability values&gt; (m_specialEffect, :1349-1353; Clear @0x140192c40 makes the
    /// probability 1): the kind CSpecialEffectFunctionManager runs (4 CSEReduceSkillGroupCooltime), the chance against
    /// FUN_140768090's roll and the values' attributes (SpecialEffectProperty::Parse @0x140192ce0, CXmlData::ParseValues
    /// @0x14092c400: the attributes of the one element the string holds; null without values).</summary>
    public int SpecialEffectType;
    public float SpecialEffectProbability = 1f;
    public Dictionary<string, string> SpecialEffectValues;
    /// <summary>&lt;InvokeEffect skillID skillGroupID effectID effectGroupID types values rates&gt; (m_invokeEffect, :1335-1344;
    /// the lists split at ',' by XmlTokenList): the stat options the effect adds to a skill, a skill group, an effect or an
    /// effect group; the lists are null when absent.</summary>
    public uint InvokeSkillId, InvokeSkillGroupId, InvokeEffectId, InvokeEffectGroupId;
    public int[] InvokeTypes, InvokeValues;
    public float[] InvokeRates;
    /// <summary>
    /// &lt;SpecialAbility&gt;&lt;v type value/&gt; (m_status.m_mapSA, :1227-1250): per &lt;v&gt; its type by name
    /// (XmlValueByName, GetSATypeByName @0x14081d2f0); an unknown name or a type whose SpecialAbilityInfo value type is 0
    /// (m_bAllowAdditional) is skipped, a value of 0 is not stored and a later row of the same type replaces the earlier one.
    /// Null when the level has none.
    /// </summary>
    public Dictionary<int, int> SpecialAbilities;

    /// <summary>InvokeEffectProperty::FUN_140192ac0 @0x140192ac0 (additionalactionadditional.cpp:1924): a target and the three
    /// lists not empty.</summary>
    public bool HasInvokeEffect =>
        (InvokeSkillId != 0 || InvokeEffectId != 0 || InvokeSkillGroupId != 0 || InvokeEffectGroupId != 0)
        && InvokeTypes is { Length: > 0 } && InvokeValues is { Length: > 0 } && InvokeRates is { Length: > 0 };
    /// <summary>&lt;UI iconOnOff&gt; (m_eIconOnOff: 0 off, 1 on (the default), 2 only for one's own buffs), icon, priority,
    /// isHideCooltime.</summary>
    public int IconOnOff = 1;
    public string Icon = "";
    public int Priority;
    public bool IsHideCooltime;
    /// <summary>The effect the level belongs to (AdditionalLevelData(AdditionalData*)).</summary>
    public AdditionalData Parent;

    /// <summary>IsKeepCondition99 @0x140175250 (:1472).</summary>
    public bool IsKeepCondition99 => KeepCondition == 99;
    /// <summary>IsHideBuffIcon @0x140175290 (:1478): keep condition 99 or isHideCooltime.</summary>
    public bool IsHideBuffIcon => IsKeepCondition99 || IsHideCooltime;
    /// <summary>The effect is a buff (AdditionalData::IsDebuff is false).</summary>
    public bool IsBuff => Parent?.IsDebuff != true;

    /// <summary>UIProperty::FUN_1401927c0 (:1896): the icon shows unless iconOnOff is 0, or 2 and <paramref name="mine"/> is
    /// false, or there is no icon.</summary>
    public bool ShowIcon(bool mine) => IconOnOff != 0 && !(IconOnOff == 2 && !mine) && Icon.Length > 0;

    /// <param name="saType">The special ability of a &lt;SpecialAbility&gt; type name, 0 for an unknown one or one not allowed
    /// here; null = no special abilities are read.</param>
    public static AdditionalLevelData Load(XElement e, AdditionalData parent, Func<string, int> saType = null)
    {
        var lv = new AdditionalLevelData
        {
            Level = (ushort)Xml.Int(e, "level"),
            StringParam = (string)e.Attribute("stringParam") ?? "",
            Parent = parent,
        };
        if (e.Element("Basic") is { } basic)
        {
            lv.DurationTick = Xml.UInt(basic, "durationTick");
            lv.KeepCondition = Xml.Int(basic, "keepCondition");
            if (basic.Attribute("maxBuffCount") != null) lv.MaxBuffCount = Xml.UInt(basic, "maxBuffCount");
            lv.EventBuffType = Xml.UInt(basic, "eventBuffType");
            lv.PvpOnOff = Xml.Bool(basic, "pvpOnOff");
            lv.ForceApplyTarget = Xml.Int(basic, "forceApplyTarget");
            lv.DisableRevivalHere = Xml.Bool(basic, "disableRevivalHere");
        }
        if (e.Element("Motion") is { } motion)
        {
            lv.Hide = Xml.Int(motion, "hide");
            lv.SuperArmor = Xml.UInt(motion, "superArmor");
            lv.SuperArmorApplyFieldType = Xml.Int(motion, "superArmorApplyFieldType");
        }
        if (e.Element("Defensive") is { } defensive) lv.Invincible = Xml.Int(defensive, "invincible");
        if (e.Element("InvokeEffect") is { } invoke)
        {
            lv.InvokeSkillId = Xml.UInt(invoke, "skillID");
            lv.InvokeSkillGroupId = Xml.UInt(invoke, "skillGroupID");
            lv.InvokeEffectId = Xml.UInt(invoke, "effectID");
            lv.InvokeEffectGroupId = Xml.UInt(invoke, "effectGroupID");
            lv.InvokeTypes = TokenInts((string)invoke.Attribute("types"));
            lv.InvokeValues = TokenInts((string)invoke.Attribute("values"));
            lv.InvokeRates = TokenFloats((string)invoke.Attribute("rates"));
        }
        if (saType != null && e.Element("SpecialAbility") is { } sa)
            foreach (var v in sa.Elements("v"))
            {
                int type = saType((string)v.Attribute("type") ?? "");
                if (type == 0) continue;
                int value = Xml.Int(v, "value");
                if (value != 0) (lv.SpecialAbilities ??= new Dictionary<int, int>())[type] = value;
            }
        if (e.Element("SpecialEffect") is { } special)
        {
            lv.SpecialEffectType = Xml.Int(special, "type");
            if (special.Attribute("probability") != null) lv.SpecialEffectProbability = Xml.Float(special, "probability");
            lv.SpecialEffectValues = ParseValues((string)special.Attribute("values"));
        }
        if (e.Element("UI") is { } ui)
        {
            if (ui.Attribute("iconOnOff") != null) lv.IconOnOff = Xml.Int(ui, "iconOnOff");
            lv.IsHideCooltime = Xml.Bool(ui, "isHideCooltime");
            lv.Priority = Xml.Int(ui, "priority");
            lv.Icon = (string)ui.Attribute("icon") ?? "";
        }
        return lv;
    }

    /// <summary>CXmlData::ParseValues @0x14092c400 (xmldata.cpp:112): the attributes of the element the string holds; null for
    /// an empty string or one that does not parse.</summary>
    internal static Dictionary<string, string> ParseValues(string values)
    {
        if (string.IsNullOrEmpty(values)) return null;
        XElement v;
        try { v = XElement.Parse(values); }
        catch (System.Xml.XmlException) { return null; }
        var d = new Dictionary<string, string>();
        foreach (var a in v.Attributes()) d[a.Name.LocalName] = a.Value;
        return d;
    }

    /// <summary>XmlTokenList (xmlattr.h:126): the attribute split at ','; null without the attribute.</summary>
    static int[] TokenInts(string s)
    {
        if (s == null) return null;
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var a = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a[i]);
        return a;
    }

    static float[] TokenFloats(string s)
    {
        if (s == null) return null;
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var a = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a[i]);
        return a;
    }

    /// <summary>CXmlData::GetInt (cxmldata.h): the value as an int, <paramref name="def"/> when the name is not there.</summary>
    public int ValueInt(string name, int def = 0) =>
        SpecialEffectValues != null && SpecialEffectValues.TryGetValue(name, out var s)
            ? int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0 : def;

    /// <summary>CXmlData::GetFloat @0x1408276a0 (specialeffectfunctionmanager.cpp:66): ParseFloatW (_wtof) of the value,
    /// <paramref name="def"/> when the name is not there.</summary>
    public float ValueFloat(string name, float def = 0f) =>
        SpecialEffectValues != null && SpecialEffectValues.TryGetValue(name, out var s)
            ? float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f : def;
}

/// <summary>One &lt;additional&gt; of additional/*.xml (AdditionalData::Load @0x140170c60, additionalactionadditional.cpp:707).</summary>
public sealed class AdditionalData
{
    public uint Id;
    public int MainType;
    public bool IsDebuff;
    /// <summary>The levels in file order.</summary>
    internal readonly List<AdditionalLevelData> Levels = new();

    /// <summary>Load: the &lt;level&gt; rows whose feature is enabled (IsElementFeatureEnabled, no locale test), in file order;
    /// null when none is left (Load returns false).</summary>
    public static AdditionalData Load(XElement e, Func<XElement, bool> featureEnabled, Func<string, int> saType = null)
    {
        var d = new AdditionalData
        {
            Id = Xml.UInt(e, "id"),
            MainType = Xml.Int(e, "mainType"),
            IsDebuff = Xml.Bool(e, "isDebuff"),
        };
        foreach (var l in e.Elements("level"))
            if (featureEnabled(l)) d.Levels.Add(AdditionalLevelData.Load(l, d, saType));          // ReadLevel (:825)
        return d.Levels.Count > 0 ? d : null;
    }

    /// <summary>GetLevel @0x140170fe0 (:819): a binary search over the levels (FindSorted, a lower bound): the first level not
    /// below <paramref name="level"/>, null past the last.</summary>
    public AdditionalLevelData GetLevel(ushort level)
    {
        int first = 0, count = Levels.Count;
        while (count > 0)
        {
            int step = count / 2, it = first + step;
            if (Levels[it].Level < level) { first = it + 1; count -= step + 1; }
            else count = step;
        }
        return first < Levels.Count ? Levels[first] : null;
    }
}

/// <summary>
/// SkillDataTableCli's additional effects (GetAdditional @0x140aafd60, FUN_140aafef0 @0x140aafef0, skilldatatablecli.cpp:300-330)
/// over ./Data/Xml/Additional/*.xml, by id. The client reads every file at startup (a later row with the same id replaces the
/// earlier one). Here the effects come from the per-install <see cref="AdditionalCache"/>, each id decoded on its first lookup.
/// Without a valid cache the XML is parsed on first use: the files are named by id prefix (additional/105.xml holds the
/// 105xxxxx ids), so an id's own files are read on its first lookup and the rest only when it is not in them.
/// </summary>
public sealed class AdditionalTable
{
    readonly Func<IReadOnlyList<string>> names;
    readonly Func<string, XDocument> read;
    readonly Func<XElement, bool> featureEnabled;
    readonly Func<string, int> saType;
    readonly Dictionary<uint, AdditionalData> data = new();
    readonly HashSet<string> loaded = new(StringComparer.Ordinal);
    readonly AdditionalCache cache;
    bool all;
    Dictionary<string, string> gms2Files;     // GMS2: the file of each id stem

    public static readonly AdditionalTable Empty = new(() => Array.Empty<string>(), _ => null, _ => true);

    /// <param name="names">The additional/*.xml entry names.</param>
    /// <param name="read">Reads one entry.</param>
    /// <param name="featureEnabled">IsElementFeatureEnabled.</param>
    /// <param name="cache">The per-install cache; null or invalid = parse the XML.</param>
    /// <param name="saType">The special ability of a type name (see <see cref="AdditionalLevelData.SpecialAbilities"/>).</param>
    public AdditionalTable(Func<IReadOnlyList<string>> names, Func<string, XDocument> read, Func<XElement, bool> featureEnabled,
                           AdditionalCache cache = null, Func<string, int> saType = null)
    {
        this.saType = saType;
        this.names = names;
        this.read = read;
        this.featureEnabled = featureEnabled;
        this.cache = cache;
    }

    /// <summary>Every effect from the XML (the cache build).</summary>
    internal IEnumerable<AdditionalData> LoadAll()
    {
        foreach (var n in names()) LoadFile(n);
        all = true;
        return data.Values;
    }

    void LoadFile(string name)
    {
        if (!loaded.Add(name)) return;
        if (read(name)?.Root is not { } root || root.Name != "ms2") return;
        if (IsGms2File(name))
        {
            if (uint.TryParse(Path.GetFileNameWithoutExtension(name), NumberStyles.None, CultureInfo.InvariantCulture, out uint id)
                && AdditionalData.Load(FromGms2(id, root), featureEnabled, saType) is { } g) data[g.Id] = g;
            return;
        }
        foreach (var e in root.Elements("additional"))
            if (AdditionalData.Load(e, featureEnabled, saType) is { } d) data[d.Id] = d;
    }

    /// <summary>The GMS2 data's folder of per-effect files, additionaleffect/&lt;id&gt;.xml, read when the data has no additional/.</summary>
    public const string Gms2Folder = "additionaleffect/";

    internal static bool IsGms2File(string name) => name.StartsWith(Gms2Folder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A GMS2 additionaleffect/&lt;id&gt;.xml root as the KMS &lt;additional&gt; row that <see cref="AdditionalData.Load"/> reads. The file
    /// has no row: the id is the file name, and each &lt;level&gt; holds the KMS level's children named with a "Property" suffix
    /// (BasicProperty, MotionProperty, UIProperty, DefensiveProperty, InvokeEffectProperty, SpecialEffectProperty, ...), so the
    /// suffix is dropped. The level number is BasicProperty@level (beginCondition@level is a requirement). There is no isDebuff:
    /// the first level's BasicProperty@buffType 2 is a debuff (in the KMS data, every row that also has an additionaleffect file is
    /// isDebuff 1 and buffType 2). The levels are put in level order (stable), since GetLevel searches them sorted and 214 GMS2
    /// files list them out of order. InvokeEffect values are written as floats ("1.000000"; all are whole numbers in the data)
    /// and become the integers the KMS row has.
    /// The special abilities (StatusProperty/Stat and /SpecialAbility, one attribute per ability, and the damage rates of
    /// OffensiveProperty / DefensiveProperty) become the KMS &lt;SpecialAbility&gt; list: see <see cref="Gms2SpecialAbilities"/>.
    /// STILL-OPEN: the GMS2 exe is not decompiled, so how it reads these files is not seen.
    /// </summary>
    internal static XElement FromGms2(uint id, XElement root)
    {
        var levels = new List<XElement>();
        foreach (var l in root.Elements("level"))
        {
            var lv = new XElement("level", l.Attributes());
            lv.SetAttributeValue("level", (string)l.Element("BasicProperty")?.Attribute("level") ?? "1");
            foreach (var c in l.Elements())
            {
                string n = c.Name.LocalName;
                if (n.EndsWith("Property", StringComparison.Ordinal)) n = n[..^"Property".Length];
                var e = new XElement(n, c.Attributes(), c.Nodes());
                if (n == "InvokeEffect" && (string)e.Attribute("values") is { Length: > 0 } values)
                    e.SetAttributeValue("values", string.Join(",", values.Split(',').Select(v =>
                        double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) && f == Math.Floor(f)
                            ? ((long)f).ToString(CultureInfo.InvariantCulture) : v.Trim())));
                lv.Add(e);
            }
            if (Gms2SpecialAbilities.FromLevel(l) is { } sa) lv.Add(sa);
            levels.Add(lv);
        }
        levels = levels.OrderBy(l => Xml.Int(l, "level")).ToList();
        var first = levels.Count > 0 ? levels[0].Element("Basic") : null;
        return new XElement("additional", new XAttribute("id", id),
            new XAttribute("isDebuff", (string)first?.Attribute("buffType") == "2" ? "1" : "0"), levels);
    }

    /// <summary>GetAdditional: null for 0 or an unknown id.</summary>
    public AdditionalData GetAdditional(uint id)
    {
        if (id == 0) return null;
        if (data.TryGetValue(id, out var d)) return d;
        if (cache?.Valid == true) return data[id] = cache.ReadEffect(id);
        if (all) return null;
        string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var files = names();
        if (files.Count > 0 && IsGms2File(files[0]))
        {
            // one file per id: only that file can hold it
            gms2Files ??= files.GroupBy(n => Path.GetFileNameWithoutExtension(n), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (gms2Files.TryGetValue(key, out var file)) LoadFile(file);
            return data.TryGetValue(id, out d) ? d : null;
        }
        foreach (var n in files)
        {
            string stem = Path.GetFileNameWithoutExtension(n);
            if (stem.Length > 0 && key.StartsWith(stem, StringComparison.Ordinal)) LoadFile(n);
        }
        if (data.TryGetValue(id, out d)) return d;
        foreach (var n in files) LoadFile(n);
        all = true;
        return data.TryGetValue(id, out d) ? d : null;
    }

    /// <summary>FUN_140aafef0: the effect's level (<see cref="AdditionalData.GetLevel"/>); null without the effect.</summary>
    public AdditionalLevelData GetLevel(uint id, ushort level) => GetAdditional(id)?.GetLevel(level);
}
