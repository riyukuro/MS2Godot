using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The worn lapenshard table (CSceneStateGame::OnLapenshard, scenestategame.cpp:8743).</summary>
public class LapenshardDataTests
{
    [Test]
    public void ModeZeroReplacesTheTableAndTheOthersEditIt()
    {
        var d = new LapenshardData();
        d.Apply(new LapenshardMessage.Equipped(5, 70));
        Assert.That(d.Apply(new LapenshardMessage.Worn(new[] { (1, 10), (2, 20) })), Is.True);
        Assert.That((d.GetItemId(1), d.GetItemId(2), d.GetItemId(5)), Is.EqualTo((10, 20, 0)), "ClearLapenshards first");
        d.Apply(new LapenshardMessage.Equipped(2, 21));
        Assert.That(d.GetItemId(2), Is.EqualTo(21));
        d.Apply(new LapenshardMessage.Unequipped(1));
        Assert.That((d.GetItemId(1), d.Count), Is.EqualTo((0, 1)));
        Assert.That(d.Apply(new LapenshardMessage.Preview(5)), Is.False, "the upgrade tab's kinds leave it alone");
        Assert.That(d.Count, Is.EqualTo(1));
    }
}
