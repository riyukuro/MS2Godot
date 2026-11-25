using Ms2.Formats;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;
using Status = Ms2.Formats.World.QuestMaskTable.QuestStatus;

namespace Ms2.Ui.Tests;

/// <summary>
/// The portals with their own quest mask (the entity's MaskQuestID / MaskQuestState, CPortal::FUN_14169b650 portal.cpp:158-174)
/// on the data: every map's such entity parses as ParseQuestMask does, and after the register its draw follows the mask in
/// every stage of the quests it names, together with the table row of the same portal (CheckPortal @0x140764f30).
/// </summary>
public class OwnMaskPortalsDataTests
{
    static readonly Status[] Stages = { Status.Absent, Status.Active, Status.Completed, Status.Rewarded };

    /// <summary>CString::Tokenize as the client splits (Unk_SplitImplA achievedata.cpp:501, XmlSplitImpl xmlattr.h:91): empty
    /// tokens are skipped.</summary>
    static List<string> Tokens(string s, char sep) => (s ?? "").Split(sep).Where(t => t.Length > 0).ToList();

    /// <summary>atoi.</summary>
    static int Atoi(string s)
    {
        int i = 0, sign = 1; long v = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') sign = -1; i++; }
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v < int.MaxValue + 1L) v = v * 10 + (s[i++] - '0');
        v *= sign;
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    /// <summary>ParseQuestMask @0x140764310 and FUN_140764760 (questmasktable.cpp:139-181) as written: {a, b, c, d} =
    /// {first id, second id, first state, second state}.</summary>
    static List<(int A, int B, int C, int D)> ClientParse(string ids, string states)
    {
        var outR = new List<(int, int, int, int)>();
        var s = Tokens(ids, '|'); var e = Tokens(states, '|');
        if (s.Count != e.Count) return outR;
        for (int i = 0; i < s.Count; i++)
        {
            var a = Tokens(s[i], ',').Select(Atoi).ToList(); var b = Tokens(e[i], ',').Select(Atoi).ToList();
            if (a.Count != b.Count) continue;
            int ra = 0, rb = 0, rc = 0, rd = 0;
            if (a.Count > 0) { ra = a[0]; rc = b[0]; }
            if (a.Count > 1) { rb = a[1]; rd = b[1]; }
            outR.Add((ra, rb, rc, rd));
        }
        return outR;
    }

    /// <summary>QuestMask::IsSatisfied @0x140266970: ranges, each with a start quest.</summary>
    static bool ClientSatisfied(List<(int A, int B, int C, int D)> r) => r.Count > 0 && r.All(x => x.A != 0);

    /// <summary>The stages to check: every combination for up to four quests (the first all absent); past that, each quest in
    /// each stage over each uniform stage of the others.</summary>
    static List<List<(int Q, Status S)>> StageCases(List<int> named)
    {
        var cases = new List<List<(int, Status)>>();
        if (named.Count <= 4)
        {
            int combos = 1; foreach (var _ in named) combos *= Stages.Length;
            for (int c = 0; c < combos; c++)
            {
                var one = new List<(int, Status)>(); int k = c;
                foreach (int q in named) { one.Add((q, Stages[k % Stages.Length])); k /= Stages.Length; }
                cases.Add(one);
            }
            return cases;
        }
        cases.Add(named.Select(q => (q, Status.Absent)).ToList());
        foreach (var bg in Stages)
            foreach (int q in named)
                foreach (var st in Stages)
                    cases.Add(named.Select(o => (o, o == q ? st : bg)).ToList());
        return cases;
    }

    static FieldPortalMessage Add(int id) => new()
    {
        Mode = 0, Id = id, TargetMap = 2000, Visible = true, Enabled = true, Dimension = new System.Numerics.Vector3(200, 200, 250),
    };

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryOwnMaskPortalIsDrawnByItsMaskInEveryQuestStage(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        var packs = TestData.Packs(dir);
        var flats = new FlatLibrary(packs);
        var masks = new QuestMaskTable(packs);
        int maps = 0, failedMaps = 0, own = 0, noQuest = 0, usable = 0, withTable = 0, stages = 0, closedAtStart = 0, loadShownClosed = 0, loadHiddenOpens = 0;
        var fails = new List<string>();
        var rows = new List<string>();
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            if (name.Length < 8 || !int.TryParse(name[..8], out int field)) continue;
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { failedMaps++; continue; }
            maps++;
            var portals = map.Entities.Where(e => e.HasMixin("Portal") && !string.IsNullOrEmpty(e.Get("MaskQuestID"))).ToList();
            if (portals.Count == 0) continue;
            var quests = new Dictionary<int, Status>();
            var live = new LivePortals(field, masks, null, q => quests.GetValueOrDefault(q));
            live.Attach(map.Entities);
            var drawn = map.Entities.ToDictionary(e => e, e => e.Bool("IsVisible", true));
            var draws = new PortalDraws((e, shown) => drawn[e] = shown);
            foreach (var e in portals)
            {
                own++;
                int id = (int)e.Number("PortalID");
                string ids = e.Get("MaskQuestID"), sts = e.Get("MaskQuestState");
                var client = ClientParse(ids, sts);
                var ours = QuestMaskTable.FromEntity(ids, sts);
                var oursR = ours.Ranges.Select(r => (r.StartQuest, r.EndQuest, r.StartState, r.EndState)).ToList();
                if (!oursR.SequenceEqual(client)) { fails.Add($"{name} {id}: parse \"{ids}\" / \"{sts}\": ours {string.Join(";", oursR)}, client {string.Join(";", client)}"); continue; }
                if (ours.IsSatisfied != ClientSatisfied(client)) { fails.Add($"{name} {id}: usable ours {ours.IsSatisfied}"); continue; }
                var table = masks.Portal(field, id);
                if (ours.IsSatisfied) usable++;
                if (table != null) withTable++;
                live.Apply(Add(id));
            }
            draws.Bind(live);
            foreach (var e in portals)
            {
                int id = (int)e.Number("PortalID");
                var p = live.ById(id);
                if (p == null || p.Entity != e) continue;
                var ownR = ClientParse(e.Get("MaskQuestID"), e.Get("MaskQuestState"));
                var table = masks.Portal(field, id);
                var named = ownR.SelectMany(r => new[] { r.A, r.B })
                    .Concat(table?.Ranges.SelectMany(r => new[] { r.StartQuest, r.EndQuest }) ?? Enumerable.Empty<int>())
                    .Where(q => q != 0).Distinct().ToList();
                int open = 0;
                var cases = StageCases(named);
                for (int c = 0; c < cases.Count; c++)
                {
                    quests.Clear();
                    foreach (var (q, st) in cases[c]) quests[q] = st;
                    live.RefreshMasks();
                    // CheckPortal as written: no mask passes (the register's flag stands); else own (when usable), then the table
                    bool ownOk = ClientSatisfied(ownR);
                    bool expect = !(ownOk || table != null)
                        || (ownOk && QuestMaskTable.Check(new QuestMaskTable.Mask { Ranges = ownR.Select(r => new QuestMaskTable.Range(r.A, r.C, r.B, r.D)).ToList() }, q => quests.GetValueOrDefault(q)))
                        || (table != null && QuestMaskTable.Check(table, q => quests.GetValueOrDefault(q)));
                    stages++;
                    if (c == 0 && !expect) closedAtStart++;
                    if (c == 0 && !expect && e.Bool("IsVisible", true)) loadShownClosed++;
                    if (expect && !e.Bool("IsVisible", true) && open == 0) loadHiddenOpens++;
                    if (expect) open++;
                    if (p.Shown != expect || drawn[e] != expect)
                        if (fails.Count < 20) fails.Add($"{name} {id}: {string.Join(",", named.Select(q => $"{q}:{quests[q]}"))}: shown {p.Shown} drawn {drawn[e]}, client {expect}");
                }
                // a "0"/"0" mask (the flat default) names no quest of its own: its table row was checked above, not listed
                if (!ownR.Any(r => r.A != 0 || r.B != 0)) { noQuest++; continue; }
                rows.Add($"{name} {id} \"{e.Get("MaskQuestID")}\"/\"{e.Get("MaskQuestState")}\" load {(e.Bool("IsVisible", true) ? "shown" : "hidden")}{(table != null ? " +table" : "")}" +
                         $"{(FieldData.EntryByXBlock(packs, name) == null ? " (no field's default xblock)" : "")}: {named.Count} quests, open in {open}/{cases.Count}");
            }
            draws.Detach();
        }
        TestContext.Out.WriteLine($"{env}: {maps} maps ({failedMaps} failed to load), {own} portals with a MaskQuestID ({noQuest} naming no quest of their own, {usable} usable, {withTable} with a table row), " +
                                  $"{stages} stage checks, {closedAtStart} closed with no quest ({loadShownClosed} shown in the xblock), " +
                                  $"{loadHiddenOpens} hidden in the xblock that open, {fails.Count} fail");
        foreach (var r in rows) TestContext.Out.WriteLine("  " + r);
        Assert.That(own, Is.GreaterThan(0));
        Assert.That(fails, Is.Empty);
    }
}
