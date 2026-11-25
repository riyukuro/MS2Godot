using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G12 — map properties C16 (CFieldData::vfunc_1 @0x140374530, fielddata.cpp:651-750): gms/map/00000001.xml has property
/// enterMinLevel="0" enterMaxLevel="0" enterAdminLevel="0" limitContent="0" exploreType="0", drop dropRank=""
/// globalDropBoxID="" and cashCall cashTaxiNotDestination="1". KMS table/fielddata.xml id 1 has none of them (no
/// cashCall), so it keeps the ctor's zeros.
/// </summary>
public class RcG12MapPropertiesTests
{
    [Test]
    public void TheKmsFieldKeepsItsMapTypeAndCapacity()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null);
        Assert.That((f.MapType, f.Capacity), Is.EqualTo((0, 0)));
        if (!RcGap.IsKr(packs)) return;
        Assert.That((f.EnterMinLevel, f.EnterMaxLevel, f.EnterAdminLevel, f.LimitContent, f.ExploreType), Is.EqualTo(((short)0, (short)0, 0, 0u, 0)));
        Assert.That((f.CashTaxiNotDeparture, f.CashTaxiNotDestination, f.CashCallMedicProhibit, f.CashCallMarketProhibit), Is.EqualTo((false, false, false, false)));
    }

    [Test]
    public void TheGms2LoginMapReadsTheC16Properties()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null, "gms/map/00000001.xml");
        Assert.That(f.EnterMinLevel, Is.EqualTo(0));
        Assert.That(f.EnterMaxLevel, Is.EqualTo(0));
        Assert.That(f.EnterAdminLevel, Is.EqualTo(0));
        Assert.That(f.LimitContent, Is.EqualTo(0u));
        Assert.That(f.ExploreType, Is.EqualTo(0));
        Assert.That(f.DropRank, Is.EqualTo(0u), "dropRank=\"\": the unsigned query stores 0");
        Assert.That(f.GlobalDropBoxIds, Is.Empty);
        Assert.That(f.CashTaxiNotDeparture, Is.False);
        Assert.That(f.CashTaxiNotDestination, Is.True);
        Assert.That(f.CashCallMedicProhibit, Is.False);
        Assert.That(f.CashCallMarketProhibit, Is.False);
        Assert.That(f.HasLimitContent(uint.MaxValue), Is.False);
    }

    /// <summary>
    /// Every field of the data reads its C16 properties as the client does: each attribute of the default environment's
    /// &lt;property&gt;, &lt;drop&gt; and &lt;cashCall&gt; is a whole number or empty in both data sets, so the field holds that
    /// number (0 for empty or absent, the ctor's default), the list holds globalDropBoxID's tokens, and HasLimitContent is
    /// true for exactly the bits of limitContent.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFieldReadsItsC16Properties(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        int fields = 0, set = 0;
        var wrong = new List<string>();
        foreach (var fd in FieldData.Document(packs).Root.Elements("fieldData"))
        {
            var f = FieldData.FromEntry(fd);
            if (f == null) continue;
            fields++;
            var env = FieldData.DefaultEnvironment(fd);
            var prop = env.Element("property") ?? fd.Element("property");
            var drop = env.Element("drop");
            var cash = env.Element("cashCall");
            long V(XElement e, string a)
            {
                var s = (string)e?.Attribute(a);
                if (string.IsNullOrEmpty(s)) return 0;
                if (!long.TryParse(s, out long v)) { wrong.Add($"{f.Id} {a}=\"{s}\" is not a whole number"); return 0; }
                return v;
            }
            void Check(string name, long have, long want)
            {
                if (have != want) wrong.Add($"{f.Id} {name} {have}, data {want}");
                if (want != 0) set++;
            }
            Check("enterAdminLevel", f.EnterAdminLevel, V(prop, "enterAdminLevel"));
            Check("enterMinLevel", f.EnterMinLevel, V(prop, "enterMinLevel"));
            Check("enterMaxLevel", f.EnterMaxLevel, V(prop, "enterMaxLevel"));
            Check("recoveryFullHP", f.RecoveryFullHP, V(prop, "recoveryFullHP"));
            Check("limitContent", f.LimitContent, V(prop, "limitContent"));
            Check("exploreType", f.ExploreType, V(prop, "exploreType"));
            Check("dropRank", f.DropRank, V(drop, "dropRank"));
            Check("cashTaxiNotDeparture", f.CashTaxiNotDeparture ? 1 : 0, V(cash, "cashTaxiNotDeparture"));
            Check("cashTaxiNotDestination", f.CashTaxiNotDestination ? 1 : 0, V(cash, "cashTaxiNotDestination"));
            Check("cashCallMedicProhibit", f.CashCallMedicProhibit ? 1 : 0, V(cash, "cashCallMedicProhibit"));
            Check("cashCallMarketProhibit", f.CashCallMarketProhibit ? 1 : 0, V(cash, "cashCallMarketProhibit"));
            var ids = ((string)drop?.Attribute("globalDropBoxID") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(uint.Parse).ToArray();
            if (!f.GlobalDropBoxIds.SequenceEqual(ids)) wrong.Add($"{f.Id} globalDropBoxID [{string.Join(",", f.GlobalDropBoxIds)}], data [{string.Join(",", ids)}]");
            for (int bit = 0; bit < 32; bit++)
                if (f.HasLimitContent(1u << bit) != ((f.LimitContent >> bit & 1) != 0)) wrong.Add($"{f.Id} HasLimitContent bit {bit}");
        }
        TestContext.Out.WriteLine($"{variable}: {fields} fields, {set} C16 values set");
        foreach (var w in wrong.Take(20)) TestContext.Out.WriteLine(w);
        Assert.That(fields, Is.GreaterThan(1000));
        Assert.That(set, Is.GreaterThan(1000));
        Assert.That(wrong, Is.Empty);
    }
}
