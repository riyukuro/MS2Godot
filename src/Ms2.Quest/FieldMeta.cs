using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// The parts of Table/FieldMetaData.xml (CFieldMetaTable::LoadFieldMetaData) the quest guide reads: which fields have portals
/// to which (fieldPortal / portal targetField, portalType 0 only, as HasPortalTarget) and which fields an npc stands in.
/// </summary>
public sealed class FieldMeta
{
    public const string Archive = "Xml";
    public const string Entry = "table/kr/fieldmetadata.xml";

    /// <summary>Every field that has a fieldPortal element, in file order (the nodes of the map graph).</summary>
    public readonly List<int> PortalFields = new();
    /// <summary>field -> target fields of its portalType 0 portals (HasPortalTarget).</summary>
    public readonly Dictionary<int, List<int>> PortalTargets = new();
    /// <summary>npc id -> fields it stands in.</summary>
    public readonly Dictionary<int, List<int>> NpcFields = new();
    /// <summary>
    /// The (npc, field) pairs with a time window (m_activeTimes: a field element with a startTime and an endTime that
    /// both parse, ParseTimeString); the npc stands there only while the server time is inside it (FUN_1406fbb50).
    /// </summary>
    public readonly HashSet<(int Npc, int Field)> TimedNpcFields = new();
    /// <summary>
    /// field -> its outdoor field (GetParentField, m_reverse): for each outdoor fieldPortal (indoor="0"), every field its
    /// portalIndoor portals lead to, and on through those fields' own portalIndoor portals (FUN_1406fbdf0), has it as
    /// the parent. The outdoor fields are walked in m_indoorPortals' hash map order (<see cref="AtlMapOrder"/>), so a field
    /// two outdoor fields reach keeps the one that comes later in that order.
    /// </summary>
    public readonly Dictionary<int, int> ParentFields = new();
    /// <summary>m_taxi (fieldmetatable.cpp:235): the &lt;taxiStation&gt; fields and their IsHomePrivilege (HasTaxi,
    /// GetTaxiHomePrivilege).</summary>
    public readonly Dictionary<int, bool> TaxiStations = new();
    /// <summary>m_indoorPortals (FUN_1406fc160): field -> the fields its portalIndoor portals lead to, in portal order.</summary>
    public readonly Dictionary<int, List<int>> IndoorPortals = new();

    /// <summary>FUN_1406fc2f0 (fieldmetatable.cpp:690): the field and every field its indoor portals reach (FUN_1406fc1b0),
    /// ascending (std::set).</summary>
    public List<int> ConnectedFields(int field)
    {
        var seen = new SortedSet<int> { field };
        void Walk(int f)
        {
            if (!IndoorPortals.TryGetValue(f, out var l)) return;
            foreach (int n in l)
                if (seen.Add(n)) Walk(n);
        }
        Walk(field);
        return seen.ToList();
    }

    public static FieldMeta Load(PackSet packs) =>
        Parse(System.Text.Encoding.UTF8.GetString(packs.Read(Archive, Entry)).TrimStart('﻿'));

    public static FieldMeta Parse(string xml)
    {
        var m = new FieldMeta();
        var root = XDocument.Parse(xml).Root;
        foreach (var npc in root.Elements("npc"))
        {
            int id = Int(npc, "id");
            foreach (var f in npc.Elements("field"))
            {
                if (!m.NpcFields.TryGetValue(id, out var l)) m.NpcFields[id] = l = new List<int>();
                l.Add(Int(f, "id"));
                if (f.Attribute("startTime") != null && f.Attribute("endTime") != null
                    && ParseTimeString((string)f.Attribute("startTime")) && ParseTimeString((string)f.Attribute("endTime")))
                    m.TimedNpcFields.Add((id, Int(f, "id")));
            }
        }
        foreach (var t in root.Elements("taxiStation"))
            m.TaxiStations[Int(t, "field")] = (string)t.Attribute("IsHomePrivilege") is "1" or "true";
        var indoorPortals = m.IndoorPortals;
        var outdoor = new Dictionary<int, bool>();          // outdoor[nField] = (indoor == 0): a later fieldPortal of the field decides
        foreach (var fp in root.Elements("fieldPortal"))
        {
            int field = Int(fp, "field");
            var targets = new List<int>();
            foreach (var p in fp.Elements("portal"))
                if (Int(p, "portalType") == 0) targets.Add(Int(p, "targetField"));
            m.PortalFields.Add(field);
            m.PortalTargets[field] = targets;
            var indoor = new List<int>();
            foreach (var p in fp.Elements("portal"))
                if (Int(p, "portalIndoor") != 0 && !indoor.Contains(Int(p, "targetField"))) indoor.Add(Int(p, "targetField"));
            indoorPortals[field] = indoor;
            outdoor[field] = Int(fp, "indoor") == 0;
        }
        foreach (int field in AtlMapOrder(indoorPortals.Keys).Where(f => outdoor[f]))
        {
            var done = new List<int>();
            var from = new List<int> { field };
            while (from.Count > 0)
            {
                var merged = new List<int>();
                foreach (int f in from)
                    if (indoorPortals.TryGetValue(f, out var targets))
                        foreach (int t in targets)
                            if (!done.Contains(t)) { merged.Add(t); done.Add(t); }
                from = merged;
            }
            foreach (int f in done) m.ParentFields[f] = field;
        }
        return m;
    }

    /// <summary>
    /// The order CAtlMap&lt;unsigned, ...&gt;(17, 0.75, 0.25, 2.25) gives its keys (atlcoll.h, MSVC 14.16): a new key goes to
    /// the head of bin hash % bins (the hash of an integer is itself); past bins * 2.25 elements the map rehashes to the
    /// smallest listed prime not below elements / 0.75, walking the old bins in order and pushing each node to the head
    /// of its new bin; GetStartPosition / GetNext walk the bins in order and each bin from its head. Keys repeat-inserted
    /// keep their place (SetAt).
    /// </summary>
    public static List<int> AtlMapOrder(IEnumerable<int> insertOrder) => AtlMapOrder(insertOrder, k => (uint)k);

    /// <summary><see cref="AtlMapOrder(IEnumerable{int})"/> for any key with its CElementTraits hash.</summary>
    public static List<T> AtlMapOrder<T>(IEnumerable<T> insertOrder, Func<T, uint> hash)
    {
        uint[] primes = { 17, 23, 29, 37, 41, 53, 67, 83, 103, 131, 163, 211, 257, 331, 409, 521, 647, 821, 1031, 1291, 1627,
                          2053, 2591, 3251, 4099, 5167, 6521, 8209, 10331, 13007, 16411, 20663, 26017, 32771, 41299, 52021, 65537 };
        uint bins = 17;
        var table = new List<T>[bins];
        for (int i = 0; i < bins; i++) table[i] = new List<T>();     // each bin head first
        var seen = new HashSet<T>();
        int count = 0;
        foreach (T key in insertOrder)
        {
            if (!seen.Add(key)) continue;
            table[hash(key) % bins].Insert(0, key);
            count++;
            if (count > (int)(2.25f * bins))
            {
                uint estimate = (uint)(count / 0.75f);
                uint next = primes.First(p => estimate <= p);
                if (next == bins) continue;
                var nt = new List<T>[next];
                for (int i = 0; i < next; i++) nt[i] = new List<T>();
                foreach (var bin in table)
                    foreach (T k in bin) nt[hash(k) % next].Insert(0, k);
                table = nt; bins = next;
            }
        }
        return table.SelectMany(b => b).ToList();
    }

    /// <summary>
    /// CFieldMetaTable::ParseTimeString's check: sscanf "%d-%d-%d-%d-%d-%d" (the parts it does not read stay 0) and each
    /// part in range (year from 1900, month 1-12, day 1-31, hour 0-23, minute and second 0-59).
    /// </summary>
    public static bool ParseTimeString(string s)
    {
        var v = new int[6];
        int i = 0;
        for (int part = 0; part < 6; part++)
        {
            if (part > 0) { if (i >= s.Length || s[i] != '-') break; i++; }
            int j = i;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            bool neg = false;
            if (j < s.Length && (s[j] == '-' || s[j] == '+')) neg = s[j++] == '-';
            int start = j;
            long n = 0;
            while (j < s.Length && s[j] >= '0' && s[j] <= '9') n = n * 10 + (s[j++] - '0');
            if (j == start) break;
            v[part] = (int)(neg ? -n : n);
            i = j;
        }
        return v[0] >= 1900 && v[1] >= 1 && v[1] <= 12 && v[2] >= 1 && v[2] <= 31 && v[3] >= 0 && v[3] <= 23
            && v[4] >= 0 && v[4] <= 59 && v[5] >= 0 && v[5] <= 59;
    }

    static int Int(XElement e, string a) =>
        int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
