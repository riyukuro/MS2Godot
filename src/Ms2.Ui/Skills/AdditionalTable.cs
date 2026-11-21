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
    /// <summary>&lt;Basic eventBuffType&gt; (m_nEventBuffType, :1053): 3 marks the adventure level's current ability
    /// (CAdventureLevelTableHelper::FindCurrent).</summary>
    public uint EventBuffType;
    /// <summary>&lt;Basic pvpOnOff&gt; (m_bPvpOnOff +0x64, :1066): AdditionalCliList::FUN_140972a00 (additionalcli_2.cpp:1081)
    /// sets the list's pvp on/off from any enabled, unsuspended effect with it.</summary>
    public bool PvpOnOff;
    /// <summary>&lt;Basic forceApplyTarget&gt; (m_nForceApplyTarget, :1074): the target filter bits FUN_140972a00
    /// (additionalcli_2.cpp:1096) ORs into the list's GetForceApplyTarget.</summary>
    public int ForceApplyTarget;
    /// <summary>&lt;Motion hide&gt; (m_motion.m_nHide, :1100): FUN_140972a00 (additionalcli_2.cpp:1072) sets the list's
    /// IsHidden from any enabled, unsuspended effect with it.</summary>
    public int Hide;
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

    public static AdditionalLevelData Load(XElement e, AdditionalData parent)
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
            lv.EventBuffType = Xml.UInt(basic, "eventBuffType");
            lv.PvpOnOff = Xml.Bool(basic, "pvpOnOff");
            lv.ForceApplyTarget = Xml.Int(basic, "forceApplyTarget");
        }
        if (e.Element("Motion") is { } motion) lv.Hide = Xml.Int(motion, "hide");
        if (e.Element("UI") is { } ui)
        {
            if (ui.Attribute("iconOnOff") != null) lv.IconOnOff = Xml.Int(ui, "iconOnOff");
            lv.IsHideCooltime = Xml.Bool(ui, "isHideCooltime");
            lv.Priority = Xml.Int(ui, "priority");
            lv.Icon = (string)ui.Attribute("icon") ?? "";
        }
        return lv;
    }
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
    public static AdditionalData Load(XElement e, Func<XElement, bool> featureEnabled)
    {
        var d = new AdditionalData
        {
            Id = Xml.UInt(e, "id"),
            MainType = Xml.Int(e, "mainType"),
            IsDebuff = Xml.Bool(e, "isDebuff"),
        };
        foreach (var l in e.Elements("level"))
            if (featureEnabled(l)) d.Levels.Add(AdditionalLevelData.Load(l, d));          // ReadLevel (:825)
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
    readonly Dictionary<uint, AdditionalData> data = new();
    readonly HashSet<string> loaded = new(StringComparer.Ordinal);
    readonly AdditionalCache cache;
    bool all;

    public static readonly AdditionalTable Empty = new(() => Array.Empty<string>(), _ => null, _ => true);

    /// <param name="names">The additional/*.xml entry names.</param>
    /// <param name="read">Reads one entry.</param>
    /// <param name="featureEnabled">IsElementFeatureEnabled.</param>
    /// <param name="cache">The per-install cache; null or invalid = parse the XML.</param>
    public AdditionalTable(Func<IReadOnlyList<string>> names, Func<string, XDocument> read, Func<XElement, bool> featureEnabled,
                           AdditionalCache cache = null)
    {
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
        foreach (var e in root.Elements("additional"))
            if (AdditionalData.Load(e, featureEnabled) is { } d) data[d.Id] = d;
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
