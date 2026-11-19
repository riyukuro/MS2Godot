using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The HUD player from the live own character (<see cref="PlayerStateLive"/>).</summary>
public class PlayerStateLiveTests
{
    [Test]
    public void ChosenEntryFillsTheUserFieldsBeforeTheOwnFieldAddUser()
    {
        var p = new PlayerState();
        Assert.That(p.Apply(new MyCharacter()), Is.Empty, "nothing known");
        var chosen = new UserBlock { Name = "Gdcap", Level = 2, JobCode = 40, Job = 400, HpMax = 0 };
        var changed = p.Apply(null, chosen);
        Assert.That((p.Name, p.Level, p.JobGroup, p.SubJob), Is.EqualTo(("Gdcap", 2, 40, 400)));
        Assert.That(changed, Is.EquivalentTo(new[] { StatusEvent.Name, StatusEvent.Level, StatusEvent.Job }), "no HP from a 0 max");
    }

    [Test]
    public void StatsWinOverTheUserBlockAndOnlyChangesAreSent()
    {
        var me = new MyCharacter { User = new UserBlock { Name = "Gdcap", Level = 3, JobCode = 40, Job = 400, HpMax = 100, HpCurrent = 90 } };
        var p = new PlayerState();
        p.Apply(me);
        Assert.That((p.HpCur, p.HpMax), Is.EqualTo((90L, 100L)), "the user block's HP before a Stat");
        me.Stats0[4] = 250; me.Stats2[4] = 240;
        me.Stats0[7] = 100; me.Stats2[7] = 60;
        me.Exp = 1234;
        var changed = p.Apply(me);
        Assert.That(changed, Is.EquivalentTo(new[] { StatusEvent.Hp, StatusEvent.Sp, StatusEvent.Exp }));
        Assert.That((p.HpCur, p.HpMax, p.SpCur, p.SpMax, p.Exp), Is.EqualTo((240L, 250L, 60L, 100L, 1234L)));
        Assert.That(p.Apply(me), Is.Empty);
        me.User.Level = 4;
        Assert.That(p.Apply(me), Is.EqualTo(new[] { StatusEvent.Level }));
    }
}
