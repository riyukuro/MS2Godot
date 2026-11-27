using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The buff icon balloon (uibuffballoon, UIBuffBalloon) run by its own ActionScript and fed by an AdditionalBuffList's
/// AddBuffUI / RemoveBuffUI (additionalcli_2.cpp:1243-1251, :1275-1280) (needs MS2_DATA).
/// </summary>
public class BuffBalloonTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static GameTables Tables()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        return tables;
    }

    static UiHost Load(UiController c)
    {
        var m = new UiModel(Tables());
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    static BuffPacket Add(int key, uint id, ushort level, uint end) =>
        new(BuffPacket.Add, 5, key, 5, 0, new BuffState(0, end, id, level, 1, 1), 0);

    static BuffPacket Remove(int key) => new(BuffPacket.Remove, 5, key, 5, 0, null, null);

    sealed class Recorder : IBuffBalloon
    {
        public readonly List<string> Calls = new();
        public void Show(int key, string image, uint time) => Calls.Add($"show {key} {image} {time}");
        public void Hide(int key) => Calls.Add("hide " + key);
    }

    static AdditionalLevelData BalloonLevel(uint id, ushort level) => id switch
    {
        1 => new AdditionalLevelData { Level = level, IconOnOff = 1, Icon = "a.png", IconBalloonOnOff = true },
        2 => new AdditionalLevelData { Level = level, IconOnOff = 1, Icon = "b.png" },
        3 => new AdditionalLevelData { Level = level, IconOnOff = 1, Icon = "c.png", IconBalloonOnOff = true },
        _ => null,
    };

    [Test]
    public void TheListGivesTheBalloonOnlyLevelsWithIconBalloonOnOff()
    {
        // AddBuffUI / RemoveBuffUI: m_bIconBalloonOnOff gates FUN_14128ecf0(key, m_strIcon, GetRemainTicks) and FUN_14128edb0(key)
        var r = new Recorder();
        uint now = 400;
        var list = new AdditionalBuffList(BalloonLevel) { UpperTargetId = 5, Balloon = () => r, ServerTick = () => now };
        list.Apply(Add(10, 1, 1, 1000));
        list.Apply(Add(11, 2, 1, 1000));   // no balloon on this level
        list.Apply(Add(12, 3, 1, 0));      // no end tick: time 0
        list.Apply(Remove(11));
        list.Apply(Remove(10));
        list.Apply(Remove(12));
        Assert.That(r.Calls, Is.EqualTo(new[] { "show 10 a.png 600", "show 12 c.png 0", "hide 10", "hide 12" }));
    }

    [Test]
    public void ClearingTheListHidesItsBalloons()
    {
        var r = new Recorder();
        var list = new AdditionalBuffList(BalloonLevel) { UpperTargetId = 5, Balloon = () => r };
        list.Apply(Add(10, 1, 1, 1000));
        list.ClearAll();
        Assert.That(r.Calls, Is.EqualTo(new[] { "show 10 a.png 1000", "hide 10" }));
    }

    [Test]
    public void EveryBalloonLevelReachesTheBalloonWithItsKeyIconAndTime()
    {
        // the whole list: every additional effect level with iconBalloonOnOff, added then removed
        var t = Tables().Additional;
        var r = new Recorder();
        var list = new AdditionalBuffList((id, lv) => t.GetLevel(id, lv)) { UpperTargetId = 5, Balloon = () => r };
        int key = 100, levels = 0, plain = 0;
        var want = new List<string>();
        foreach (var d in t.LoadAll().OrderBy(d => d.Id))
            foreach (var lv in d.Levels)
            {
                // GetLevel's lower bound: only the levels it finds by their own number are reachable
                if (!ReferenceEquals(t.GetLevel(d.Id, lv.Level), lv)) continue;
                key++;
                list.Apply(Add(key, d.Id, lv.Level, 5000));
                list.Apply(Remove(key));
                if (lv.IconBalloonOnOff)
                {
                    levels++;
                    want.Add($"show {key} {lv.Icon} 5000");
                    want.Add("hide " + key);
                }
                else plain++;
            }
        TestContext.Progress.WriteLine($"buff balloon: {levels} balloon levels, {plain} others");
        Assert.That(levels, Is.GreaterThan(50), "levels with an icon balloon");
        Assert.That(r.Calls, Is.EqualTo(want));
    }

    [Test]
    public void ABalloonIsHeldOverItsActorUntilItsKeyIsHidden()
    {
        long now = 10_000;
        var c = new BuffBalloonController { TickMs = () => now };
        var h = Load(c);
        var pc = new BuffBalloonController.Actor { ObjectId = 7, Offset = 20, Distance = 100 };
        // PC::vfunc_84: none while ProgressiveLoading holds the model back
        Assert.That(c.BalloonOf(pc, pcNotAdded: true), Is.Null);
        Assert.That(c.ClipsMade, Is.EqualTo(0));
        var e = c.BalloonOf(pc);
        Assert.That(e, Is.Not.Null);
        Assert.That(c.BalloonOf(pc), Is.SameAs(e));
        Assert.That(c.ClipsMade, Is.EqualTo(1));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False, "a new clip starts hidden");

        var list = new AdditionalBuffList(BalloonLevel) { UpperTargetId = 5, Balloon = () => c.BalloonOf(pc) };
        list.Apply(Add(10, 1, 1, 1000));
        Assert.That((e.Active, e.Held, e.Key, e.Image), Is.EqualTo((true, true, 10, "a.png")));
        c.Process(_ => (500f, 400f, 6f));
        Assert.That((e.Opens, e.Opened), Is.EqualTo((1, "a.png")));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        // vfunc_6: SetPositionFloat(x - 55, y + offset + pitch shift - 110)
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "x")), Is.EqualTo(445));
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "y")), Is.EqualTo(400 + 20 + 6 - 110));

        // held: still shown long after its time, and not opened again
        now += 60_000;
        c.Process(_ => (500f, 400f, 6f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True, "held past the timer");
        Assert.That(e.Opens, Is.EqualTo(1));

        // past BuffBallonDistance: hidden; back in range: opened again
        pc.Distance = 2600;
        c.Process(_ => (500f, 400f, 6f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False, "out of range");
        pc.Distance = 100;
        c.Process(_ => (510f, 400f, 6f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        Assert.That(e.Opens, Is.EqualTo(2));
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "x")), Is.EqualTo(455));

        // another buff on the shown balloon: the newest icon is opened, the timer restarted
        list.Apply(Add(11, 3, 1, 1000));
        Assert.That((e.Key, e.Image, e.Reopen), Is.EqualTo((11, "c.png", true)));
        c.Process(_ => (510f, 400f, 6f));
        Assert.That((e.Opens, e.Opened), Is.EqualTo((3, "c.png")));

        // FUN_14128edb0: a key that is not the shown one does nothing; the shown one stops it
        list.Apply(Remove(10));
        Assert.That(e.Active, Is.True, "an older key");
        list.Apply(Remove(11));
        Assert.That((e.Active, e.Held), Is.EqualTo((false, false)));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        c.Process(_ => (510f, 400f, 6f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False, "an inactive balloon is skipped");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void AReleasedBalloonIsPooledAndHandedToTheNextActor()
    {
        var c = new BuffBalloonController();
        var h = Load(c);
        var a = new BuffBalloonController.Actor { ObjectId = 7 };
        var b = new BuffBalloonController.Actor { ObjectId = 8 };
        var e = c.BalloonOf(a);
        e.Show(10, "a.png", 1000);
        c.Process(_ => (500f, 400f, 0f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        c.Remove(a);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        Assert.That((e.Actor, e.Key, e.Image, e.Active), Is.EqualTo(((BuffBalloonController.Actor)null, 0, "", false)));
        var e2 = c.BalloonOf(b);
        Assert.That(e2, Is.SameAs(e), "the pooled entity");
        Assert.That(c.ClipsMade, Is.EqualTo(1));
        Assert.That(e2.Actor, Is.SameAs(b));
        Assert.That(c.Entities.Keys, Is.EqualTo(new[] { 8 }));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void NoBalloonOpensBeforeTheCameraIsReady()
    {
        // vfunc_2 returns before TimerEntity::vfunc_2 while the camera is not ready
        bool ready = false;
        var c = new BuffBalloonController { CameraReady = () => ready };
        var h = Load(c);
        var e = c.BalloonOf(new BuffBalloonController.Actor { ObjectId = 7 });
        e.Show(10, "a.png", 1000);
        Assert.That((e.Active, e.Held), Is.EqualTo((false, true)));
        c.Process(_ => (500f, 400f, 0f));
        Assert.That(e.Opens, Is.EqualTo(0));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        ready = true;
        e.Show(11, "b.png", 1000);
        c.Process(_ => (500f, 400f, 0f));
        Assert.That((e.Opens, e.Opened), Is.EqualTo((1, "b.png")));
    }
}
