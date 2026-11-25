using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every SelectItemBox item of the data set (needs MS2_DATA): its picks and its individual drop box as
/// UISelectItemBoxDialog::OnLoadComplete reads them (uiselectitemboxdialog.cpp:146-153, the arguments or the xml values), and
/// the box's rewards for each job group and gender.
/// </summary>
public class SelectItemBoxSweepTests
{
    /// <summary>The job groups (job 1 counts as 10 in ItemLimit::IsJobRecommended).</summary>
    static readonly int[] JobGroups = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    [Test]
    public void EverySelectItemBoxItemHasItsPicksAndARewardList()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var tables = new GameTables(packs, "en");
        int args = 0, xml = 0, noBox = 0, someJobsEmpty = 0;
        Dictionary<int, HashSet<string>> raw = null;
        var unavailable = new SortedDictionary<string, int>();
        var noRows = new List<string>();
        var bad = new List<string>();
        var samples = new List<string>();
        foreach (int id in ItemFunctionSweepTests.ItemIds(packs))
        {
            if (tables.Items.Get(id) is not { FunctionName: "SelectItemBox" } data) continue;
            if (data.HasFunctionXml) xml++; else args++;
            if (samples.Count < 4 && (data.HasFunctionXml || samples.Count < 2)) samples.Add($"{id}: {data.FunctionParameter}");
            int picks = data.SelectItemBoxCount, box = data.SelectItemBoxDropBoxId;
            if (picks <= 0 || box <= 0 || picks > SelectItemBoxState.SlotCount) { bad.Add($"{id} '{data.FunctionParameter}': picks {picks}, box {box}"); continue; }
            if (!tables.IndividualDrops.GetBoxItems(tables.Items, box, 10, 0, false, new List<ItemDropEntry>()))
            {
                // the client's GetBoxItems fails the same way, and its list shows only the empty rows, when the data has no
                // row of the box or every row is another locale's or behind an off feature (IsElementAvailable,
                // commonutil.cpp:1047); a box with available rows that did not load is the port's gap
                var why = (raw ??= RawRows(packs)).TryGetValue(box, out var rows) ? rows : null;
                if (why == null) { noRows.Add($"{id}: box {box}"); continue; }
                if (why.Contains("")) bad.Add($"{id}: box {box} has available rows but was not loaded");
                else { noBox++; foreach (var w in why) unavailable[w] = unavailable.GetValueOrDefault(w) + 1; }
                continue;
            }
            int empty = 0;
            foreach (int job in JobGroups)
                for (int gender = 0; gender < 2; gender++)
                    if (SelectItemBoxController.BoxRewards(tables, box, job, gender).Count == 0) empty++;
            if (empty == JobGroups.Length * 2) bad.Add($"{id}: box {box} lists nothing for any job and gender");
            else if (empty > 0) someJobsEmpty++;
        }
        TestContext.Out.WriteLine($"SelectItemBox items: {args} with arguments, {xml} with xml values; {noBox} name a box whose rows are all " +
                                  $"unavailable here (an empty list, as in the client), {someJobsEmpty} list nothing for some job or gender; {bad.Count} unusable");
        foreach (var (w, n) in unavailable) TestContext.Out.WriteLine($"  rows unavailable: {w} ({n} boxes)");
        TestContext.Out.WriteLine($"  {noRows.Count} name a box the client data has no row of (an empty list, as in the client): " +
                                  string.Join(", ", noRows.Take(5)) + (noRows.Count > 5 ? ", ..." : ""));
        foreach (var s in samples) TestContext.Out.WriteLine("  " + s);
        foreach (var b in bad.Take(60)) TestContext.Out.WriteLine("  unusable " + b);
        Assume.That(args + xml, Is.GreaterThan(0), "no SelectItemBox item");
        Assert.That(bad, Is.Empty);
    }

    /// <summary>Every row of every individual drop box in the data, keyed by box: "" for a row IsElementAvailable keeps, else why
    /// not ("locale X" / "feature X"). KMS: individualitemdrop_final.xml's box, group and v elements; GMS2: the flat
    /// individualDropBox rows of every table/individualitemdrop*.xml and its region folder's.</summary>
    static Dictionary<int, HashSet<string>> RawRows(PackSet packs)
    {
        var rows = new Dictionary<int, HashSet<string>>();
        string Why(params System.Xml.Linq.XElement[] path)
        {
            foreach (var e in path)
            {
                string locale = (string)e.Attribute("locale") ?? "", feature = (string)e.Attribute("feature") ?? "";
                if (locale != "" && locale != LobbyFeatures.LocaleOf(packs)) return "locale " + locale;
                if (!LobbyFeatures.IsEnabledByName(packs, feature)) return "feature " + feature;
            }
            return "";
        }
        void Add(int box, string why) { if (!rows.TryGetValue(box, out var set)) rows[box] = set = new(); set.Add(why); }
        if (packs.Exists("Xml", "table/individualitemdrop_final.xml"))
        {
            foreach (var b in GameStrings.Parse(packs.Read("Xml", "table/individualitemdrop_final.xml")).Root.Elements("dropBox"))
                foreach (var g in b.Elements("group"))
                    foreach (var v in g.Elements("v"))
                        Add(ItemData.Atoi((string)b.Attribute("dropBoxID") ?? "0"), Why(b, g, v));
            return rows;
        }
        foreach (var name in packs.List("Xml").Where(n => n.StartsWith("table/") && n.Contains("/individualitemdrop") && n.EndsWith(".xml")))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", name)).Root.Elements("individualDropBox"))
                Add(ItemData.Atoi((string)e.Attribute("individualDropBoxID") ?? "0"), Why(e) is "" && name.Count(c => c == '/') > 1
                    && !name.StartsWith("table/" + TablePath.Region(n => packs.Exists("Xml", n)) + "/") ? "region folder " + name.Split('/')[1] : Why(e));
        return rows;
    }
}
