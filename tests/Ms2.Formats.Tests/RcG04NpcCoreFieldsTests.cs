using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G04 — NPC core fields (D3, C9-C13): GMS2 keeps shadow/speed in &lt;environment&gt;&lt;shadow&gt;/&lt;speed&gt;, the talk and
/// shop flags on &lt;basic&gt;, and sight/corpse/lookattarget in their own children; NpcTable reads &lt;model shadowScale&gt; only.
/// Fixtures: gms/npc/22/00/22000052.xml (env EliteRenewal: shadow 380, speed 250/500, sight 400/300/0,
/// talkAni 1, hitImmune 99, npcAttackGroup 1, regenEffect BG/Common/Eff_Test_Regen.xml, corpse hitAble 0,
/// lookAtMyPCWhenTalking 0, useTalkMotion 1) and kms/npcdata/220.xml (the same values on &lt;model&gt;).
/// </summary>
public class RcG04NpcCoreFieldsTests
{
    [Test]
    public void TheKmsNpcKeepsItsModelShadowAndSpeeds()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var r = new NpcTable(packs).Find(22000052);
        Assert.That(r, Is.Not.Null, "kms/npcdata/220.xml npc 22000052");
        Assert.That((r.ShadowScale, r.WalkSpeed, r.RunSpeed), Is.EqualTo((380f, 250f, 500f)));
    }

    [Test]
    [Ignore("G04: GMS2 npc <shadow>/<speed>/<distance>/<corpse>/<lookattarget> and basic talk/shop/regen not read yet")]
    public void TheGms2NpcReadsShadowSpeedSightCorpseAndTalk()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var r = new NpcTable(packs).Find(22000052);
        Assert.That(r, Is.Not.Null, "gms/npc/22/00/22000052.xml npc 22000052");
        Assert.That(r.ShadowScale, Is.EqualTo(380f), "<shadow scale=\"380\">");
        Assert.That(r.WalkSpeed, Is.EqualTo(250f), "<speed walk=\"250\">");
        Assert.That(r.RunSpeed, Is.EqualTo(500f), "<speed run=\"500\">");
        Assert.That(RcGap.Member<int>(r, "Sight"), Is.EqualTo(400), "<distance sight=\"400\">");
        Assert.That(RcGap.Member<int>(r, "SightHeightUp"), Is.EqualTo(300), "<distance sightHeightUP=\"300\">");
        Assert.That(RcGap.Member<int>(r, "SightHeightDown"), Is.EqualTo(0));
        Assert.That(RcGap.Member<int>(r, "Avoid"), Is.EqualTo(140));
        Assert.That(RcGap.Member<int>(r, "TalkAni"), Is.EqualTo(1), "<basic talkAni=\"1\">");
        Assert.That(RcGap.Member<int>(r, "HitImmune"), Is.EqualTo(99));
        Assert.That(RcGap.Member<int>(r, "NpcAttackGroup"), Is.EqualTo(1));
        Assert.That(RcGap.Member<int>(r, "NpcDefenseGroup"), Is.EqualTo(2));
        Assert.That(RcGap.Member<string>(r, "RegenEffect"), Is.EqualTo("BG/Common/Eff_Test_Regen.xml"));
        Assert.That(r.CorpseHitAble, Is.False, "<corpse hitAble=\"0\">");
        Assert.That(RcGap.Member<bool>(r, "LookAtMyPcWhenTalking"), Is.False, "<lookattarget lookAtMyPCWhenTalking=\"0\">");
        Assert.That(RcGap.Member<bool>(r, "UseTalkMotion"), Is.True, "<lookattarget useTalkMotion=\"1\">");
        Assert.That(RcGap.Member<bool>(r, "RotationDisabled"), Is.False);
    }
}
