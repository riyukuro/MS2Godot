using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// PCBase's hide reasons on an npc or pet through the one ApplyHideReasons (pcbase.cpp:1155-1203): the pet's reason 1, the
/// quest mask's 5 and the spawn state's 10 share one set, so clearing one while another is set keeps the actor, its name tag
/// and its shadow hidden. Before the merge the spawn reason drove the body alone, outside the set, so its end showed the body
/// of an npc a quest mask hid and the shadow stayed on while the actor was hidden.
/// </summary>
[TestFixture]
public class ActorHideReasonsTests
{
    const int Pet = 1, Quest = NpcQuestVisibility.HideReasonQuest, Spawn = OwnStateExit.SpawnHideReason;

    struct View : IHideReasonView
    {
        public bool Shown, TagHidden, ShadowVisible;
        public int Applies;
        public void SetShown(bool shown) { Shown = shown; Applies++; }
        public void SetTagHidden(bool hidden) => TagHidden = hidden;
        public void SetShadowVisible(bool visible) => ShadowVisible = visible;
    }

    static void Expect(in View v, bool shown, string step)
    {
        Assert.That(v.Shown, Is.EqualTo(shown), $"{step}: actor shown");
        Assert.That(v.TagHidden, Is.EqualTo(!shown), $"{step}: name tag hidden");
        Assert.That(v.ShadowVisible, Is.EqualTo(shown), $"{step}: shadow");
    }

    [Test]
    public void ReasonsOneFiveAndTenInterleave()
    {
        int reasons = 0;
        var v = new View { Shown = true, ShadowVisible = true };

        Assert.That(ActorHideReasons.Apply(ref reasons, Spawn, true, ref v), Is.True);
        Expect(v, false, "spawn on");
        Assert.That(ActorHideReasons.Apply(ref reasons, Quest, true, ref v), Is.True);
        Expect(v, false, "quest on");
        Assert.That(ActorHideReasons.Apply(ref reasons, Spawn, false, ref v), Is.True);
        Expect(v, false, "spawn off, quest still set");
        Assert.That(ActorHideReasons.Apply(ref reasons, Pet, true, ref v), Is.True);
        Expect(v, false, "pet on");
        Assert.That(ActorHideReasons.Apply(ref reasons, Quest, false, ref v), Is.True);
        Expect(v, false, "quest off, pet still set");
        Assert.That(ActorHideReasons.Apply(ref reasons, Spawn, true, ref v), Is.True);
        Expect(v, false, "spawn on again");
        Assert.That(ActorHideReasons.Apply(ref reasons, Pet, false, ref v), Is.True);
        Expect(v, false, "pet off, spawn still set");
        Assert.That(ActorHideReasons.Apply(ref reasons, Spawn, false, ref v), Is.True);
        Expect(v, true, "all off");
        Assert.That(reasons, Is.Zero);
    }

    [Test]
    public void AnUnchangedBitAppliesNothing()
    {
        int reasons = 0;
        var v = new View { Shown = true, ShadowVisible = true };
        Assert.That(ActorHideReasons.Apply(ref reasons, Pet, false, ref v), Is.False, "RemoveHideReason of an unset bit");
        Assert.That(v.Applies, Is.Zero);
        ActorHideReasons.Apply(ref reasons, Pet, true, ref v);
        Assert.That(ActorHideReasons.Apply(ref reasons, Pet, true, ref v), Is.False, "AddHideReason of a set bit");
        Assert.That(v.Applies, Is.EqualTo(1));
        Expect(v, false, "pet on once");
    }
}
