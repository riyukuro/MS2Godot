using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CGameRule's special ability hot time sums (commonfiles/gamerule.cpp:782-812, :2897-2919), fed by the game event list's
/// GameEventSAHotTime and GameEventPCBangSAHotTime events (commonfiles/gameevent.cpp:653-726): OnStart adds the event's
/// rates under its id (AddHotTimeEvent), vfunc_6 takes them off (RemoveHotTimeEvent), and each change rebuilds the sums
/// (FUN_14047f9d0): a PC bang event's rates into one sum, every other event's into the other. The ids are kept per
/// hot time event here; GameEventManagerCli keeps one map over every event type, so an id another type holds is not added
/// again there (Add, gameeventmanagercli.cpp:211-220).
/// </summary>
public sealed class SpecialAbilityHotTimes
{
    readonly Func<int, bool> perMille;
    readonly Dictionary<int, (bool PcBang, Dictionary<int, int> Rates)> events = new();
    readonly Dictionary<int, int> normal = new();    // m_b0
    readonly Dictionary<int, int> pcBang = new();    // m_c0

    /// <param name="perMille">FUN_14081dcd0 (specialability.cpp:467): whether the special ability's valueType is 2, a rate
    /// the event value gives in units (stored times 1000).</param>
    public SpecialAbilityHotTimes(Func<int, bool> perMille) { this.perMille = perMille ?? (_ => false); }

    /// <summary>The number of hot time events held (CGameRule m_38).</summary>
    public int Count => events.Count;

    /// <summary>
    /// GameEventSAHotTime::vfunc_2 @0x1403bba50 (gameevent.cpp:671-688): the ids (FUN_140274fc0, commonstruct_wc.cpp:189:
    /// the "," tokens, empty ones skipped, each through _wtoi) and the values (ParseFloatList, commonutil.cpp:1676: each
    /// token through _wtof as a float). Counts that differ store nothing; else per id the value times 1000 for a valueType 2
    /// ability (FUN_14081dcd0), else the value, truncated; a repeated id keeps its last value (std::map operator[]).
    /// </summary>
    public Dictionary<int, int> Parse(SpecialAbilityHotTimeEvent e)
    {
        var rates = new Dictionary<int, int>();
        var ids = Tokens(e.Ids);
        var values = Tokens(e.Values);
        if (ids.Count != values.Count) return rates;
        for (int i = 0; i < ids.Count; i++)
        {
            int id = Ms2.Ui.Skills.Xml.Atoi(ids[i]);   // _wtoi
            float v = (float)Ms2.Formats.Text.ArgumentFormat.Atof(values[i]);
            rates[id] = perMille(id) ? (int)(float)(v * 1000.0f) : (int)v;
        }
        return rates;
    }

    // CStringW::Tokenize(L",") until an empty token: runs of delimiters are skipped
    static List<string> Tokens(string s)
    {
        var list = new List<string>();
        foreach (var t in (s ?? "").Split(',')) if (t.Length != 0) list.Add(t);
        return list;
    }

    /// <summary>GameEventManagerCli::Dispatch (gameeventmanagercli.cpp:77-95) for the hot time events of one message.</summary>
    public void Apply(GameEventMessage m)
    {
        bool changed = false;
        switch (m.Kind)
        {
            case 0:
            case 1:
                // Packet0 / Packet1 -> Add (:211-220): an id already held is not added again; else OnStart (:715-724)
                foreach (var e in m.HotTimes)
                {
                    if (events.ContainsKey(e.Id)) continue;
                    events[e.Id] = (e.PcBang, Parse(e));
                    changed = true;
                }
                break;
            case 2:
                // Packet2 -> Remove (:171-185, :223-233): vfunc_6 (:728) -> RemoveHotTimeEvent
                foreach (int id in m.RemovedIds) changed |= events.Remove(id);
                break;
            case 3:
                // Packet3 -> Update (:236-242): vfunc_7 (gameeventbase.cpp:202-207) runs the old one's vfunc_6, then OnStart
                foreach (var e in m.HotTimes)
                {
                    events[e.Id] = (e.PcBang, Parse(e));
                    changed = true;
                }
                break;
        }
        if (changed) Rebuild();
    }

    /// <summary>GameEventManagerCli::Clear (:98-107): every event's vfunc_6.</summary>
    public void Clear()
    {
        events.Clear();
        Rebuild();
    }

    /// <summary>CGameRule::FUN_14047f9d0 @0x14047f9d0 (gamerule.cpp:2897): the sums over the held events.</summary>
    void Rebuild()
    {
        normal.Clear();
        pcBang.Clear();
        foreach (var (pc, rates) in events.Values)
        {
            var sum = pc ? pcBang : normal;
            foreach (var (k, v) in rates) sum[k] = (sum.TryGetValue(k, out int x) ? x : 0) + v;
        }
    }

    /// <summary>CGameRule::FUN_1404774a0 @0x1404774a0 (gamerule.cpp:805): the non PC bang sum of the ability, plus the PC
    /// bang sum when <paramref name="all"/> (the wearer's PC::FUN_141658f30, a PC bang grade above 0).</summary>
    public int Total(int type, bool all)
    {
        int n = normal.TryGetValue(type, out int a) ? a : 0;
        if (all && pcBang.TryGetValue(type, out int b)) n += b;
        return n;
    }
}
