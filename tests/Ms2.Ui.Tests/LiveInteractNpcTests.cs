using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class LiveInteractNpcTests
{
    sealed class Host : IInteractNpcHost
    {
        public bool HasPlayer { get; set; } = true;
        public float InteractionDistance { get; set; } = 200f;
        public Vec3 PlayerForward { get; set; } = new(1, 0, 0);
        public int? PvPPoint { get; set; }
        public bool CanEnterInteractNpcState { get; set; } = true;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<string> Anims = new();
        public int Casts, Cancels, AnimStops, LastDuration;
        public bool LastShowBar;
        public ICastingEventListener Listener;

        public void Send(IOutPacket packet) => Sent.Add(packet);
        public void StartCasting(ICastingEventListener listener, int durationMs, bool showBar)
        { Casts++; Listener = listener; LastDuration = durationMs; LastShowBar = showBar; }
        public void CancelCasting() => Cancels++;
        public void PlayPlayerAnim(string animName, bool loop) => Anims.Add(animName);
        public void StopPlayerInteractAnim() => AnimStops++;
    }

    static NpcRow Row(int friendly = 2, string function = "Fn", string anim = "", int castMs = 1500, bool showBar = true, params string[] tags) =>
        new() { Id = 11000001, Friendly = friendly, InteractFunction = function, InteractCastingAnimation = anim,
                InteractCastingTime = castMs, InteractShowCastingBar = showBar, PropertyTags = tags };

    static readonly Vec3 Origin = new(0, 0, 0);

    [Test]
    public void TalkableFlagFromAddAndInteractNpcGatesThePick()
    {
        // FindInteractNpc rejects a non-talkable npc (actionkeyobject.cpp:1677); 0x59 sets the flag (scenestategame.cpp:3634)
        var live = new LiveInteractNpc(new Host());
        live.Add(5, Row(), talkable: false, new Vec3(100, 0, 0));
        Assert.That(live.Poll(Origin), Is.False);

        Assert.That(live.Apply(new InteractNpcMessage(5, true, true)), Is.True);
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Current.ObjectId, Is.EqualTo(5));

        live.Apply(new InteractNpcMessage(5, true, false));
        Assert.That(live.Poll(Origin), Is.False);
    }

    [Test]
    public void InteractNpcForMissingNpcIsIgnored()
    {
        var live = new LiveInteractNpc(new Host());
        live.Add(5, Row(), talkable: false, new Vec3(100, 0, 0));
        Assert.That(live.Apply(new InteractNpcMessage(6, true, true)), Is.False);
        Assert.That(live.Apply(new InteractNpcMessage(5, false, false)), Is.False);
        Assert.That(live.Find(5).Talkable, Is.False);
    }

    [Test]
    public void NearestIsCheckedWithoutFallingBackToAFartherNpc()
    {
        // FindNearestNpc picks the nearest friendly npc, then FindInteractNpc checks only that one (:1672-1679)
        var live = new LiveInteractNpc(new Host());
        live.Add(1, Row(function: ""), talkable: true, new Vec3(50, 0, 0));    // nearest, no interact function
        live.Add(2, Row(), talkable: true, new Vec3(150, 0, 0));
        Assert.That(live.Poll(Origin), Is.False);

        live.Remove(1);
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Current.ObjectId, Is.EqualTo(2));
    }

    [Test]
    public void DeadOrOutOfRangeIsNotPicked()
    {
        var live = new LiveInteractNpc(new Host { InteractionDistance = 120f });
        live.Add(3, Row(), talkable: true, new Vec3(150, 0, 0));
        Assert.That(live.Poll(Origin), Is.False);
        live.Move(3, new Vec3(0, 0, 100));     // 3D distance
        Assert.That(live.Poll(Origin), Is.True);
        live.SetDead(3, true);
        Assert.That(live.Poll(Origin), Is.False);
    }

    [Test]
    public void FriendlyTwoIsTriedBeforeKindOneOrThePvPPoint()
    {
        var host = new Host();
        var live = new LiveInteractNpc(host);
        live.Add(1, Row(friendly: 1), talkable: true, new Vec3(30, 0, 0));
        live.Add(2, Row(friendly: 2), talkable: true, new Vec3(150, 0, 0));
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Current.ObjectId, Is.EqualTo(2));
        live.Remove(2);
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Current.ObjectId, Is.EqualTo(1));
        host.PvPPoint = 3;
        Assert.That(live.Poll(Origin), Is.False);
    }

    [Test]
    public void NpcBehindThePlayerIsSkippedUnlessDummyNpc()
    {
        // worldservice_1.cpp:6467: the front plane, property tag 8 "dummy_npc" exempt (npcdata.cpp:1345)
        var live = new LiveInteractNpc(new Host { PlayerForward = new Vec3(1, 0, 0) });
        live.Add(4, Row(), talkable: true, new Vec3(-60, 0, 0));
        Assert.That(live.Poll(Origin), Is.False);
        live.Add(4, Row(tags: "dummy_npc"), talkable: true, new Vec3(-60, 0, 0));
        Assert.That(live.Poll(Origin), Is.True);
    }

    [Test]
    public void EqualDistanceGoesToTheLowerObjectId()
    {
        var live = new LiveInteractNpc(new Host { PlayerForward = new Vec3(0, 1, 0) });
        live.Add(9, Row(), talkable: true, new Vec3(-50, 50, 0));
        live.Add(8, Row(), talkable: true, new Vec3(50, 50, 0));
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Current.ObjectId, Is.EqualTo(8));
    }

    [Test]
    public void UseCastsWithRowTimeAndAnimationThenSendsOnFinish()
    {
        // StateInteractionNpc::vfunc_4 (stateinteractdata.cpp:575-608) and vfunc_8 (:616-650)
        var host = new Host();
        var live = new LiveInteractNpc(host);
        live.Add(7, Row(anim: "Interaction_Hold_A", castMs: 2500, showBar: false), talkable: true, new Vec3(100, 0, 0));
        Assert.That(live.Poll(Origin), Is.True);
        Assert.That(live.Use(), Is.True);
        Assert.That(host.Anims, Is.EqualTo(new[] { "Interaction_Hold_A" }));
        Assert.That(host.LastDuration, Is.EqualTo(2500));
        Assert.That(host.LastShowBar, Is.False);

        live.Tick(Origin, false);
        Assert.That(host.Sent, Is.Empty);
        host.Listener.OnCastingFinished();
        live.Tick(Origin, false);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
        Assert.That(host.Sent[0], Is.EqualTo(new InteractNpcRequest(7)));
        Assert.That(live.IsCasting, Is.False);
        live.Tick(Origin, false);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void EmptyCastingAnimationFallsBackToObjectReactA()
    {
        var host = new Host();
        var live = new LiveInteractNpc(host);
        live.Add(7, Row(anim: ""), talkable: true, new Vec3(100, 0, 0));
        live.Poll(Origin);
        live.Use();
        Assert.That(host.Anims, Is.EqualTo(new[] { "Object_React_A" }));
        Assert.That(host.LastShowBar, Is.True);
    }

    [Test]
    public void MovingOrLeavingRangeCancelsWithoutSending()
    {
        var host = new Host();
        var live = new LiveInteractNpc(host);
        live.Add(7, Row(), talkable: true, new Vec3(100, 0, 0));
        live.Poll(Origin); live.Use();
        live.Tick(Origin, playerMoved: true);
        Assert.That(live.IsCasting, Is.False);
        Assert.That(host.Cancels, Is.EqualTo(1));

        live.Poll(Origin); live.Use();
        live.Tick(new Vec3(-300, 0, 0), playerMoved: false);
        Assert.That(live.IsCasting, Is.False);
        Assert.That(host.Cancels, Is.EqualTo(2));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FinishedCastWithNpcGoneSendsNothing()
    {
        var host = new Host();
        var live = new LiveInteractNpc(host);
        live.Add(7, Row(), talkable: true, new Vec3(100, 0, 0));
        live.Poll(Origin); live.Use();
        live.Remove(7);
        host.Listener.OnCastingFinished();
        live.Tick(Origin, false);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(live.IsCasting, Is.False);
    }

    [Test]
    public void StateGateBlocksUse()
    {
        var host = new Host { CanEnterInteractNpcState = false };
        var live = new LiveInteractNpc(host);
        live.Add(7, Row(), talkable: true, new Vec3(100, 0, 0));
        live.Poll(Origin);
        Assert.That(live.Use(), Is.False);
        Assert.That(host.Casts, Is.EqualTo(0));
    }

    [Test]
    public void NpcRowParsesInteractElement()
    {
        // CNpcData::LoadXml <interact> (npcdata.cpp:1595-1602); defaults npcdata.cpp:593 (bar shown)
        var x = XElement.Parse("<npc id=\"11000001\"><environment friendly=\"2\"><interact interactFunction=\"Mailbox\" " +
            "interactCastingAnimation=\"Emotion_Hello_A\" interactCastingTime=\"1200\" interactCoolTime=\"5\" interactIsShowCastingBar=\"0\"/></environment></npc>");
        var row = NpcTable.ParseRow(x, Array.Empty<EffectDummy>());
        Assert.That(row.InteractFunction, Is.EqualTo("Mailbox"));
        Assert.That(row.InteractCastingAnimation, Is.EqualTo("Emotion_Hello_A"));
        Assert.That(row.InteractCastingTime, Is.EqualTo(1200));
        Assert.That(row.InteractCoolTime, Is.EqualTo(5));
        Assert.That(row.InteractShowCastingBar, Is.False);

        var bare = NpcTable.ParseRow(XElement.Parse("<npc id=\"2\"><environment friendly=\"2\"/></npc>"), Array.Empty<EffectDummy>());
        Assert.That(bare.InteractFunction, Is.EqualTo(""));
        Assert.That(bare.InteractShowCastingBar, Is.True);
    }

    [Test]
    public void PollAndTickAllocateNothing()
    {
        var host = new Host();
        var live = new LiveInteractNpc(host);
        for (int i = 0; i < 40; i++) live.Add(100 + i, Row(), talkable: i % 2 == 0, new Vec3(10 * i, 5, 0));
        var pos = new Vec3(0, 0, 0);
        live.Poll(pos); live.Use();
        for (int i = 0; i < 20; i++) { live.Poll(pos); live.Tick(pos, false); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) { live.Poll(pos); live.Tick(pos, false); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Poll+Tick allocated {allocated} B over 600 frames");
    }
}
