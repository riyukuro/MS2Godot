using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G04 — NPC core fields (D3, C9-C13): GMS2 keeps shadow/speed in &lt;environment&gt;&lt;shadow&gt;/&lt;speed&gt; (read), the talk and
/// shop flags on &lt;basic&gt;, sight/corpse in their own children and &lt;lookattarget&gt; (all read).
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
        Assert.That((r.ShadowScale, r.WalkSpeed, r.RunSpeed, r.RotationSpeed), Is.EqualTo((380f, 250f, 500f, 720f)));
    }

    // the turn cap: 0 makes NpcLayer.Turn leave the npc at its spawn yaw whatever the sync blocks say
    [Test]
    public void TheGms2NpcReadsShadowAndSpeeds()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new NpcTable(packs);
        var r = t.Find(22000052);
        Assert.That(r, Is.Not.Null, "gms/npc/22/00/22000052.xml npc 22000052");
        Assert.That((r.ShadowScale, r.WalkSpeed, r.RunSpeed, r.RotationSpeed), Is.EqualTo((380f, 250f, 500f, 720f)),
            "<shadow scale=\"380\"> <speed rotation=\"720\" walk=\"250\" run=\"500\">");
        // gms/npc/11/00/11000169.xml, the Tria guard the town's spawn points place: <speed rotation="500" walk="120" run="0">
        Assert.That(t.Find(11000169)?.RotationSpeed, Is.EqualTo(500f));
        Assert.That(t.Rows.Count(n => n.RotationSpeed > 0), Is.GreaterThan(9000), "the npcs that turn (9,764)");
    }

    // the talk look: GMS2's <lookattarget> and the KMS row's environment attributes give the same values
    [TestCase("MS2_GMS2_DATA")]
    [TestCase("MS2_DATA")]
    public void TheNpcReadsItsTalkLook(string data)
    {
        using var packs = new PackSet(RcGap.Data(data));
        var t = new NpcTable(packs);
        var r = t.Find(22000052);
        Assert.That(r, Is.Not.Null, "npc 22000052");
        Assert.That((r.LookAtMyPcWhenTalking, r.UseTalkMotion), Is.EqualTo((false, true)), "lookAtMyPCWhenTalking 0, useTalkMotion 1");
        // KMS2_DEV 7,333 of 10,793 rows turn it off, GMS2 6,922 of 10,193
        Assert.That(t.Rows.Count(n => !n.LookAtMyPcWhenTalking), Is.GreaterThan(6000));
        Assert.That(t.Rows.Count(n => !n.UseTalkMotion), Is.GreaterThan(500));
    }

    // the spawn effect: environment regenEffect, which StateSpawn plays when the spawn state shows the npc (statespawn.cpp:114-127)
    [TestCase("MS2_GMS2_DATA")]
    [TestCase("MS2_DATA")]
    [TestCase("MS2_KMS2_DEV_DATA")]
    public void TheNpcReadsItsRegenEffect(string data)
    {
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(data))) Assert.Ignore($"{data} not set");
        using var packs = new PackSet(RcGap.Data(data));
        var t = new NpcTable(packs);
        Assert.That(t.Find(22000052)?.RegenEffect, Is.EqualTo("BG/Common/Eff_Test_Regen.xml"));
        var with = t.Rows.Where(n => !string.IsNullOrEmpty(n.RegenEffect)).ToList();
        var paths = with.GroupBy(n => n.RegenEffect, System.StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
        var effects = new EffectTable(packs);
        var missing = paths.Where(g => effects.Get(g.Key) == null).Select(g => $"{g.Key} ({g.Count()})").ToList();
        TestContext.Out.WriteLine($"{data}: {t.Rows.Count()} rows, {with.Count} with a regenEffect, {paths.Count} paths, {missing.Count} without an effect xml: " +
            string.Join(", ", paths.Take(12).Select(g => $"{g.Key} {g.Count()}")) + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : ""));
        // KMS2 4,789 of 10,795 rows (BG/Common/Eff_Test_Regen.xml 4,509), KMS2_DEV 4,788 of 10,793, GMS2 4,373 of 10,193
        Assert.That(with.Count, Is.GreaterThan(4000));
        Assert.That(missing, Is.Empty);
    }

    // CNpcData::LoadXml (@0x1406cae60, npcdata.cpp:1448-1563): the groups, hitImmune, rotationDisabled and talkAni off the
    // environment (GMS2: its <basic>), the distances off <distance> as floats, hitAble off <corpse>
    [TestCase("MS2_GMS2_DATA")]
    [TestCase("MS2_DATA")]
    public void TheNpcReadsItsSightCorpseGroupsAndTalk(string data)
    {
        using var packs = new PackSet(RcGap.Data(data));
        var t = new NpcTable(packs);
        var r = t.Find(22000052);
        Assert.That(r, Is.Not.Null, "npc 22000052");
        Assert.That((r.ShadowScale, r.WalkSpeed, r.RunSpeed), Is.EqualTo((380f, 250f, 500f)));
        Assert.That((r.Avoid, r.Sight, r.SightHeightUp, r.SightHeightDown), Is.EqualTo((140f, 400f, 300f, 0f)),
            "<distance avoid=\"140\" sight=\"400\" sightHeightUP=\"300\">");
        Assert.That(r.TalkAni, Is.True, "talkAni=\"1\"");
        Assert.That((r.HitImmune, r.NpcAttackGroup, r.NpcDefenseGroup), Is.EqualTo((99, 1, 2)));
        Assert.That(r.RegenEffect, Is.EqualTo("BG/Common/Eff_Test_Regen.xml"));
        Assert.That(r.CorpseHitAble, Is.False, "<corpse hitAble=\"0\">");
        Assert.That(r.RotationDisabled, Is.False);
        // over every row: the sight is set on most npcs, and the groups, hitImmune and rotationDisabled on some
        int sight = t.Rows.Count(n => n.Sight > 0), attack = t.Rows.Count(n => n.NpcAttackGroup != 0), defense = t.Rows.Count(n => n.NpcDefenseGroup != 0),
            immune = t.Rows.Count(n => n.HitImmune != 0), fixedYaw = t.Rows.Count(n => n.RotationDisabled), lastSight = t.Rows.Count(n => n.CustomLastSightRadius > 0);
        TestContext.Out.WriteLine($"{data}: {t.Rows.Count()} rows; sight {sight}, attack group {attack}, defense group {defense}, hitImmune {immune}, rotationDisabled {fixedYaw}, customLastSightRadius {lastSight}");
        Assert.That(sight, Is.GreaterThan(1000));
        Assert.That(attack, Is.GreaterThan(0));
        Assert.That(defense, Is.GreaterThan(0));
        Assert.That(immune, Is.GreaterThan(0));
    }
}
