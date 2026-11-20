using System.Xml.Linq;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CSkillSystem's cooldown table and charge uses (skillslot.cpp:1700-2040, 2645-2700).</summary>
public class SkillCooltimesTests
{
    static SkillDataTable Table() => new(() => new[] { XDocument.Parse("<ms2>"
        + "<skill id=\"100\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"7\" rechargeMaxCount=\"3\"/></basic></skill>"
        + "<skill id=\"101\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"7\"/></basic></skill>"
        + "<skill id=\"102\" originID=\"100\"><basic mainType=\"1\"><stateAttr rechargeShareOrigin=\"1\" rechargeMaxCount=\"3\"/></basic></skill>"
        + "<skill id=\"103\"><basic mainType=\"1\"/></skill></ms2>") }, (e, n, _) => e.Elements(n).ToList());

    static (SkillCooltimes C, List<string> Log) Make()
    {
        var t = Table();
        var c = new SkillCooltimes(t.GetSkillData);
        var log = new List<string>();
        c.Started += k => log.Add($"+{k:x}");
        c.Ended += k => log.Add($"-{k:x}");
        return (c, log);
    }

    [Test]
    public void ADurationRunsFromItsStartUnderTheSkillsKey()
    {
        var (c, log) = Make();
        c.SetDuration(1000, 103, 2.5f);
        Assert.That(c.Get(103) is { Start: 1000, End: 3500 }, "FUN_14028d260: seconds * 1000");
        Assert.That(c.Remain(103, false, 2000), Is.EqualTo(1.5f).Within(1e-6f));
        Assert.That(c.Remain(103, false, 3500), Is.EqualTo(0f), "IsOver at the end tick");
        Assert.That(log, Is.EqualTo(new[] { "+67" }));
    }

    [Test]
    public void AGroupKeyCoolsTheGroupsOtherSkills()
    {
        var (c, _) = Make();
        c.SetDuration(0, 100, 10f);                       // key 7 << 32 | 100
        Assert.That(c.Get((7UL << 32) | 100), Is.Not.Null);
        Assert.That(c.Remain(101, true, 4000, out var remain, out var total, out var byGroup), Is.True);
        Assert.That(remain, Is.EqualTo(6f).Within(1e-5f));
        Assert.That((total, byGroup), Is.EqualTo((10f, true)));
        Assert.That(c.Remain(103, false, 4000), Is.EqualTo(0f), "no group");
    }

    [Test]
    public void AnEndTickReplacesTheKeyUnlessItHasPassed()
    {
        var (c, log) = Make();
        c.SetDuration(0, 103, 50f);
        c.SetEnd(103, 9000, 8000);
        Assert.That(c.Get(103) is { Start: 0, End: 9000 });
        c.SetEnd(103, 7000, 8000);
        Assert.That(c.Get(103).End, Is.EqualTo(9000u), "FUN_141766900 returns once the server tick is past the end");
        c.ClearKey(103);
        Assert.That(c.Remain(103, false, 8000), Is.EqualTo(0f));
        Assert.That(log, Is.EqualTo(new[] { "+67", "+67", "-67" }));
    }

    [Test]
    public void ResetClearsEveryEndAndTheUsesWithoutAnnouncing()
    {
        var (c, log) = Make();
        var t = Table();
        c.SetDuration(0, 103, 50f);
        c.SetRechargeUses(100, 2);
        c.ResetAll();
        Assert.That((c.Remain(103, false, 10), c.RechargeUses(t.GetSkillData(100))), Is.EqualTo((0f, 0)));
        Assert.That(log, Is.EqualTo(new[] { "+67" }));
        c.Update(10);
        Assert.That(log, Is.EqualTo(new[] { "+67", "-67" }), "the next update announces it");
    }

    [Test]
    public void UpdateAnnouncesOnceThenDropsTenSecondsLater()
    {
        var (c, log) = Make();
        c.SetDuration(0, 103, 1f);
        c.Update(999);
        c.Update(1000);
        c.Update(5000);
        Assert.That((log.Count, c.Count), Is.EqualTo((2, 1)));
        c.Update(11001);
        Assert.That(c.Count, Is.EqualTo(0));
    }

    [Test]
    public void UsesShareTheOriginsKeyWhenTheSkillSaysSo()
    {
        var (c, _) = Make();
        var t = Table();
        c.SetRechargeUses(102, 1);
        Assert.That(c.RechargeUses(t.GetSkillData(100)), Is.EqualTo(1), "rechargeShareOrigin: under 100");
        Assert.That((c.RechargeRemain(t.GetSkillData(100)), c.RechargeRemain(t.GetSkillData(103))), Is.EqualTo((2, 0)));
        c.SetRechargeUses(100, 5);
        Assert.That(c.RechargeRemain(t.GetSkillData(102)), Is.EqualTo(0));
    }

    [Test]
    public void AFailedUseStartsTheRechargeIdsCooldownOrClearsTheSkill()
    {
        var (c, log) = Make();
        // OnSkillUseFailed :3449-3466: skill 102 shares origin 100's charges, so its cooldown and uses go under 100
        c.Apply(new Ms2.Net.Packets.SkillUseFailed(0, 1, 102, 0, 0, 2000, 3f, 2));
        Assert.That(c.Get((7UL << 32) | 100) is { Start: 2000, End: 5000 }, "under the recharge id's key");
        Assert.That(c.Get(102), Is.Null);
        Assert.That(c.RechargeUses(Table().GetSkillData(100)), Is.EqualTo(2));
        // no cooldown and no uses: the skill's own key ends now
        c.SetDuration(0, 103, 10f);
        c.Apply(new Ms2.Net.Packets.SkillUseFailed(0, 1, 103, 0, 0, 0, 0f, 0));
        Assert.That(c.Get(103).End, Is.EqualTo(0u));
        // no cooldown but uses left: nothing is cleared
        c.SetDuration(0, 103, 10f);
        c.Apply(new Ms2.Net.Packets.SkillUseFailed(0, 1, 103, 0, 0, 0, 0f, 1));
        Assert.That(c.Get(103).End, Is.EqualTo(10000u));
        Assert.That(log, Is.EqualTo(new[] { "+700000064", "+67", "-67", "+67" }));
    }

    [Test]
    public void ACooldownPacketEndsOrSetsEachKeyAndItsUses()
    {
        var (c, log) = Make();
        c.SetDuration(0, 103, 10f);
        long groupKey = (7L << 32) | 100;
        c.Apply(new Ms2.Net.Packets.SkillCooldown(new[]
        {
            new Ms2.Net.Packets.SkillCooldownEntry(103, 0, 0),
            new Ms2.Net.Packets.SkillCooldownEntry(groupKey, 9000, 1),
        }), 4000);
        Assert.That(c.Get(103).End, Is.EqualTo(0u), "end 0: FUN_141765f60");
        Assert.That(c.Get((ulong)groupKey) is { End: 9000 });
        Assert.That(c.RechargeUses(Table().GetSkillData(100)), Is.EqualTo(1), "uses under the key's low word");
        Assert.That(log, Is.EqualTo(new[] { "+67", "-67", "+700000064" }));
    }

    [Test]
    public void UpdateAllocatesNothing()
    {
        var c = new SkillCooltimes(Table().GetSkillData);
        int ended = 0;
        c.Ended += _ => ended++;
        c.SetDuration(0, 103, 1f);
        c.SetDuration(0, 100, 1000f);
        c.Update(1); c.Update(2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint i = 0; i < 100000; i++) c.Update(10 + i);
        // a Release run takes a one-off 24 B while the loop tiers up; per call nothing
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512), "100000 updates");
        Assert.That(ended, Is.EqualTo(1), "103 ended inside the loop");
    }
}
