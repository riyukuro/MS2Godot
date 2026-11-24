using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CCoupleEmotionTable::Load / GetAni / Validate (coupleemotiontable.cpp) and CoupleEmotionAnimInfo::Init
/// (statecoupleemotion.cpp:217-245), and the sweep of every couple emotion over the data.</summary>
public class CoupleEmotionTableTests
{
    static Dictionary<uint, CoupleEmotionTable.Data> Parse(string rows, out int invalid) =>
        CoupleEmotionTable.Parse(XElement.Parse("<ms2>" + rows + "</ms2>"), out invalid);

    const string Hug = "<coupleEmotion code=\"100001\" animationDiffGender_loop=\"1\""
        + " animationDiffGender_male=\"[M_Ready, M_Loop]\" animationDiffGender_female=\"F_Ready,F_Loop\""
        + " animationDiffGender_pathNif=\"D_Ready.nif,D_Loop.nif\""
        + " animationSameGenderMale_requestUser=\"MB\" animationSameGenderMale_targetUser=\"MA\" animationSameGenderMale_pathNif=\"S_M.nif\""
        + " animationSameGenderFemale_loop=\"TRUE\" animationSameGenderFemale_requestUser=\"FB,,\" animationSameGenderFemale_targetUser=\"FA\""
        + " animationSameGenderFemale_pathNif=\"S_F.nif\" teleport_effect=\"CH/CoupleAction/Eff_CoupleAction _Start.xml\" />";

    [Test]
    public void TheRowKeepsEachPairingsLists()
    {
        var rows = Parse(Hug, out int invalid);
        Assert.That(invalid, Is.EqualTo(0));
        var d = rows[100001];
        // Remove('['), Remove(']'), Remove(' ') then Tokenize at ",": the brackets and spaces go, empty parts are skipped
        var diff = d.GetAni(0, 1);
        Assert.That(diff.RequestAnis, Is.EqualTo(new[] { "F_Ready", "F_Loop" }));
        Assert.That(diff.TargetAnis, Is.EqualTo(new[] { "M_Ready", "M_Loop" }));
        Assert.That(diff.NifPaths, Is.EqualTo(new[] { "D_Ready.nif", "D_Loop.nif" }));
        Assert.That(diff.Loop, Is.True);
        // GetAni swaps the genders so the smaller is first
        Assert.That(d.GetAni(1, 0), Is.SameAs(diff));
        var male = d.GetAni(0, 0);
        Assert.That((male.RequestAnis.Single(), male.TargetAnis.Single(), male.NifPaths.Single(), male.Loop), Is.EqualTo(("MB", "MA", "S_M.nif", false)));
        var female = d.GetAni(1, 1);
        Assert.That((female.RequestAnis.Single(), female.Loop), Is.EqualTo(("FB", true)));
        // teleport_effect as it is, inner space kept
        Assert.That(d.TeleportEffect, Is.EqualTo("CH/CoupleAction/Eff_CoupleAction _Start.xml"));
        Assert.That(d.All().Select(x => (x.Gender1, x.Gender2)), Is.EqualTo(new[] { (0, 1), (0, 0), (1, 1) }));
    }

    [Test]
    public void ARowWhoseListsDifferInCountIsLeftOut()
    {
        // ValidateAni: the nif count against the request count, then the request count against the target count
        string fewerNifs = "<coupleEmotion code=\"2\" animationDiffGender_male=\"a,b\" animationDiffGender_female=\"c,d\" animationDiffGender_pathNif=\"x.nif\" />";
        string fewerTargets = "<coupleEmotion code=\"3\" animationSameGenderMale_requestUser=\"a,b\" animationSameGenderMale_targetUser=\"c\" animationSameGenderMale_pathNif=\"x.nif,y.nif\" />";
        // a pairing with no lists at all is valid (three empty lists)
        string empty = "<coupleEmotion code=\"4\" />";
        var rows = Parse(fewerNifs + fewerTargets + empty, out int invalid);
        Assert.That(invalid, Is.EqualTo(2));
        Assert.That(rows.Keys, Is.EqualTo(new uint[] { 4 }));
        Assert.That(rows[4].GetAni(0, 1).RequestAnis, Is.Empty);
        Assert.That(rows[4].TeleportEffect, Is.Null);
    }

    [Test]
    public void ALaterRowOfACodeReplacesTheEarlier()
    {
        var rows = Parse("<coupleEmotion code=\"5\" animationSameGenderMale_loop=\"1\" />"
            + "<coupleEmotion code=\"5\" animationSameGenderMale_loop=\"0\" />", out _);
        Assert.That(rows[5].GetAni(0, 0).Loop, Is.False);
        // no <ms2> root: no rows
        Assert.That(CoupleEmotionTable.Parse(XElement.Parse("<table><coupleEmotion code=\"1\"/></table>"), out _), Is.Empty);
    }

    [Test]
    public void InitPicksTheActorsNodeAndList()
    {
        var t = new TestTable(Hug);
        // genders differ: the female (gender 1) is "B" with the request list whatever the swap flag says
        var f = t.Init(100001, 1, 0, false);
        Assert.That((f.OwnerNode, f.PeerNode, f.AniNames[0]), Is.EqualTo(("B", "A", "F_Ready")));
        var m = t.Init(100001, 0, 1, true);
        Assert.That((m.OwnerNode, m.PeerNode, m.AniNames[0]), Is.EqualTo(("A", "B", "M_Ready")));
        // same genders: the swap flag (IsRequester) picks "B" and the request list
        var req = t.Init(100001, 0, 0, true);
        Assert.That((req.OwnerNode, req.AniNames.Single()), Is.EqualTo(("B", "MB")));
        var tgt = t.Init(100001, 0, 0, false);
        Assert.That((tgt.OwnerNode, tgt.AniNames.Single()), Is.EqualTo(("A", "MA")));
        Assert.That(t.Init(100002, 0, 0, false), Is.Null);
        // GetAniName out of range is "", GetLastAni the last index, Load's path under Resource/Model/Path
        Assert.That((f.AniName(-1), f.AniName(2), f.LastAni), Is.EqualTo(("", "", 1)));
        Assert.That(f.NifPath(1), Is.EqualTo("Data/Resource/Model/Path/D_Loop.nif"));
        Assert.That(f.NifPath(2), Is.Null);
    }

    sealed class TestTable
    {
        readonly Dictionary<uint, CoupleEmotionTable.Data> rows;
        public TestTable(string xml) { rows = Parse(xml, out _); }
        public CoupleEmotionTable.AnimInfo Init(uint id, int g1, int g2, bool swap) =>
            CoupleEmotionTable.Init(rows.GetValueOrDefault(id), g1, g2, swap);
    }

    /// <summary>
    /// Every coupleEmotion row of the data: kept by Validate; each pairing's path nifs are in Resource/Model/Path and hold
    /// the "A" and "B" nodes Load looks up (Eff_center is optional: Load falls back to the owner node); each actor's
    /// animations are body sequences of the body that plays them (the different-gender request list on the female body,
    /// the target list on the male body; the same-gender lists on that body); and every couple emotion skill (main type
    /// 0xf) has a row for its param1.
    /// </summary>
    static void EveryCoupleEmotionPlays(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var tables = new GameTables(packs, "en");
        var table = tables.CoupleEmotions;
        var rows = table.Rows.OrderBy(r => r.Id).ToList();
        string nx = NifXmlPath();
        var spec = nx == null ? null : new Ms2.Formats.Nif.NifSpec(nx);
        var noNif = new SortedSet<string>(StringComparer.Ordinal);
        var noNode = new SortedSet<string>(StringComparer.Ordinal);
        var noCenter = new SortedSet<string>(StringComparer.Ordinal);
        var noSeq = new SortedSet<string>(StringComparer.Ordinal);
        var nifs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        int sets = 0, anis = 0;
        foreach (var d in rows)
            foreach (var (g1, g2, a) in d.All())
            {
                sets++;
                foreach (var nif in a.NifPaths)
                {
                    string entry = nif.ToLowerInvariant();
                    if (!packs.Exists("Resource/Model/Path", entry)) { noNif.Add($"{d.Id}:{nif}"); continue; }
                    if (spec == null) continue;
                    if (!nifs.TryGetValue(entry, out var names))
                    {
                        var file = Ms2.Formats.Nif.NifFile.Load(spec, packs.Read("Resource/Model/Path", entry));
                        names = new HashSet<string>(StringComparer.Ordinal);
                        for (int i = 0; i < file.Blocks.Count; i++)
                            if (file.Blocks[i].Type.EndsWith("Node", StringComparison.Ordinal) && file.Name(i) is { } n) names.Add(n);
                        nifs[entry] = names;
                    }
                    foreach (var node in new[] { CoupleEmotionTable.NodeA, CoupleEmotionTable.NodeB })
                        if (!names.Contains(node)) noNode.Add($"{nif}:{node}");
                    if (!names.Contains(CoupleEmotionTable.CenterNode)) noCenter.Add(nif);
                }
                // the body each list plays on: Init gives the request list to "B" (the female when the genders differ)
                string requestBody = g1 != g2 ? "female" : g1 == 0 ? "male" : "female";
                string targetBody = g1 != g2 ? "male" : requestBody;
                foreach (var (body, list) in new[] { (requestBody, a.RequestAnis), (targetBody, a.TargetAnis) })
                    foreach (var name in list)
                    {
                        anis++;
                        if (!BodySequences.Exists(packs, body, name)) noSeq.Add($"{d.Id}:{body}/{name}");
                    }
            }
        var couples = tables.Skills.Skills.Rows.Where(s => s.MainType == 0xf).OrderBy(s => s.Id).ToList();
        var noRow = couples.Where(s => table.GetData(unchecked((uint)s.Param1)) == null).Select(s => $"{s.Id}:{s.Param1}").ToList();
        TestContext.WriteLine($"{variable}: {rows.Count} rows ({table.InvalidCount} left out by Validate), {sets} sets, {anis} anis, "
            + $"{nifs.Count} nifs read; {couples.Count} couple skills; no nif: {Show(noNif)}; nif without A/B: {Show(noNode)}; "
            + $"nif without Eff_center: {Show(noCenter)}; no body sequence: {Show(noSeq)}; skill without row: {Show(noRow)}");
        Assert.That(rows, Has.Count.GreaterThan(20));
        Assert.That(table.InvalidCount, Is.EqualTo(0));
        Assert.That(couples, Has.Count.GreaterThan(20));
        Assert.That(noRow, Is.Empty);
        Assert.That(noNif, Is.Empty);
        Assert.That(noNode, Is.Empty);
        Assert.That(noSeq, Is.Empty);
    }

    static string Show(IEnumerable<string> s) => s.Any() ? string.Join(", ", s) : "none";

    [Test]
    public void EveryKmsCoupleEmotionPlays() => EveryCoupleEmotionPlays("MS2_DATA");

    [Test]
    public void EveryGms2CoupleEmotionPlays() => EveryCoupleEmotionPlays("MS2_GMS2_DATA");

    /// <summary>The couple skills both data sets have keep the same code: GMS2's &lt;kinds emotion&gt; is KMS's param1.</summary>
    [Test]
    public void TheGms2CoupleCodeIsTheKmsParam1()
    {
        using var kms = new PackSet(RcGap.Data("MS2_DATA"));
        using var gms = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var k = new GameTables(kms, "en").Skills.Skills.Rows.Where(s => s.MainType == 0xf).ToDictionary(s => s.Id, s => s.Param1);
        var g = new GameTables(gms, "en").Skills.Skills.Rows.Where(s => s.MainType == 0xf).ToDictionary(s => s.Id, s => s.Param1);
        var both = k.Keys.Intersect(g.Keys).OrderBy(i => i).ToList();
        var differ = both.Where(i => k[i] != g[i]).Select(i => $"{i}: {k[i]} / {g[i]}").ToList();
        TestContext.WriteLine($"{both.Count} couple skills in both; differ: {Show(differ)}");
        Assert.That(both, Has.Count.GreaterThan(20));
        Assert.That(differ, Is.Empty);
    }

    static string NifXmlPath()
    {
        var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(nx))
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "MS2Godot.sln"))) { nx = Path.Combine(d.FullName, "external", "nifxml", "nif.xml"); break; }
        return !string.IsNullOrEmpty(nx) && File.Exists(nx) ? nx : null;
    }
}
