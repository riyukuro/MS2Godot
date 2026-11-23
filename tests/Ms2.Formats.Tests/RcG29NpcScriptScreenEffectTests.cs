using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G29 — NPC/quest script screen effects (C17): GMS2 script/quest/questscript_eventkr.xml has content rows with
/// screenEffectMovie="mask_black" screenEffectAction="fadeout" screenEffectValue="1000"; NpcScriptTable.Content reads none.
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
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var c = FirstContent(packs, "script/quest/questscript_final.xml");
        Assert.That(c, Is.Not.Null, "kms script/quest/questscript_final.xml has a screen-effect content");
        var ct = NpcScriptTable.Content.Parse(c);
        Assert.That(ct.Text, Is.Not.Empty);
        Assert.That(ct.SpeakerNpc, Is.EqualTo(11004430));
    }

    [Test]
    [Ignore("G29: NpcScriptTable.Content does not read screenEffectMovie/screenEffectAction/screenEffectValue yet")]
    public void TheScreenEffectAttributesAreRead()
    {
        using var kms = new PackSet(RcGap.Data("MS2_DATA"));
        var kc = NpcScriptTable.Content.Parse(FirstContent(kms, "script/quest/questscript_final.xml"));
        Assert.That(RcGap.Member<string>(kc, "ScreenEffectMovie"), Is.EqualTo("mask_black"));
        Assert.That(RcGap.Member<string>(kc, "ScreenEffectAction"), Is.EqualTo("fadeout"));
        Assert.That(RcGap.Member<int>(kc, "ScreenEffectValue"), Is.EqualTo(2000), "kms quest 10003330");

        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var gc = NpcScriptTable.Content.Parse(FirstContent(packs, "script/quest/questscript_eventkr.xml"));
        Assert.That(RcGap.Member<string>(gc, "ScreenEffectMovie"), Is.EqualTo("mask_black"));
        Assert.That(RcGap.Member<string>(gc, "ScreenEffectAction"), Is.EqualTo("fadeout"));
        Assert.That(RcGap.Member<int>(gc, "ScreenEffectValue"), Is.EqualTo(1000));
    }
}
