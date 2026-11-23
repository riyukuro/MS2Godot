using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The additional-effect status attributes and their list sums (combat-buffs tasks 2, 3, 5 and 7). The client reads
/// &lt;Motion stun noFly skillUseDisable skillUseDisableIDs confusionMove soundPitch alphaBlending scale
/// ignoreCollisionGroups fixedZRotation&gt;, &lt;Basic itemSlotDisable&gt;, &lt;Consume hpConsumeRate spConsumeRate&gt;,
/// &lt;Shield hpValue hpByTargetMaxHP uiType uiTypeValue&gt; and &lt;UI bInfinitySP bVisibleItemOption iconBalloonOnOff&gt;
/// (additionalactionadditional.cpp:1045-1118, .h:259-288,468-471), then AdditionalCliList::FUN_140972a00 sums the mask,
/// alpha, scale, pitch, rotation and collision groups (additionalcli_2.cpp:1019-1157). Main's reader sees none of them.
/// </summary>
public class RcAdditionalStatusTests
{
    const string LevelXml =
        "<level level=\"1\">" +
        "<Basic itemSlotDisable=\"5\"/>" +
        "<Motion stun=\"1\" stunAnimation=\"Stun_A\" noFly=\"1\" skillUseDisable=\"1\" skillUseDisableIDs=\"100,200\"" +
        " confusionMove=\"1\" soundPitch=\"1.5\" alphaBlending=\"0.5\" scale=\"2\" ignoreCollisionGroups=\"1,2\"" +
        " fixedZRotation=\"90\"/>" +
        "<Consume hpConsumeRate=\"10\" spConsumeRate=\"20\"/>" +
        "<Shield hpValue=\"100\" hpByTargetMaxHP=\"50\" uiType=\"1\" uiTypeValue=\"2\"/>" +
        "<UI bInfinitySP=\"1\" bVisibleItemOption=\"1\" iconBalloonOnOff=\"1\"/>" +
        "</level>";

    static AdditionalLevelData Level() =>
        AdditionalData.Load(XElement.Parse("<additional id=\"7\" mainType=\"1\">" + LevelXml + "</additional>"), _ => true)
            .GetLevel(1);

    [Ignore("the status attributes are not read")]
    [Test]
    public void TheLevelReadsTheStatusAttributes()
    {
        var lv = Level();
        Assert.That(RcLane.Int(lv, "Stun", "StunFlag"), Is.Not.Zero);
        Assert.That(RcLane.MemberAny<string>(lv, "StunAnimation", "StunSequence"), Is.EqualTo("Stun_A"));
        Assert.That(RcLane.Int(lv, "NoFly", "NoFlyFlag"), Is.Not.Zero);
        Assert.That(RcLane.Int(lv, "SkillUseDisable", "SkillUseDisableFlag"), Is.Not.Zero);
        Assert.That(RcLane.MemberAny(lv, "SkillUseDisableIds", "SkillUseDisableIDs", "SkillUseDisableIdList").ToString(),
            Does.Contain("100").And.Contain("200"));
        Assert.That(RcLane.Int(lv, "ConfusionMove", "ConfusionMoveFlag"), Is.Not.Zero);
        Assert.That(RcLane.Float(lv, "SoundPitch"), Is.EqualTo(1.5f));
        Assert.That(RcLane.Float(lv, "AlphaBlending"), Is.EqualTo(0.5f));
        Assert.That(RcLane.Float(lv, "Scale", "ScaleMultiplier"), Is.EqualTo(2f));
        Assert.That(RcLane.MemberAny(lv, "IgnoreCollisionGroups").ToString(), Does.Contain("1").And.Contain("2"));
        Assert.That(RcLane.Float(lv, "FixedZRotation"), Is.EqualTo(90f));
        Assert.That(RcLane.Int(lv, "ItemSlotDisable", "ItemSlotDisabled"), Is.EqualTo(5));
        Assert.That(RcLane.Int(lv, "HpConsumeRate"), Is.EqualTo(10));
        Assert.That(RcLane.Int(lv, "SpConsumeRate"), Is.EqualTo(20));
        Assert.That(RcLane.Int(lv, "ShieldHpValue", "Shield.Value"), Is.EqualTo(100));
        Assert.That(RcLane.Int(lv, "ShieldHpByTargetMaxHp", "Shield.ByTargetMaxHp"), Is.EqualTo(50));
        Assert.That(RcLane.Int(lv, "ShieldUiType", "Shield.UiType"), Is.EqualTo(1));
        Assert.That(RcLane.Int(lv, "ShieldUiTypeValue", "Shield.UiTypeValue"), Is.EqualTo(2));
        Assert.That(RcLane.Bool(lv, "InfinitySp", "BInfinitySp", "InfinitySP"), Is.True);
        Assert.That(RcLane.Bool(lv, "VisibleItemOption", "BVisibleItemOption"), Is.True);
        Assert.That(RcLane.Bool(lv, "IconBalloonOnOff"), Is.True);
    }

    [Ignore("the list sums are not computed")]
    [Test]
    public void TheListSumsTheStatusAttributes()
    {
        var lv = Level();
        var list = new AdditionalBuffList((id, level) => id == 7 ? lv : null);
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 10, 5, 0, new BuffState(0, 1000, 7, 1, 1, 1), 0));
        Assert.That(RcLane.Long(list, "SkillUseDisableMask"), Is.EqualTo(1L << 1), "the effect's main type 1");
        Assert.That(RcLane.Float(list, "AlphaBlending"), Is.EqualTo(0.5f));
        Assert.That(RcLane.Float(list, "ScaleMultiplier", "Scale"), Is.EqualTo(2f));
        Assert.That(RcLane.Float(list, "SoundPitch"), Is.EqualTo(1.5f));
        Assert.That(RcLane.MemberAny(list, "IgnoreCollisionGroups").ToString(), Does.Contain("1").And.Contain("2"));
        Assert.That(RcLane.Float(list, "FixedZRotation"), Is.EqualTo(90f));
        Assert.That(RcLane.Bool(list, "InfinitySp", "InfinitySP"), Is.True);
        Assert.That(RcLane.Call(list, "ItemSlotDisabled", 5), Is.EqualTo(true), "the right hand slot is disabled");
        Assert.That(RcLane.Call(list, "ItemSlotDisabled", 1), Is.EqualTo(false));
    }
}
