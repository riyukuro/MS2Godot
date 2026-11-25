using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G29 — NPC/quest script screen effects (C17): GMS2 script/quest/questscript_eventkr.xml has content rows with
/// screenEffectMovie="mask_black" screenEffectAction="fadeout" screenEffectValue="1000"; NpcScriptTable.Content reads them
/// (CCinematicContent::vfunc_2 @0x14078de70, scriptcontent.cpp:268-270).
/// KMS script/quest/questscript_final.xml has the same attributes (quest 10003330, value 2000).
/// </summary>
public class RcG29NpcScriptScreenEffectTests
{
    static XElement FirstContent(PackSet packs, string entry)
    {
        var doc = GameStrings.Parse(packs.Read("Xml", entry));
        return doc.Descendants("content").FirstOrDefault(c => (string)c.Attribute("screenEffectMovie") != null);
    }

    [Test]
    public void TheKmsContentKeepsItsTextAndSpeaker()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var c = FirstContent(packs, "script/quest/questscript_final.xml");
        Assert.That(c, Is.Not.Null, "kms script/quest/questscript_final.xml has a screen-effect content");
        var ct = NpcScriptTable.Content.Parse(c);
        Assert.That(ct.Text, Is.Not.Empty);
        Assert.That(ct.SpeakerNpc, Is.EqualTo(11004430));
    }

    [Test]
    public void TheKmsScreenEffectAttributesAreRead()
    {
        using var kms = new PackSet(RcGap.KmsData());
        var kc = NpcScriptTable.Content.Parse(FirstContent(kms, "script/quest/questscript_final.xml"));
        Assert.That((kc.ScreenEffectMovie, kc.ScreenEffectAction, kc.ScreenEffectValue), Is.EqualTo(("mask_black", "fadeout", 2000)), "kms quest 10003330");
    }

    [Test]
    public void TheGms2ScreenEffectAttributesAreRead()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var gc = NpcScriptTable.Content.Parse(FirstContent(packs, "script/quest/questscript_eventkr.xml"));
        Assert.That((gc.ScreenEffectMovie, gc.ScreenEffectAction, gc.ScreenEffectValue), Is.EqualTo(("mask_black", "fadeout", 1000)));
    }

    /// <summary>
    /// Every &lt;content&gt; of every script file (script/npc and script/quest) on MS2_DATA and MS2_GMS2_DATA: the three
    /// screen effect attributes read as the client's XmlGetAttr does (scriptcontent.cpp:268-270, the value unsigned), and a
    /// page with an action or value also names the movie, since CCinematicComponent::vfunc_5 (cinematiccomponent.cpp:323)
    /// passes the effect only when the movie is set. Prints the effects by data set.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryScriptContentReadsItsScreenEffect(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var files = packs.List("Xml").Where(e => e.StartsWith("script/", StringComparison.OrdinalIgnoreCase) && e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(files, Is.Not.Empty);
        int contents = 0;
        var effects = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var f in files)
            foreach (var c in GameStrings.Parse(packs.Read("Xml", f)).Descendants("content"))
            {
                contents++;
                string movie = (string)c.Attribute("screenEffectMovie"), action = (string)c.Attribute("screenEffectAction"), value = (string)c.Attribute("screenEffectValue");
                if (movie == null && action == null && value == null) continue;
                var ct = NpcScriptTable.Content.Parse(c);
                if (ct.ScreenEffectMovie != movie || ct.ScreenEffectAction != action) bad.Add($"{f}: {movie}/{action} read as {ct.ScreenEffectMovie}/{ct.ScreenEffectAction}");
                if (value != null && (!uint.TryParse(value, out uint v) || v != (uint)ct.ScreenEffectValue)) bad.Add($"{f}: value {value} read as {ct.ScreenEffectValue}");
                if (string.IsNullOrEmpty(movie)) bad.Add($"{f}: an action/value without a movie");
                string key = $"{movie} {action} {value}";
                effects[key] = effects.GetValueOrDefault(key) + 1;
            }
        TestContext.WriteLine($"{variable}: {files.Count} script files, {contents} contents, {effects.Values.Sum()} with a screen effect");
        foreach (var (k, n) in effects) TestContext.WriteLine($"  {k}: {n}");
        Assert.That(effects, Is.Not.Empty, "the data has screen effects");
        Assert.That(bad, Is.Empty);
    }
}
