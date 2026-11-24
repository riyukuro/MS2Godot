using System.Xml.Linq;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CSkillSystem's cooldown table and charge uses (skillslot.cpp:1700-2040, 2266-2307, 2645-2700).</summary>
public class SkillCooltimesTests
{
    static SkillDataTable Table() => new(() => new[] { XDocument.Parse("<ms2>"
        + "<skill id=\"100\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"7\" rechargeMaxCount=\"3\"/></basic></skill>"
        + "<skill id=\"101\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"7\"/></basic></skill>"
        + "<skill id=\"102\" originID=\"100\"><basic mainType=\"1\"><stateAttr rechargeShareOrigin=\"1\" rechargeMaxCount=\"3\"/></basic></skill>"
        + "<skill id=\"103\"><basic mainType=\"1\"/></skill>"
        + "<skill id=\"104\"><basic mainType=\"1\"><condition useCoolDownShowUI=\"1\"/></basic>"
        + "<level value=\"1\" cooldown=\"5000\"><motion sequenceName=\"A\" sequenceSpeed=\"2\"/></level></skill>"
        + "<skill id=\"105\"><basic mainType=\"1\"/><level value=\"1\" cooldown=\"4000\"><motion sequenceSpeed=\"2\"/></level></skill>"
        + "<skill id=\"106\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"8\"/></basic></skill>"
        + "<skill id=\"113\"><basic mainType=\"1\"><stateAttr cooldownGroupID=\"8\" ignoreReduceCooldown=\"1\"/></basic></skill>"
        + "<skill id=\"114\"><basic mainType=\"1\"><kinds groupIDs=\"9\"/></basic></skill>"
        // 107: no cooldown, a sequence (the combo window); 108: a combo row swapping to 109; 110: two charges 0.5 s apart;
        // 111: a continue skill; 112: a charging combo row swapping to itself
        + "<skill id=\"107\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"B\" sequenceSpeed=\"2\"/></level></skill>"
        + "<skill id=\"108\"><basic mainType=\"1\"><combo inputSkill=\"108\" outputSkill=\"109\"/></basic>"
        + "<level value=\"1\"><motion sequenceName=\"B\"/></level></skill>"
        + "<skill id=\"109\"><basic mainType=\"1\"/><level value=\"1\"><motion sequenceName=\"C\"/></level></skill>"
        + "<skill id=\"110\"><basic mainType=\"1\"><stateAttr rechargeMaxCount=\"2\"/><condition defaultRechargingCooldownTime=\"0.5\"/></basic>"
        + "<level value=\"1\" cooldown=\"3000\"><motion sequenceName=\"B\"/></level></skill>"
        + "<skill id=\"111\"><basic mainType=\"1\"><kinds continue=\"1\"/></basic><level value=\"1\"><motion sequenceName=\"B\"/></level></skill>"
        + "<skill id=\"112\"><basic mainType=\"1\"><combo inputSkill=\"1\" outputSkill=\"112\" chargingSkill=\"1\"/></basic>"
        + "<level value=\"1\"><motion sequenceName=\"B\"/></level></skill>"
        // 120: a GMS2-shaped level (beginCondition cooldownTime 0.4 s) over a sequence whose combo key is 1.2 s
        + "<skill id=\"120\"><basic mainType=\"1\"/><level value=\"1\"><beginCondition cooldownTime=\"0.4\"/><motion sequenceName=\"B\"/></level></skill></ms2>") }, (e, n, _) => e.Elements(n).ToList());

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
        uint now = 10;                                         // the clock runs on across the windows (Alloc.Least)
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100000; i++) c.Update(now++); }), Is.LessThan(512), "100000 updates");
        Assert.That(ended, Is.EqualTo(1), "103 ended inside the loop");
    }

    static CastRecord Record(SkillDataTable t, uint id, int sendTick)
    {
        var d = t.GetSkillData(id);
        return new CastRecord { Skill = d, Level = d.GetLevelData(1), Motion = 0, Tick = sendTick, SendTick = sendTick };
    }

    static SlotWorld Gate(SkillCooltimes c, Func<uint> now) => new() { CooldownRemain = (id, mode) => c.Remain(id, mode != 0, now()) };

    [Test]
    public void AnOwnCastCoolsFromItsSendTickAndRefusesARecast()
    {
        var (c, log) = Make();
        var t = Table();
        uint now = 2000;
        var lv = t.GetSkillData(104).GetLevelData(1);
        Assert.That(CastGate.Cost(Gate(c, () => now), lv, false), Is.EqualTo(0), "not cooling yet");
        // FUN_141525ff0: a shown cooldown is not scaled by the motion speed; the state's elapsed 0.5 s comes off
        c.StartCast(Record(t, 104, 1000), null, 0.5f, now);
        Assert.That(c.Get(104) is { Start: 1000, End: 5500, IsCombo: false, Combo: 0f });
        Assert.That(log, Is.EqualTo(new[] { "+68" }), "FUN_141b0e260 without the combo flag");
        Assert.That(CastGate.Cost(Gate(c, () => now), lv, false), Is.EqualTo(2), "FUN_14176a530: still cooling");
        now = 5500;
        Assert.That(CastGate.Cost(Gate(c, () => now), lv, false), Is.EqualTo(0));
    }

    /// <summary>A level cooldown of 0.4 s starts 400 ms, not the sequence's 1.2 s combo window. The seconds-to-ms mapping of the GMS2
    /// beginCondition is a data shape, not a recording.</summary>
    [Test]
    public void ALevelCooldownWinsOverTheSequenceWindow()
    {
        var (c, _) = Make();
        var t = Table();
        c.StartCast(Record(t, 120, 1000), Seq((1.2, "combo")), 0f, 1000);
        Assert.That(c.Get(120) is { Start: 1000, End: 1400 });
    }

    [Test]
    public void AHiddenCooldownIsScaledByTheMotionSpeedWithoutTheReduce()
    {
        var (c, log) = Make();
        var t = Table();
        c.RateReduce = _ => 0.5f;
        c.StartCast(Record(t, 105, 1000), null, 0f, 1000);
        Assert.That(c.Get(105) is { Start: 1000, End: 3000, IsCombo: true }, "4 s / GetMotionSpeedScale 2, the combo flag skips the reduce");
        Assert.That(log, Is.Empty, "no Started event with the combo flag");
        uint now = 2000;
        Assert.That(CastGate.Cost(Gate(c, () => now), t.GetSkillData(105).GetLevelData(1), false), Is.EqualTo(0x23));
    }

    [Test]
    public void TheReduceComesOffAndANothingLeftTakesTheComboWindow()
    {
        var (c, _) = Make();
        c.GroupReduceRate = g => g == 8 ? 0.25f : 0f;
        c.FlatReduce = _ => 1f;
        c.RateReduce = _ => 0.25f;
        c.Start(0, 106, 10f, 0f, false, 0);
        Assert.That(c.Get((8UL << 32) | 106).End, Is.EqualTo(4000u), "(10 - 1) - 10 * (0.25 + 0.25)");
        c.Start(0, 106, 1f, 3f, false, 0);
        Assert.That(c.Get((8UL << 32) | 106) is { End: 3000, Combo: 3f }, "the reduce leaves 0: the combo window");
        c.Start(0, 106, 1f, 0f, false, 0);
        Assert.That(c.Get((8UL << 32) | 106), Is.Null, "nothing left: the key is removed and none set");
    }

    [Test]
    public void IgnoreReduceCooldownKeepsTheStatsOutButNotTheGroupReduce()
    {
        // m_bIgnoreReduceCooldown (<stateAttr ignoreReduceCooldown>, skilloverlapcompare.cpp:868) in FUN_1417664e0 (skillslot.cpp:2288)
        var (c, _) = Make();
        c.GroupReduceRate = g => g == 8 ? 0.25f : 0f;
        c.FlatReduce = _ => 1f;
        c.RateReduce = _ => 0.25f;
        c.Start(0, 113, 10f, 0f, false, 0);
        Assert.That(c.Get((8UL << 32) | 113).End, Is.EqualTo(7500u), "10 - 10 * 0.25");
    }

    [Test]
    public void TheAbilityRateAddsToTheRateUnlessTheSkillIgnoresTheReduce()
    {
        // FUN_1417664e0 (skillslot.cpp:2350): fRate += DAT_143aea120 + FUN_1416628b0(0x3e9, id), not with ignoreReduceCooldown
        var (c, _) = Make();
        c.AbilityRate = 0.25f;
        c.RateReduce = _ => 0.25f;
        c.Start(0, 106, 10f, 0f, false, 0);
        Assert.That(c.Get((8UL << 32) | 106).End, Is.EqualTo(5000u), "10 - 10 * (0.25 + 0.25)");
        c.Start(0, 113, 10f, 0f, false, 0);
        Assert.That(c.Get((8UL << 32) | 113).End, Is.EqualTo(10000u), "ignoreReduceCooldown");
    }

    [Test]
    public void TheSkillsGroupIsItsKindsGroupIds()
    {
        // <kinds groupIDs> (m_nGroupIDs, skilloverlapcompare.cpp:850), the group an invoke effect's skillGroupID names
        Assert.That((Table().GetSkillData(114).GroupIds, Table().GetSkillData(100).GroupIds), Is.EqualTo((9u, 0u)));
    }

    [Test]
    public void ACoolingChargeSkillKeepsItsCooldown()
    {
        var (c, _) = Make();
        c.SetDuration(0, 100, 10f);
        c.Start(1000, 100, 2f, 0f, false, 1000);
        Assert.That(c.Get((7UL << 32) | 100).End, Is.EqualTo(10000u), "rechargeMaxCount 3 and still cooling");
    }

    /// <summary>A sequence of 2 s with the given text keys.</summary>
    static Ms2.Formats.Model.KfAnimation Seq(params (double, string)[] keys)
    {
        var k = new Ms2.Formats.Model.KfAnimation { Duration = 2.0 };
        k.TextKeys.AddRange(keys);
        return k;
    }

    [Test]
    public void ASecondPressInsideTheCooldownSendsNothing()
    {
        var (c, _) = Make();
        var t = Table();
        // MyPC::vfunc_102 (mypc.cpp:1152-1166): FUN_141765fe0 > 0 refuses whether the row shows the cooldown (104) or not (105)
        foreach (uint id in new uint[] { 104, 105 })
        {
            var r = Record(t, id, 1000);
            Assert.That(SkillCast.CooldownGate(c, r, false, 1000), Is.EqualTo(CoolGate.Cast), $"{id}: not cooling yet");
            c.StartCast(r, Seq(), 0f, 1000);
            Assert.That(SkillCast.CooldownGate(c, Record(t, id, 1500), false, 1500), Is.EqualTo(CoolGate.Refused), $"{id}");
            Assert.That(SkillCast.CooldownGate(c, Record(t, id, 1500), true, 1500), Is.EqualTo(CoolGate.Queued), $"{id}: the skill state queues it");
        }
    }

    [Test]
    public void ASkillWithoutACooldownCoolsForItsComboWindow()
    {
        var (c, log) = Make();
        var t = Table();
        // FUN_141525ff0: the "combo" key over the motion speed 2; no cooldown takes it with the combo flag
        c.StartCast(Record(t, 107, 1000), Seq((0.3, "begin"), (1.2, "combo")), 0.1f, 1000);
        Assert.That(c.Get(107) is { Start: 1000, End: 1500, IsCombo: true, Combo: 0f }, "1.2 / 2 - 0.1 elapsed; FUN_1417664e0 keeps the window only for a 0 cooldown");
        Assert.That(log, Is.Empty);
        Assert.That(SkillCast.CooldownGate(c, Record(t, 107, 1400), false, 1400), Is.EqualTo(CoolGate.Refused), "inside the combo window");
        Assert.That(SkillCast.CooldownGate(c, Record(t, 107, 1500), false, 1500), Is.EqualTo(CoolGate.Cast));
        // FUN_141b6fee0: no "combo" key gives the sequence's length; text keys match by case
        c.StartCast(Record(t, 107, 2000), Seq((0.5, "COMBO")), 0f, 2000);
        Assert.That(c.Get(107).End, Is.EqualTo(3000u), "2 s / 2");
        Assert.That(SkillCooltimes.KeyTime(null, "combo"), Is.EqualTo(-float.MaxValue));
    }

    [Test]
    public void AComboRowCoolsItsSwapSkill()
    {
        var (c, _) = Make();
        var t = Table();
        c.StartCast(Record(t, 108, 1000), Seq((0.4, "combo")), 0f, 1000);
        Assert.That(c.Get(109) is { Start: 1000, End: 1400, IsCombo: true, Combo: 0f }, "FUN_1417664e0(swap, combo, 0, 1)");
        Assert.That(c.Get(108) is { End: 1400 }, "then its own combo window");
        // a charging row: "charging_combo"; the swap skill is the cast's own, so nothing more
        c.StartCast(Record(t, 112, 5000), Seq((0.4, "combo"), (0.9, "charging_combo")), 0f, 5000);
        Assert.That(c.Get(112) is { Start: 5000, End: 5900, Combo: 0f });
        // a continue skill without a cooldown cools to "loopend"
        c.StartCast(Record(t, 111, 1000), Seq((0.7, "loopend"), (1.5, "combo")), 0f, 1000);
        Assert.That(c.Get(111) is { End: 1700, IsCombo: true }, "loopend, not the combo window");
    }

    [Test]
    public void AChargeSkillCastsUntilItsUsesAreSpent()
    {
        var (c, _) = Make();
        var t = Table();
        var s = t.GetSkillData(110);
        // first use: the cooldown starts, the use counts (OnRechargeSkillUsed)
        c.StartCast(Record(t, 110, 1000), Seq(), 0f, 1000);
        c.OnRechargeSkillUsed(s, 1000);
        Assert.That((c.RechargeUses(s), c.RechargeTick(s)), Is.EqualTo((1, 1000u)));
        Assert.That(SkillCast.CooldownGate(c, Record(t, 110, 1400), false, 1400), Is.EqualTo(CoolGate.Refused),
            "CanUseRechargeSkill: defaultRechargingCooldownTime 0.5 s not passed");
        Assert.That(SkillCast.CooldownGate(c, Record(t, 110, 1500), false, 1500), Is.EqualTo(CoolGate.Cast), "a charge left");
        c.OnRechargeSkillUsed(s, 1500);
        Assert.That(c.RechargeRemain(s), Is.EqualTo(0));
        Assert.That(SkillCast.CooldownGate(c, Record(t, 110, 2500), false, 2500), Is.EqualTo(CoolGate.Refused), "past the uses");
        Assert.That(SkillCast.CooldownGate(c, Record(t, 110, 4000), false, 4000), Is.EqualTo(CoolGate.Cast), "the cooldown is over");
        c.OnRechargeSkillUsed(t.GetSkillData(103), 1000);
        Assert.That(c.RechargeUses(t.GetSkillData(103)), Is.EqualTo(0), "no charges: nothing counted");
    }

    [Test]
    public void AQueuedPressCastsWhenItsCooldownEndsOrAChargeIsReady()
    {
        var (c, _) = Make();
        var t = Table();
        // StateSkill::FUN_1417f11d0 (stateskill.cpp:1583): CanUseRechargeSkill || IsNearlyEqual(FUN_141765fe0(id, 0), 0)
        var q = Record(t, 104, 1500);
        c.StartCast(Record(t, 104, 1000), Seq(), 0f, 1000);
        Assert.That(SkillCast.QueuedReady(c, q, 1500), Is.False, "cooling: the press stays queued");
        Assert.That(SkillCast.QueuedReady(c, q, 5999), Is.False, "1 ms left");
        Assert.That(SkillCast.QueuedReady(c, q, 6000), Is.True, "the cooldown is over");
        Assert.That(SkillCast.QueuedReady(c, Record(t, 105, 1500), 1500), Is.True, "not cooling at all");

        var s = t.GetSkillData(110);
        c.StartCast(Record(t, 110, 1000), Seq(), 0f, 1000);
        c.OnRechargeSkillUsed(s, 1000);
        var r = Record(t, 110, 1400);
        Assert.That(SkillCast.QueuedReady(c, r, 1400), Is.False, "defaultRechargingCooldownTime 0.5 s not passed");
        Assert.That(SkillCast.QueuedReady(c, r, 1500), Is.True, "a charge is ready while the cooldown remains");

        // the least of up to 5 windows is asserted (Alloc.Least)
        long Window()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint i = 0; i < 100000; i++) SkillCast.QueuedReady(c, q, 1500 + (i & 1023));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.That(Alloc.Least(Window), Is.LessThan(512), "100000 checks");
    }

    [Test]
    public void ACooldownThatRunsOutGivesAChargeBackAndRestartsWhileUsesAreLeft()
    {
        var (c, log) = Make();
        c.LevelCooldown = id => id == 110 ? 3f : null;
        c.SetDuration(0, 110, 3f);
        c.SetRechargeUses(110, 2);
        c.Update(2999);
        Assert.That(c.RechargeUses(Table().GetSkillData(110)), Is.EqualTo(2), "nothing before the end");
        c.Update(3000);                                       // FUN_1417687e0: one use back, the cooldown starts again
        var sk = Table().GetSkillData(110);
        Assert.That(c.RechargeUses(sk), Is.EqualTo(1));
        Assert.That(c.Get(110) is { Start: 3000, End: 6000, State: 0 }, "restarted from now");
        Assert.That(c.IsCooling(110, 3500), Is.True);
        Assert.That(c.Remaining(110, 4000), Is.EqualTo(2f).Within(1e-5f));
        Assert.That(log, Is.EqualTo(new[] { "+6e", "-6e", "+6e" }));
        c.Update(6000);                                       // the last use back: no restart
        Assert.That(c.RechargeUses(sk), Is.EqualTo(0));
        Assert.That(c.IsCooling(110, 6000), Is.False);
        Assert.That(log, Is.EqualTo(new[] { "+6e", "-6e", "+6e", "-6e" }));
    }

    [Test]
    public void ChargesWithNoCooldownRunningStartOneAndACombosEndRestoresNothing()
    {
        var (c, log) = Make();
        c.LevelCooldown = id => id == 110 ? 3f : null;
        c.SetRechargeUses(110, 1);
        c.Update(1000);                                       // FUN_141768a50: uses left, no cooldown: the level's cooldown starts
        Assert.That(c.Get(110) is { Start: 1000, End: 4000 }, Is.True);
        Assert.That(log, Is.EqualTo(new[] { "+6e" }));
        var (d, _) = Make();
        d.LevelCooldown = _ => 3f;
        d.SetRechargeUses(110, 2);
        d.Start(0, 110, 0f, 0.5f, true, 0);                   // a combo cooldown (no Started event)
        d.Update(600);
        Assert.That(d.RechargeUses(Table().GetSkillData(110)), Is.EqualTo(2), "m_bCombo: FUN_1417687e0 returns");
    }

    [Test]
    public void UpdateAllocatesNothingOnceTheListsHaveGrown()
    {
        var (c, _) = Make();
        c.LevelCooldown = _ => 3f;
        c.SetDuration(0, 110, 3f); c.SetRechargeUses(110, 2); c.SetDuration(0, 103, 50f);
        for (uint t = 0; t < 20000; t += 500) c.Update(t);    // warm: the lists and tables grow once
        uint start = 20000;
        long Window()                                          // each window restarts the cooldown outside the count
        {
            c.SetDuration(start, 110, 3f); c.SetRechargeUses(110, 2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint t = start; t < start + 20000; t += 16) c.Update(t);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            start += 20000;
            return bytes;
        }
        Assert.That(Alloc.Least(Window), Is.LessThanOrEqualTo(2048), "0 B per frame bar the restarted cooldown objects");
    }
}
