using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The trigger sweep's action list (<see cref="TriggerSweepRoutes"/>, game/tests) against the trigger xmls (Xml.m2d trigger/)
/// of a data set: every action there, by its name and, for an action the Maple2 server splits by an attribute
/// (TriggerDefinitionOverride's FunctionSplitter), that attribute's value ("(absent)" when it has none), is a row of the
/// list, so the headless sweep (res://tests/trigger_sweep.tscn) covers it. A new action or variant fails here until the list
/// is regenerated. With MS2_SWEEP_OUT set it writes the uses per row there (uses-&lt;variable&gt;.tsv).
/// </summary>
public class TriggerSweepTests
{
    static readonly Dictionary<string, string> SplitAttr = TriggerSweepRoutes.Actions.Where(a => a.Split.Length > 0)
        .GroupBy(a => a.Raw, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Split.Split('=')[0], StringComparer.Ordinal);

    [Test]
    public void EachRowIsOneNameAndVariantAndASplitNameSplitsByOneAttribute()
    {
        Assert.That(TriggerSweepRoutes.Actions.GroupBy(a => (a.Raw, a.Split)).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty);
        foreach (var a in TriggerSweepRoutes.Actions.Where(a => SplitAttr.ContainsKey(a.Raw)))
            Assert.That(a.Split, Does.StartWith(SplitAttr[a.Raw] + "="), a.Raw + " has a row without its split");
    }

    static (string Raw, string Split) Key(System.Xml.Linq.XElement a)
    {
        var raw = (string)a.Attribute("name") ?? "";
        if (!SplitAttr.TryGetValue(raw, out var attr)) return (raw, "");
        return (raw, attr + "=" + ((string)a.Attribute(attr) ?? "(absent)"));
    }

    static void EveryAction(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var rows = TriggerSweepRoutes.Actions.ToDictionary(a => (a.Raw, a.Split));
        var uses = new Dictionary<(string, string), int>();
        var unknown = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int files = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
            files++;
            foreach (var a in doc.Descendants("action"))
            {
                var k = Key(a);
                if (rows.ContainsKey(k)) uses[k] = uses.GetValueOrDefault(k) + 1;
                else unknown.TryAdd(k.Split.Length > 0 ? $"{k.Raw} {k.Split}" : k.Raw, entry);
            }
        }
        var table = new List<string> { "raw\tsplit\taction\troute\tuses" };
        table.AddRange(TriggerSweepRoutes.Actions.Select(a => $"{a.Raw}\t{a.Split}\t{a.Name}\t{a.Route}\t{uses.GetValueOrDefault((a.Raw, a.Split))}"));
        if (Environment.GetEnvironmentVariable("MS2_SWEEP_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, $"uses-{variable}.tsv"), table);
        }
        TestContext.WriteLine($"{variable}: {files} trigger files, {uses.Values.Sum()} actions, {uses.Count} of {rows.Count} rows used, "
            + $"{TriggerSweepRoutes.Actions.Where(a => uses.ContainsKey((a.Raw, a.Split))).Select(a => a.Name).Distinct().Count()} server actions");
        Assert.That(unknown.Select(kv => $"{kv.Key} ({kv.Value})"), Is.Empty, "actions the sweep list does not have");
        Assert.That(uses.Count, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsAction() => EveryAction("MS2_DATA");
    [Test] public void EveryGms2Action() => EveryAction("MS2_GMS2_DATA");

    /// <summary>A row neither data set uses is stale (the list is made from the uses of both).</summary>
    [Test]
    public void EveryRowIsUsedOnOneDataSet()
    {
        var used = new HashSet<(string, string)>();
        foreach (var variable in new[] { "MS2_DATA", "MS2_GMS2_DATA" })
        {
            using var packs = new PackSet(variable == "MS2_DATA" ? RcGap.KmsData() : RcGap.Data(variable));
            foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)))
            {
                System.Xml.Linq.XDocument doc;
                try { doc = PackXml.Parse(packs.Read("Xml", entry)); } catch (System.Xml.XmlException) { continue; }
                foreach (var a in doc.Descendants("action")) used.Add(Key(a));
            }
        }
        Assert.That(TriggerSweepRoutes.Actions.Where(a => !used.Contains((a.Raw, a.Split))).Select(a => $"{a.Raw} {a.Split}"), Is.Empty);
    }
}
