using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>
/// CReactorDataTable (commonfiles/reactordata.cpp): the field reactors' data from Xml.m2d table/reactor.xml
/// (&lt;ms2&gt;&lt;reactor .../&gt;) and their states from table/reactorstate.xml (&lt;ms2&gt;&lt;reactorState .../&gt;). The states
/// load first (LoadStates @0x14076b560) since a reactor resolves its state list by id.
/// </summary>
public sealed class ReactorTable
{
    /// <summary>ReactorConditionItem (LoadConditionItem @0x14076a1e0): an item the player must hold; rank 1 unless set
    /// (0 = the item's own rank), consume and checkCount 0 unless set.</summary>
    public sealed record Condition(int Code, int Rank, int Consume, int CheckCount);

    /// <summary>ReactorStateData (Load @0x14076a550). Type: "connect" 1, "sleep" 2, "reward" 3, anything else 0
    /// (@0x14076be60, case-insensitive).</summary>
    public sealed class State
    {
        public int Id, Type, NextTimeSec, ExpireTimeSec;
        public string KfmName = "", StartAni = "", IdleAni = "", ActionStringKey = "";
        public List<Condition> Conditions = new();
    }

    /// <summary>A reward row of rewardItemListXml (&lt;items&gt;&lt;v itemID count grade/&gt;&lt;/items&gt;, ItemEntity::vfunc_2).</summary>
    public readonly record struct Reward(int ItemId, int Count, int Grade);

    /// <summary>ReactorData (Load @0x14076ab40).</summary>
    public sealed class Reactor
    {
        public int Id;
        public List<string> CreateAnimations = new(), ReactAnimations = new();
        /// <summary>reactTimeMSec, 15000 unless set.</summary>
        public int ReactTimeMSec = 15000;
        public float ReactDistance;
        public string ConnectedEffect = "";
        public uint GameEventId;
        /// <summary>m_states: indexed by the packet's state index (CReactor::GetStateData @0x140b78c00).</summary>
        public List<State> States = new();
        public List<Reward> Rewards = new();

        /// <summary>CReactor::GetStateData @0x140b78c00: null below 0 or past the last state.</summary>
        public State StateAt(int index) => index >= 0 && index < States.Count ? States[index] : null;
    }

    readonly Dictionary<int, State> states = new();
    readonly Dictionary<int, Reactor> reactors = new();

    /// <param name="available">IsElementAvailable for a &lt;reactor&gt; row (its locale and feature); every row when null.
    /// LoadStates checks no availability.</param>
    public ReactorTable(PackSet packs, Func<XElement, bool> available = null)
        : this(Doc(packs, "table/reactorstate.xml"), Doc(packs, "table/reactor.xml"), available) { }

    /// <summary>A table from already parsed documents (tests).</summary>
    public ReactorTable(XDocument stateXml, XDocument reactorXml, Func<XElement, bool> available = null)
    {
        LoadStates(stateXml);
        LoadReactors(reactorXml, available);
    }

    static XDocument Doc(PackSet packs, string entry) =>
        packs.Exists("Xml", entry) ? GameStrings.Parse(packs.Read("Xml", entry)) : null;

    public Reactor Get(int id) => reactors.GetValueOrDefault(id);
    public State GetState(int id) => states.GetValueOrDefault(id);
    public int Count => reactors.Count;

    /// <summary>LoadStates @0x14076b560: every &lt;reactorState&gt; under &lt;ms2&gt;, the last row of an id wins (SetAt).</summary>
    void LoadStates(XDocument x)
    {
        var root = x?.Root;
        if (root == null || root.Name != "ms2") return;
        foreach (var e in root.Elements("reactorState"))
        {
            var s = new State
            {
                Id = Int(e, "id", 0),
                Type = StateType((string)e.Attribute("type")),
                NextTimeSec = Int(e, "nextTimeSec", 0),
                ExpireTimeSec = Int(e, "expireTimeSec", 0),
                KfmName = (string)e.Attribute("kfmName") ?? "",
                StartAni = (string)e.Attribute("startAni") ?? "",
                IdleAni = (string)e.Attribute("idleAni") ?? "",
                ActionStringKey = (string)e.Attribute("actionStringKey") ?? "",
            };
            // the loader list holds one entry ("condItem") and takes only its FirstChild; a condItem without a code gives none
            var c = e.Element("condItem");
            if (c != null && int.TryParse((string)c.Attribute("code"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                s.Conditions.Add(new Condition(code, Int(c, "rank", 1), Int(c, "consume", 0), Int(c, "checkCount", 0)));
            states[s.Id] = s;
        }
    }

    /// <summary>
    /// LoadReactors @0x14076b2e0: each available &lt;reactor&gt;; ReactorData::Load fails (the row dropped) when a state id of
    /// reactorStateList (split on ", []") is not in the state table or the reward xml does not parse. The last row of an id wins.
    /// </summary>
    void LoadReactors(XDocument x, Func<XElement, bool> available)
    {
        var root = x?.Root;
        if (root == null || root.Name != "ms2") return;
        foreach (var e in root.Elements("reactor"))
        {
            if (available != null && !available(e)) continue;
            var r = Load(e);
            if (r != null) reactors[r.Id] = r;
        }
    }

    Reactor Load(XElement e)
    {
        var r = new Reactor
        {
            Id = Int(e, "id", 0),
            ReactTimeMSec = Int(e, "reactTimeMSec", 15000),
            ReactDistance = float.TryParse((string)e.Attribute("reactDistance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 0,
            ConnectedEffect = (string)e.Attribute("connectedEffect") ?? "",
            GameEventId = uint.TryParse((string)e.Attribute("gameEventId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint g) ? g : 0,
        };
        Tokens((string)e.Attribute("createAnimation"), ",", r.CreateAnimations);
        Tokens((string)e.Attribute("reactAnimation"), ",", r.ReactAnimations);
        var ids = new List<string>();
        Tokens((string)e.Attribute("reactorStateList"), ", []", ids);
        foreach (var t in ids)
        {
            int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id);
            var s = GetState(id);
            if (s == null) return null;
            r.States.Add(s);
        }
        string xml = (string)e.Attribute("rewardItemListXml");
        if (!string.IsNullOrEmpty(xml))
        {
            XElement items;
            try { items = XElement.Parse(xml); }
            catch (System.Xml.XmlException) { return null; }
            foreach (var v in items.Elements("v"))
                r.Rewards.Add(new Reward(Int(v, "itemID", 0), Int(v, "count", 0), Int(v, "grade", 0)));
        }
        return r;
    }

    /// <summary>CString::Tokenize: any of the delimiters splits, empty tokens are skipped.</summary>
    static void Tokens(string s, string delims, List<string> into)
    {
        if (string.IsNullOrEmpty(s)) return;
        foreach (var t in s.Split(delims.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)) into.Add(t);
    }

    /// <summary>The state type reader @0x14076be60.</summary>
    public static int StateType(string s) =>
        string.Equals(s, "connect", StringComparison.OrdinalIgnoreCase) ? 1 :
        string.Equals(s, "sleep", StringComparison.OrdinalIgnoreCase) ? 2 :
        string.Equals(s, "reward", StringComparison.OrdinalIgnoreCase) ? 3 : 0;

    static int Int(XElement e, string a, int def) =>
        int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
}
