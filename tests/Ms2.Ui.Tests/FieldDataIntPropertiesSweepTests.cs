using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every int property FieldData reads, over every field of the data of MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2),
/// against the client's read: XmlGetAttr of an int is TiXmlElement::QueryIntAttribute (tinyxmlhelper.cpp:151; sscanf "%d",
/// tinyxml.cpp:1160), so a value with no leading number keeps the ctor's default; the defaults are CFieldData's
/// (fielddata.cpp:543-556) and CFieldDataCli's (fielddatacli.cpp:67-79). The elements are the selected environment's
/// &lt;property&gt; (fielddata.cpp:662-708), &lt;drop&gt; (:723-726), &lt;split&gt; (:730-731), &lt;cashCall&gt; (:734-741), &lt;ui&gt;
/// (fielddatacli.cpp:218-232) and &lt;shadow&gt; (FUN_140a56ad0, :280). The int members the client tests for nonzero are bools here.
/// The death-state properties are in <see cref="RevivalDataSweepTests"/>.
/// </summary>
[NonParallelizable]
public class FieldDataIntPropertiesSweepTests
{
    /// <summary>QueryIntAttribute: the leading signed digits after white space; no attribute or no number keeps <paramref name="fallback"/>.</summary>
    static int Query(XElement e, string name, int fallback)
    {
        if ((string)e?.Attribute(name) is not { } s) return fallback;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        int start = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i++] - '0'); if (v > int.MaxValue + 1L) return fallback; }
        if (i == start) return fallback;
        return (int)(neg ? -v : v);
    }

    sealed record Prop(string Element, string Name, int Default, Func<FieldData, int> Ours);

    static int B(bool b) => b ? 1 : 0;

    static readonly Prop[] Props =
    {
        new("property", "checkClimb", 1, f => B(f.CheckClimb)),
        new("property", "checkFly", 0, f => B(f.CheckFly)),
        new("property", "checkPartyObserve", 0, f => B(f.CheckPartyObserve)),
        new("property", "mapType", 0, f => f.MapType),
        new("property", "capacity", 0, f => f.Capacity),
        new("property", "pkMode", 0, f => f.PkMode),
        new("property", "continentCode", 0, f => f.ContinentCode),
        new("property", "requireQuest", 0, f => f.RequireQuest),
        new("property", "tutorialType", 0, f => f.TutorialType),
        new("property", "channelGroupID", 0, f => f.ChannelGroupId),
        new("drop", "maplevel", 0, f => f.MapLevel),
        new("split", "globalSplit", 0, f => B(f.GlobalSplit)),
        new("cashCall", "RecallOtherUserProhibit", 0, f => B(f.RecallOtherUserProhibit)),
        new("ui", "useEPSkill", 1, f => B(f.UseEPSkill)),
        new("ui", "useRidee", 1, f => B(f.UseRidee)),
        new("ui", "usePet", 1, f => B(f.UsePet)),
        new("ui", "vscroll", 1, f => f.VScroll),
        new("ui", "hscroll", 1, f => f.HScroll),
        new("ui", "instancingGrid", 0, f => f.InstancingGrid),
        new("shadow", "optionHighLevel", 0, f => f.ShadowOptionHighLevel),
    };

    static readonly HashSet<string> Bools = new() { "checkClimb", "checkFly", "checkPartyObserve", "globalSplit", "RecallOtherUserProhibit", "useEPSkill", "useRidee", "usePet" };

    /// <summary>The values the data sets do not hold today: each property set to each value in one synthetic row; the
    /// strict parse it replaced read "" and "x" as 0 (or true for the "!= 0" flags) and "1.0" / " 2" / "3abc" as 0.</summary>
    [TestCase("")] [TestCase("x")] [TestCase("1.0")] [TestCase(" 2")] [TestCase("3abc")] [TestCase("00")] [TestCase("-4")] [TestCase("+5")]
    public void EachIntPropertyReadsAnOddValueAsTheClient(string value)
    {
        var problems = new List<string>();
        foreach (var p in Props)
        {
            var target = new XElement(p.Element, new XAttribute(p.Name, value));
            var env = new XElement("environment", new XElement("xblock", new XAttribute("name", "x")), target);
            var row = new XElement("fieldData", new XAttribute("id", 1), env);
            int want = Query(target, p.Name, p.Default);
            if (Bools.Contains(p.Name)) want = want != 0 ? 1 : 0;
            int got = p.Ours(FieldData.FromEntry(row));
            if (got != want) problems.Add($"{p.Element} {p.Name}=\"{value}\": {got}, the client reads {want}");
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFieldsIntPropertiesReadAsTheClientReadsThem(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var rows = new Dictionary<int, XElement>();
        foreach (var fd in FieldData.Document(packs).Root!.Elements("fieldData"))
        {
            var env = FieldData.DefaultEnvironment(fd);
            if (env?.Element("xblock")?.Attribute("name") == null) continue;
            if ((int?)fd.Attribute("id") is int id) rows[id] = fd;
        }
        Assert.That(rows, Is.Not.Empty);

        var problems = new List<string>();
        var byProp = new Dictionary<string, int>();
        int present = 0;
        foreach (var (id, fd) in rows.OrderBy(r => r.Key))
        {
            var env = FieldData.DefaultEnvironment(fd);
            var ours = FieldData.FindById(packs, id);
            foreach (var p in Props)
            {
                var e = p.Element == "property" ? env.Element("property") ?? fd.Element("property") : env.Element(p.Element);
                if (e?.Attribute(p.Name) != null) present++;
                int want = Query(e, p.Name, p.Default);
                if (Bools.Contains(p.Name)) want = want != 0 ? 1 : 0;
                int got = p.Ours(ours);
                if (got == want) continue;
                problems.Add($"field {id}: {p.Element} {p.Name}=\"{(string)e?.Attribute(p.Name)}\" is {got}, the client reads {want}");
                byProp[p.Name] = byProp.GetValueOrDefault(p.Name) + 1;
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {rows.Count} fields x {Props.Length} properties ({present} attributes present), {problems.Count} differences" +
            (byProp.Count > 0 ? ": " + string.Join(", ", byProp.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")) : ""));
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }
}
