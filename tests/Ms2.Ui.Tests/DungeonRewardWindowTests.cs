using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>What the dungeon result dialog object does with its packet and OnOpenDialog (uidungeonrewarddialog.cpp:386, dungeonrecordcli.cpp:306); no data needed.</summary>
public class DungeonRewardWindowTests
{
    sealed class Rig
    {
        public readonly List<string> Log = new();
        public readonly List<DungeonRewardDialogController> Opened = new();
        public bool Open, CanOpen = true;
        public readonly DungeonRewardWindow W;

        public Rig()
        {
            W = new DungeonRewardWindow
            {
                Open = c =>
                {
                    if (!CanOpen) { Log.Add("refused"); return false; }
                    Log.Add("open"); Opened.Add(c); Open = true; return true;
                },
                IsOpen = () => Open,
                CloseWindow = () => { Log.Add("close"); Open = false; },
            };
        }
    }

    static DungeonRewardPacket Pkt(byte mode, Action<ByteWriter> body = null)
    {
        var w = new ByteWriter();
        w.WriteByte(mode);
        body?.Invoke(w);
        return DungeonRewardPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    static void NoRewards(ByteWriter w) { w.WriteInt(0); w.WriteInt(0); }

    static DungeonRewardPacket Result(int seconds) => Pkt(0, w =>
    {
        w.WriteBool(true); w.WriteInt(1); w.WriteBool(false); w.WriteInt(seconds); w.WriteInt(0); w.WriteInt(0);
        w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        NoRewards(w); NoRewards(w);
    });

    [Test]
    public void ASecondResultWhileTheWindowIsOpenClosesItAndOpensTheNewState()
    {
        var r = new Rig();
        r.W.OnPacket(Result(11));
        r.W.OnPacket(Result(22));
        Assert.That(r.Log, Is.EqualTo(new[] { "close", "open", "close", "open" }));
        Assert.That(r.Opened.Select(c => c.Seconds), Is.EqualTo(new[] { 11, 22 }));
        Assert.That(r.W.Stored, Is.Null);
    }

    [Test]
    public void AModeTwoPacketOnlyStoresAndTheOpenRequestShowsThatState()
    {
        var r = new Rig();
        r.W.OnPacket(Pkt(2, w => { w.WriteInt(1); w.WriteByte(1); w.WriteInt(8); w.WriteInt(0); }));
        Assert.That(r.Opened, Is.Empty);
        Assert.That(r.W.Stored, Is.Not.Null);
        var stored = r.W.Stored;
        r.W.OnOpenRequested();
        Assert.That(r.Opened, Is.EqualTo(new[] { stored }));
        Assert.That((stored.Type, stored.Main.GetMeso()), Is.EqualTo((3, 8)));
        Assert.That(r.W.Stored, Is.Null);
        r.W.OnOpenRequested();
        Assert.That(r.Opened.Count, Is.EqualTo(1), "an open window stays");
    }

    [Test]
    public void AnUnknownModeResetsTheStoredStateAndTheOpenRequestShowsAnEmptyOne()
    {
        var r = new Rig();
        r.W.OnPacket(Pkt(2, w => { w.WriteInt(1); w.WriteByte(1); w.WriteInt(8); w.WriteInt(0); }));
        r.W.OnPacket(Pkt(9));
        Assert.That(r.W.Stored, Is.Null);
        r.W.OnOpenRequested();
        Assert.That(r.Opened.Single().Type, Is.EqualTo(0));
    }

    /// <summary>The window's owner hangs <see cref="DungeonRewardWindow.OnSceneLeft"/> on <see cref="SceneChange.SceneLeft"/>, as the HUD does.</summary>
    static void WithSceneLeft(Rig r, Action act)
    {
        SceneChange.SceneLeft += r.W.OnSceneLeft;
        try { act(); } finally { SceneChange.SceneLeft -= r.W.OnSceneLeft; }
    }

    [Test]
    public void ASceneStateChangeClosesTheOpenWindowOnceAndKeepsAStoredState()
    {
        var r = new Rig();
        r.W.OnPacket(Result(11));
        r.Log.Clear();
        WithSceneLeft(r, () =>
        {
            SceneChange.StateChanged();             // the GameToGame move: SetScene -> UISceneGame::vfunc_49 -> CloseClosableDialogs
            SceneChange.StateChanged();
        });
        Assert.That(r.Log, Is.EqualTo(new[] { "close" }));
        Assert.That(r.Open, Is.False);
    }

    [Test]
    public void TheGameScenesLeaveClosesTheOpenWindow()
    {
        var r = new Rig();
        r.W.OnPacket(Result(11));
        r.Log.Clear();
        WithSceneLeft(r, SceneChange.GameSceneLeft);
        Assert.That(r.Log, Is.EqualTo(new[] { "close" }));
    }

    [Test]
    public void WithoutAScenesLeaveTheWindowStaysUp()
    {
        // a same-server field change reaches neither SceneChange.StateChanged nor GameSceneLeft (CSceneStateGame::OnRequestFieldEnter)
        var r = new Rig();
        r.W.OnPacket(Result(11));
        r.Log.Clear();
        WithSceneLeft(r, () => { });
        Assert.That(r.Log, Is.Empty);
        Assert.That(r.Open, Is.True);
    }

    [Test]
    public void ASceneLeaveWithoutAnOpenWindowDoesNothingAndAModeTwoStateStays()
    {
        var r = new Rig();
        r.W.OnPacket(Pkt(2, w => { w.WriteInt(1); w.WriteByte(1); w.WriteInt(8); w.WriteInt(0); }));
        var stored = r.W.Stored;
        r.Log.Clear();
        WithSceneLeft(r, SceneChange.GameSceneLeft);
        Assert.That(r.Log, Is.Empty);
        Assert.That(r.W.Stored, Is.SameAs(stored));
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    [TestCase((byte)3)]
    public void TheStateOfAnOpeningModeIsKeptWhenTheWindowDoesNotOpen(byte mode)
    {
        var r = new Rig { CanOpen = false };
        var p = mode switch
        {
            0 => Result(33),
            1 => Pkt(1, w => { w.WriteInt(2); w.WriteInt(3); NoRewards(w); }),
            _ => Pkt(3, w => { w.WriteBool(false); NoRewards(w); }),
        };
        r.W.OnPacket(p);
        Assert.That(r.Log, Is.EqualTo(new[] { "close", "refused" }));
        var kept = r.W.Stored;
        Assert.That(kept, Is.Not.Null, "the client's dialog object holds the state with no window");
        Assert.That(kept.Type, Is.EqualTo(mode switch { 0 => 1, 1 => 2, _ => 4 }));
        r.CanOpen = true;
        r.W.OnOpenRequested();
        Assert.That(r.Opened, Is.EqualTo(new[] { kept }));
        if (mode == 0) Assert.That(kept.Seconds, Is.EqualTo(33));
        Assert.That(r.W.Stored, Is.Null);
    }
}
