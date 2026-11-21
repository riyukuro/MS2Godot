using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>cale_pc_OffenseScore / cale_pc_DefenseScore against the client chunk (luapack64.o run by Lua 5.1 on the same
/// arguments, after set_locale).</summary>
public class CombatScoreTests
{
    static readonly double[] O1 = { 40, 100, 120, 0, 4, 0, 10, 30, 0.05, 0, 0.03, 82, 51, 150, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 1, 0, 0, 0 };
    static readonly double[] O2 = { 10, 100, 120, 0, 40, 5, 60, 10, 0.2, 0.1, 0.05, 200, 300, 180, 0.1, 0.2, 0.05, 0.1, 0.15, 0.02, 0.3, 0.04, 0.12, 0.08, 0, 130, 0.4, 0, 4, 25, 0, 0 };
    static readonly double[] O3 = { 90, 100, 120, 0, 40, 5, 60, 10, 0.5, 0.3, 0.3, 200, 300, 180, 0.1, 0.2, 0.05, 0.1, 0.15, 0.02, 0.3, 0.04, 0.12, 0.08, 0, 200, 0.4, 0, 6, 25, 0, 0 };
    static readonly double[] O4 = { 50, 100, 120, 0, 40, 5, 60, 10, 0.5, 0.3, 0.3, 5, 300, 180, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 25, 0, 0 };

    [Test]
    public void OffenseFollowsTheScript()
    {
        Assert.That(CombatScore.Offense(O1, "KR"), Is.EqualTo(15.135386171254659).Within(1e-9), "a priest: magic attack, rarity 1");
        Assert.That(CombatScore.Offense(O2, "KR"), Is.EqualTo(583.8193169591184).Within(1e-9), "a knight: physical attack");
        Assert.That(CombatScore.Offense(O3, "KR"), Is.EqualTo(777.49491057610101).Within(1e-9), "a runeblade: both, capped rates");
        Assert.That(CombatScore.Offense(O4, "KR"), Is.EqualTo(10), "accuracy under 10: clipped to 0, the base 10");
        Assert.That(CombatScore.Offense(O2, "NA"), Is.EqualTo(639.33558433116639).Within(1e-9), "another locale: the rarity rate on both");
        var noJob = (double[])O2.Clone();
        noJob[0] = 0;
        Assert.That(CombatScore.Offense(noJob, "KR"), Is.Null, "no BAP row: the call fails");
        Assert.That(CombatScore.Result(null), Is.Zero);
        Assert.That(CombatScore.Result(CombatScore.Offense(O1, "KR")), Is.EqualTo(15));
    }

    [Test]
    public void DefenseFollowsTheScript()
    {
        Assert.That(CombatScore.Defense(new double[] { 40, 63, 7, 5, 5, 70, 50, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            Is.EqualTo(10.954787378136523).Within(1e-9));
        var d2 = new double[] { 20, 5000, 900, 300, 200, 120, 80, 0.1, 0.05, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.1, 0, 0, 10, 0 };
        Assert.That(CombatScore.Defense(d2), Is.EqualTo(12956.687567591602).Within(1e-7));
        var noDefense = (double[])d2.Clone();
        noDefense[0] = 100; noDefense[2] = 0;
        Assert.That(CombatScore.Defense(noDefense), Is.EqualTo(10), "no defense: 10 before the table");
        var noJob = (double[])d2.Clone();
        noJob[0] = 7;
        Assert.That(CombatScore.Defense(noJob), Is.Null);
    }

    [Test]
    public void ZeroCriticalEvasionGivesTheClientsIntMinValue()
    {
        // 1325 / (a[6] * 2) is a Lua float divide: 0 gives inf, the score -inf, and the wrapper's (int) cast of it is 0x80000000
        var d = new double[] { 20, 5000, 900, 300, 200, 120, 0, 0.1, 0.05, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.1, 0, 0, 10, 0 };
        Assert.That(CombatScore.Defense(d), Is.EqualTo(double.NegativeInfinity));
        Assert.That(CombatScore.Result(CombatScore.Defense(d)), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ArgsFollowTheWrappers()
    {
        var stats0 = new long[0x23];
        stats0[4] = 63; stats0[7] = 100; stats0[0xa] = 120;
        float? Basic(int s) => s switch { 0x1f => 50f, 0x18 => 30.9f, 0xd => 100f, _ => 1f };
        float? Special(int s, bool tenth) => s == 0x1b ? 3f : 0f;
        var o = CombatScore.OffenseArgs(40, stats0, 4, Basic, Special, 1);
        Assert.That(o, Has.Length.EqualTo(32));
        Assert.That(o[1], Is.EqualTo(100)); Assert.That(o[2], Is.EqualTo(120)); Assert.That(o[4], Is.EqualTo(4));
        Assert.That(o[7], Is.EqualTo(30), "m_38: (__int64) of the float");
        Assert.That(o[8], Is.EqualTo((double)(50f * 0.01f)), "m_40 * 0.01f in float");
        Assert.That(o[10], Is.EqualTo((double)(3f * 0.01f)));
        Assert.That(o[25], Is.EqualTo(100)); Assert.That(o[28], Is.EqualTo(1));
        Assert.That(CombatScore.OffenseArgs(40, stats0, 4, Basic, (s, t) => s == 0x23 ? null : 0f, 1), Is.Null, "a stat not known");
        var d = CombatScore.DefenseArgs(40, stats0, Basic, Special);
        Assert.That(d, Has.Length.EqualTo(20));
        Assert.That(d[1], Is.EqualTo(63));
        Assert.That(d[15], Is.EqualTo((double)(1f * 0.01f)), "m_58: GetBasicStat(0x15) * 0.01f");
    }

    [Test]
    public void CriticalDamageRateIsClamped()
    {
        Assert.That(CombatScore.CriticalDamageRate(0, 0f, 0), Is.EqualTo(1f));
        Assert.That(CombatScore.CriticalDamageRate(500, 0f, 0), Is.EqualTo(1.5f));
        Assert.That(CombatScore.CriticalDamageRate(5000, 0f, 0), Is.EqualTo(2.5f));
        Assert.That(CombatScore.CriticalDamageRate(5000, 0f, 100), Is.EqualTo(2.6f), "the bonus goes past the clamp");
    }
}
