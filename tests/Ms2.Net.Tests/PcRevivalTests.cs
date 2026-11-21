using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Another PC's revival bookkeeping: PC m_n10e8 / m_n10ec / m_n10f0 and the expiry PC::FUN_141661ac0.</summary>
public class PcRevivalTests {
    [Test]
    public void TheConfirmCountExpiresAtItsEndTickAndTheFlagStays() {
        var r = new PcRevival();
        r.Confirm(5000, 2);
        Assert.That((r.EndTick, r.Count, r.Blocked), Is.EqualTo((5000u, 2, true)));
        Assert.That(r.Expire(4999), Is.False, "before the end tick");
        Assert.That(r.Expire(5000), Is.True);
        Assert.That((r.EndTick, r.Count, r.Blocked), Is.EqualTo((0u, 0, false)));
        Assert.That(r.Expire(9999), Is.False, "no end tick: nothing to expire");

        r.Confirm(6000, 1);
        r.Died();                                 // vfunc_137 with a confirm count
        Assert.That(r.Flag, Is.True);
        r.Expire(6000);
        Assert.That((r.Count, r.Flag, r.Blocked), Is.EqualTo((0, true, true)), "FUN_141661ac0 leaves m_n10f0");
    }

    [Test]
    public void AnEndTickOfZeroNeverExpires() {
        var r = new PcRevival();
        r.Confirm(0, 3);
        Assert.That(r.Expire(uint.MaxValue), Is.False);
        Assert.That(r.Count, Is.EqualTo(3));
    }

    [Test]
    public void FieldAddUserCarriesTheThree() {
        var r = new PcRevival();
        r.Apply(new FieldAddUser { ObjectId = 5, User = new UserBlock { Name = "b" }, RevivalEndTick = 9000, RevivalCount = 3, RevivalFlag = true });
        Assert.That((r.EndTick, r.Count, r.Flag), Is.EqualTo((9000u, 3, true)));
        r.Died();
        Assert.That(r.Expire(9000), Is.True);
        Assert.That(r.Blocked, Is.True);
    }
}
