using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class SkillLoopTests
{
    static SkillData Continue(bool on = true) => new() { Id = 10200001, Continue = on };

    [Test]
    public void TheHoldCheckRunsForAContinueSkillUntilTheLoopEndsAndAgainFromTheNextLoopStart()
    {
        // vfunc_8 (stateskill.cpp:649): m_bA5 && m_bContinue && IsNearlyEqual(m_fChargeTime, 0); loopend sets m_fChargeTime
        // (:876), loopstart clears it (:864)
        var l = new SkillLoop();
        Assert.That((l.HoldCheckRuns(Continue()), l.HoldCheckRuns(Continue(false))), Is.EqualTo((true, false)));
        l.OnLoopEnd(1.5f, 1.5f, 1f, 1000);
        Assert.That(l.HoldCheckRuns(Continue()), Is.False);
        l.OnLoopStart(0.5f, 1100);
        Assert.That(l.HoldCheckRuns(Continue()), Is.True);
    }

    [Test]
    public void AHeldCastableSkillChargesOnlyWithBothLoopKeysAndAnUncastableOneStopsTheCheck()
    {
        // vfunc_8 :671-693: bLoop = loopstart && loopend; FUN_141765a70 true -> m_bCharging = bLoop, false -> m_bA5 = false
        var l = new SkillLoop();
        l.Hold(held: true, hasLoopKeys: true, chargingSkill: false, castable: true);
        Assert.That(l.Charging, Is.True);
        l.Hold(held: false, hasLoopKeys: true, chargingSkill: false, castable: true);
        Assert.That((l.Charging, l.Active), Is.EqualTo((false, true)));
        l.Hold(held: true, hasLoopKeys: false, chargingSkill: false, castable: true);
        Assert.That((l.Charging, l.Active), Is.EqualTo((false, true)));
        l.Hold(held: true, hasLoopKeys: true, chargingSkill: false, castable: false);
        Assert.That((l.Charging, l.Active, l.HoldCheckRuns(Continue())), Is.EqualTo((false, false, false)));
    }

    [Test]
    public void AChargingSkillChargesWhileRepeatsAreLeft()
    {
        // vfunc_8 :677-684: m_b111 -> bLoop && m_nRepeat > 0
        var l = new SkillLoop();
        l.Hold(true, true, chargingSkill: true, castable: false);
        Assert.That((l.Charging, l.Active), Is.EqualTo((false, true)));
        l.StartSequence(2);
        l.Hold(true, true, chargingSkill: true, castable: false);
        Assert.That(l.Charging, Is.True);
    }

    [Test]
    public void ALoopEndWhileChargingStartsARoundWithTheLengthAndTheLateTimeClamped()
    {
        // loopend (:876-905): m_n134 = (int)((end - start) / speed * 1000), m_nChargeTick = ClampedElapsedAfter16ms(now - m_n138,
        // m_n134), at most m_n134 - 5
        var l = new SkillLoop();
        l.OnLoopStart(0.5f, 1000);
        l.Hold(true, true, false, true);
        Assert.That(l.OnLoopEnd(1.5f, 1.5f, 2f, 1540), Is.True);
        Assert.That((l.LoopMs, l.ChargeTick, l.Charging, l.InLoop, l.Speed), Is.EqualTo((500u, 24u, false, true, 2f)));
        // a quick loop: the clamp to the length less 5
        var q = new SkillLoop();
        q.OnLoopStart(0.5f, 1000);
        q.Hold(true, true, false, true);
        q.OnLoopEnd(0.53125f, 0.53125f, 1f, 1100);
        Assert.That((q.LoopMs, q.ChargeTick), Is.EqualTo((31u, 26u)));
    }

    [Test]
    public void ALoopEndWithoutAHoldEndsTheLoopAndARepeatPlaysItWithoutOne()
    {
        // loopend :876-879: m_bCharging || m_nRepeat > 0, a repeat used; EndOfSequence (:1056-1060) waits while either holds
        var l = new SkillLoop();
        l.OnLoopStart(0.5f, 0);
        Assert.That((l.OnLoopEnd(1f, 1f, 1f, 10), l.InLoop, l.HoldsEnd), Is.EqualTo((false, false, false)));
        l.StartSequence(1);
        Assert.That(l.HoldsEnd, Is.True);
        Assert.That((l.OnLoopEnd(1f, 1f, 1f, 10), l.Repeat, l.HoldsEnd), Is.EqualTo((true, 0, false)));
    }

    [Test]
    public void TheLateTimeCountsAfterTheFirst16MsUpTo50()
    {
        // ClampedElapsedAfter16ms @0x1417f1a10 (:1611)
        Assert.That((SkillLoop.ClampedElapsedAfter16ms(100, 110), SkillLoop.ClampedElapsedAfter16ms(116, 100),
                     SkillLoop.ClampedElapsedAfter16ms(130, 100), SkillLoop.ClampedElapsedAfter16ms(500, 100)),
            Is.EqualTo((0, 0, 14, 50)));
    }

    [Test]
    public void ARoundsCooldownIsTheLoopLessTheChargeTickNotBelowZero()
    {
        // FUN_1417f16f0 (:1640-1643)
        Assert.That(SkillLoop.RoundCooldown(0.5f, 1.5f, 2f, 100), Is.EqualTo(0.4f).Within(1e-6f));
        Assert.That(SkillLoop.RoundCooldown(0.5f, 0.52f, 1f, 50), Is.EqualTo(0f));
    }

    [Test]
    public void AReleasedChargingSkillSkipsToItsChargingAttackAndSkipsTheSyncThatFrame()
    {
        // vfunc_8 (stateskill.cpp:698-722): m_b111 && !m_bLoop && !IsKeyDown(m_nA4) -> m_bCharging off, m_nRepeat 0; without
        // disableChargingAttackSkipFrame m_bLoop on, m_b117 off, m_fLoopStart the charging_attack key; return (no SendChargeState)
        var l = new SkillLoop();
        l.StartSequence(3);
        l.Charging = true;
        l.InLoop = true;
        Assert.That(l.Release(true, false, false, 0.75f, out bool seek), Is.True);
        Assert.That((seek, l.Charging, l.Repeat, l.Loop, l.InLoop, l.LoopStart), Is.EqualTo((true, false, 0, true, false, 0.75f)));
        // once m_bLoop is on the branch is passed and the sync runs
        Assert.That((l.Release(true, false, false, 0.75f, out seek), seek), Is.EqualTo((false, false)));
    }

    [Test]
    public void AHeldKeyOrANonChargingSkillIsNotReleased()
    {
        // vfunc_8 :698-701: only m_b111 with the cast key up
        var l = new SkillLoop();
        l.StartSequence(2);
        Assert.That(l.Release(true, true, false, 0.75f, out _), Is.False);
        Assert.That(l.Release(false, false, false, 0.75f, out _), Is.False);
        Assert.That((l.Repeat, l.Loop), Is.EqualTo((2, false)));
    }

    [Test]
    public void WithDisableChargingAttackSkipFrameTheReleaseOnlyStopsTheChargeAndEveryFrameSkipsTheSync()
    {
        // vfunc_8 :705: the skip is under !m_bDisableChargingAttackSkipFrame; the return is not, and m_bLoop stays off
        var l = new SkillLoop();
        l.StartSequence(2);
        l.Charging = true;
        Assert.That((l.Release(true, false, true, 0.75f, out bool seek), seek, l.Charging, l.Repeat, l.Loop),
            Is.EqualTo((true, false, false, 0, false)));
        Assert.That(l.Release(true, false, true, 0.75f, out _), Is.True);
    }

    [Test]
    public void TheChargingTextKeysTurnTheLoopOnOnlyWithNoRepeatLeft()
    {
        // TextKeyEvent :980-987 (point: m_bLoop on), :990-997 (charging_attack: m_bLoop on, m_b117 off), both under
        // m_b111 && !m_bLoop && m_nRepeat == 0
        var l = new SkillLoop();
        l.StartSequence(1);
        l.InLoop = true;
        l.OnAttackPoint(true);
        l.OnChargingAttack(true);
        Assert.That((l.Loop, l.InLoop), Is.EqualTo((false, true)));
        l.StartSequence(0);
        l.OnChargingAttack(false);
        l.OnAttackPoint(false);
        Assert.That(l.Loop, Is.False);
        l.OnAttackPoint(true);
        Assert.That((l.Loop, l.InLoop), Is.EqualTo((true, true)));
        var a = new SkillLoop();
        a.InLoop = true;
        a.OnChargingAttack(true);
        Assert.That((a.Loop, a.InLoop), Is.EqualTo((true, false)));
    }

    [Test]
    public void TheChargingComboKeyWithTheKeyHeldTakesTheLateTimeAndTurnsTheLoopOn()
    {
        // TextKeyEvent :947-957: IsKeyDown(m_nA4) -> m_nChargeTick = ClampedElapsedAfter16ms(now, m_nChargingComboTick),
        // m_bLoop on, m_b117 off
        var l = new SkillLoop();
        l.InLoop = true;
        l.OnChargingCombo(true, false, 1100, 1000);
        Assert.That((l.Loop, l.ChargeTick), Is.EqualTo((false, 0u)));
        l.OnChargingCombo(true, true, 1040, 1000);
        Assert.That((l.Loop, l.InLoop, l.ChargeTick), Is.EqualTo((true, false, 24u)));
    }

    [Test]
    public void TheComboElementLoadsDisableChargingAttackSkipFrame()
    {
        // Unk_SkillBlockA4::Load (skillleveldata.cpp:502)
        var e = System.Xml.Linq.XElement.Parse(
            "<skill id=\"10200001\"><basic><combo chargingSkill=\"1\" disableChargingAttackSkipFrame=\"1\"/></basic></skill>");
        var s = SkillData.Load(e, (_, _, _) => new System.Collections.Generic.List<System.Xml.Linq.XElement>());
        Assert.That((s.ChargingSkill, s.DisableChargingAttackSkipFrame), Is.EqualTo((true, true)));
    }

    [Test]
    public void TheLoopAllocatesNothing()
    {
        var l = new SkillLoop();
        var s = Continue();
        long Window()   // measured twice, the second asserted (the first can take one-time JIT allocations)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                l.OnLoopStart(0.5f, (uint)i);
                if (l.HoldCheckRuns(s)) l.Hold(true, true, false, true);
                l.OnLoopEnd(1f, 1f, 1f, (uint)i + 20);
                l.OnChargingCombo(true, true, (uint)i, 0);
                l.OnAttackPoint(true);
                l.OnChargingAttack(true);
                l.Release(true, false, false, 0.75f, out _);
                l.Loop = false;
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Window();
        Assert.That(Window(), Is.LessThan(512));
    }
}
