using System.Text;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;
using P = Ms2.Ui.Hud.QuestNavigationController.Point;

namespace Ms2.Ui.Tests;

/// <summary>The quest markers (uiquestnavigation) run by their own ActionScript and fed by QuestNavigationController
/// (needs MS2_DATA).</summary>
public class QuestNavigationTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        return new UiModel(tables);
    }

    static UiHost Load(QuestNavigationController c)
    {
        var m = Model();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    const int Field = 2000001;
    const float Shift = 7f;

    /// <summary>A controller whose world has every object found, alive and active, projected to (x, y) of the point.</summary>
    static QuestNavigationController World() => new()
    {
        FieldId = () => Field,
        CanShowBeam = () => true,
        PlayerPosition = () => new P(10, 20, 0),
        NpcDead = _ => false,
        NpcAnchor = id => new P(100 + id, 200, 0),
        PropActive = _ => true,
        PropPosition = _ => new P(300, 310, 0),
        LiftState = _ => 0,
        LiftPosition = _ => new P(400, 410, 0),
        PortalPosition = _ => new P(500, 510, 0),
        PortalShown = _ => true,
        Project = p => (p.X, p.Y, Shift),
        NpcNametagOffset = 12f,
    };

    static int Children(UiHost h) => (int)Rt.ToNumber(h.Get(h.Entity, "numChildren"));
    static double Num(UiHost h, object o, string member) => Rt.ToNumber(h.Get(o, member));

    [Test]
    public void EachMarkerIsMadeFromItsClassWithItsKeyAsId()
    {
        var c = World();
        var h = Load(c);
        var pc = c.CompleteMyPc();
        var npc = c.AddNpc(7, 11000001);
        var it = c.AddInteract("box01", 10000123, 1);
        var lift = c.AddLiftable("lift01", 55);
        var portal = c.AddPortal(3);
        Assert.That(h.RT.Errors, Is.Empty);
        foreach (var (m, key) in new[] { (pc, "PcHud"), (npc, "npc_7"), (it, "interact_box01"), (lift, "liftable_lift01"), (portal, "portal_3") })
        {
            Assert.That(m.Clip, Is.Not.Null, key);
            Assert.That(Rt.ToString(h.Get(m.Clip, "m_id")), Is.EqualTo(key));
            Assert.That(c.Find(key), Is.SameAs(m));
        }
        // only the pc class (UIQuestNavigation_pc_hud) has the distance field
        Assert.That(Rt.IsNullish(h.Get(pc.Clip, "m_distance")), Is.False);
        Assert.That(Rt.IsNullish(h.Get(npc.Clip, "m_distance")), Is.True);
        // made, not added: nothing on the root before a frame shows them
        Assert.That(Children(h), Is.EqualTo(0));
    }

    [Test]
    public void TheTargetIsShownOpaqueTheContextHalfAndTheRestRemoved()
    {
        var c = World();
        var h = Load(c);
        var a = c.AddNpc(1, 11000001);
        var b = c.AddNpc(2, 11000002);
        var other = c.AddNpc(3, 11000003);
        var ctx = new TraceContext { MapId = Field };
        ctx.NpcIds.Add(11000001);
        var r = new TraceResult { Kind = TraceKind.Npc, Context = ctx };
        var wide = new TraceContext { MapId = Field };
        wide.NpcIds.Add(11000001);
        wide.NpcIds.Add(11000002);
        c.Tick(r, wide);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(a.Shown && b.Shown, Is.True);
        Assert.That(other.Shown, Is.False);
        Assert.That(Children(h), Is.EqualTo(2));
        Assert.That(Num(h, a.Clip, "alpha"), Is.EqualTo(1.0).Within(0.01));
        Assert.That(Num(h, b.Clip, "alpha"), Is.EqualTo(0.5).Within(0.01));
        // the npc dies: off, removed from the root
        c.NpcDead = id => id == 2;
        c.Tick(r, wide);
        Assert.That(b.Shown, Is.False);
        Assert.That(Children(h), Is.EqualTo(1));
        // the trace is now done (kind 2): the npc is not the target, only in the context, at 50
        r.Kind = TraceKind.NpcDone;
        c.Tick(r, wide);
        Assert.That(a.Shown, Is.True);
        Assert.That(Num(h, a.Clip, "alpha"), Is.EqualTo(0.5).Within(0.01));
        // another field: the result's npc test fails, the context one has no field test
        c.FieldId = () => Field + 1;
        r.Kind = TraceKind.Npc;
        c.Tick(r, wide);
        Assert.That(Num(h, a.Clip, "alpha"), Is.EqualTo(0.5).Within(0.01));
        c.Tick(r, new TraceContext());
        Assert.That(a.Shown, Is.False);
        Assert.That(Children(h), Is.EqualTo(0));
    }

    [Test]
    public void APcMarkerSitsAboveThePlayerWithTheDistanceText()
    {
        var c = World();
        var h = Load(c);
        var pc = c.CompleteMyPc();
        var r = new TraceResult { Kind = TraceKind.Npc, DistanceText = "123" };
        c.Tick(r, null);
        Assert.That(pc.Shown, Is.True);
        Assert.That(Num(h, pc.Clip, "x"), Is.EqualTo(10));
        Assert.That(Num(h, pc.Clip, "y"), Is.EqualTo(20 + 40 - Shift));
        Assert.That(Rt.ToBoolean(h.Get(pc.Clip, "visible")), Is.True);
        var field = h.Get(pc.Clip, "m_distance");
        Assert.That(Rt.ToString(h.Get(field, "text")), Is.EqualTo("123"));
        // no trace (kind 0) or no beam: off, but the text still follows the result
        r.Kind = TraceKind.None;
        r.DistanceText = "45";
        c.Tick(r, null);
        Assert.That(pc.Shown, Is.False);
        Assert.That(Rt.ToString(h.Get(field, "text")), Is.EqualTo("45"));
        r.Kind = TraceKind.Portal;
        c.CanShowBeam = () => false;
        c.Tick(r, null);
        Assert.That(pc.Shown, Is.False);
    }

    [Test]
    public void AnNpcMarkerSitsAboveItsNametag()
    {
        var c = World();
        var h = Load(c);
        var npc = c.AddNpc(4, 11000001);
        var ctx = new TraceContext();
        ctx.NpcIds.Add(11000001);
        c.Tick(null, ctx);
        Assert.That(Num(h, npc.Clip, "x"), Is.EqualTo(104));
        Assert.That(Num(h, npc.Clip, "y"), Is.EqualTo(200 + Shift - (12f + 30f)));
        // not projected: hidden, still added
        c.Project = _ => null;
        c.Tick(null, ctx);
        Assert.That(npc.Shown, Is.True);
        Assert.That(Rt.ToBoolean(h.Get(npc.Clip, "visible")), Is.False);
    }

    [Test]
    public void InteractLiftableAndPortalFollowTheirObjects()
    {
        var c = World();
        var h = Load(c);
        Assert.That(c.AddInteract("mesh02", 10000124, 2), Is.Null);   // only kind 1 gets a marker
        Assert.That(c.Find("interact_mesh02"), Is.Null);
        var it = c.AddInteract("box01", 10000123, 1);
        var lift = c.AddLiftable("lift01", 55);
        Assert.That(c.AddLiftable("lift01", 56), Is.Null);   // already there: skipped
        Assert.That(c.Find("liftable_lift01").Id, Is.EqualTo(55));
        var portal = c.AddPortal(3);
        var ctx = new TraceContext { MapId = Field };
        ctx.InteractIds.Add(10000123);
        ctx.LiftableIds.Add(55);
        var r = new TraceResult { Kind = TraceKind.Portal, X = 500, Y = 510, Z = 0, Context = ctx };
        c.Tick(r, ctx);
        Assert.That(it.Shown && lift.Shown && portal.Shown, Is.True);
        Assert.That(Num(h, portal.Clip, "alpha"), Is.EqualTo(1.0).Within(0.01));
        Assert.That(Num(h, it.Clip, "y"), Is.EqualTo(310 + Shift));
        // a lift not at state 0 and an inactive prop are off; the portal off when the beam aims elsewhere
        c.LiftState = _ => 1;
        c.PropActive = _ => false;
        r.X = 501;
        c.Tick(r, ctx);
        Assert.That(lift.Shown || it.Shown || portal.Shown, Is.False);
        Assert.That(Children(h), Is.EqualTo(0));
        // the portal has no context test (vfunc_6 0): a portal trace on a hidden portal is off
        r.X = 500;
        c.PortalShown = _ => false;
        c.Tick(r, ctx);
        Assert.That(portal.Shown, Is.False);
    }

    [Test]
    public void RemoveNpcTakesItsClipOffAndUnloadClearsAll()
    {
        var c = World();
        var h = Load(c);
        var npc = c.AddNpc(9, 11000001);
        c.AddPortal(1);
        var ctx = new TraceContext();
        ctx.NpcIds.Add(11000001);
        c.Tick(null, ctx);
        Assert.That(Children(h), Is.EqualTo(1));
        c.RemoveNpc(9);
        Assert.That(c.Find("npc_9"), Is.Null);
        Assert.That(Children(h), Is.EqualTo(0));
        c.Tick(null, ctx);
        c.Unload();
        Assert.That(c.Markers, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MarkersMadeBeforeTheMovieGetTheirClipsOnLoad()
    {
        var c = World();
        var npc = c.AddNpc(5, 11000001);
        Assert.That(npc.Clip, Is.Null);
        var h = Load(c);
        Assert.That(npc.Clip, Is.Not.Null);
        Assert.That(Rt.ToString(h.Get(npc.Clip, "m_id")), Is.EqualTo("npc_5"));
    }

    /// <summary>A list-of-lists model of CAtlMap&lt;CStringA&gt;: head insert, grow past 2.25 x bins to the next prime of n / 0.75.</summary>
    static List<string> AtlOrder(IEnumerable<string> keys)
    {
        uint[] primes = { 17, 23, 29, 37, 41, 53, 67, 83, 103, 131, 163, 211 };
        static uint H(string s) { uint h = 0; foreach (byte b in Encoding.ASCII.GetBytes(s)) h = h * 33 + (uint)(sbyte)b; return h; }
        var bins = Enumerable.Range(0, 17).Select(_ => new List<string>()).ToList();
        int n = 0;
        foreach (var k in keys)
        {
            bins[(int)(H(k) % (uint)bins.Count)].Insert(0, k);
            if (++n > (int)(2.25f * bins.Count))
            {
                uint size = primes.First(p => p >= (uint)(n / 0.75f));
                var nb = Enumerable.Range(0, (int)size).Select(_ => new List<string>()).ToList();
                foreach (var b in bins) foreach (var s in b) nb[(int)(H(s) % size)].Insert(0, s);
                bins = nb;
            }
        }
        return bins.SelectMany(b => b).ToList();
    }

    [Test]
    public void TheFrameWalksTheMarkersInTheHashMapsOrder()
    {
        var c = new QuestNavigationController();
        var keys = new List<string>();
        for (int i = 1; i <= 45; i++) { c.AddNpc(i * 37, 1); keys.Add("npc_" + i * 37); }
        c.CompleteMyPc(); keys.Add("PcHud");
        c.AddPortal(2); keys.Add("portal_2");
        var want = AtlOrder(keys);
        Assert.That(c.Markers.Select(m => m.Key), Is.EqualTo(want));
        // not insertion order, and a set again key keeps its place
        Assert.That(want, Is.Not.EqualTo(keys));
        var again = c.AddNpc(37, 2);
        Assert.That(c.Markers.Select(m => m.Key), Is.EqualTo(want));
        Assert.That(c.Find("npc_37"), Is.SameAs(again));
    }

    [Test]
    public void AFrameWithNothingNewAllocatesNothing()
    {
        var c = World();
        var h = Load(c);
        c.CompleteMyPc();
        for (int i = 0; i < 8; i++) c.AddNpc(i, 11000001 + i % 2);
        c.AddInteract("box01", 10000123, 1);
        c.AddLiftable("lift01", 55);
        c.AddPortal(3);
        var ctx = new TraceContext { MapId = Field };
        ctx.NpcIds.Add(11000001);
        ctx.InteractIds.Add(10000123);
        ctx.LiftableIds.Add(55);
        var r = new TraceResult { Kind = TraceKind.Npc, DistanceText = "12", Context = ctx };
        for (int i = 0; i < 3; i++) c.Tick(r, ctx);
        // the least of up to 5 rounds (Alloc.Least): a round can take the loop's one-time JIT cost, not per frame
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100; i++) c.Tick(r, ctx); }), Is.EqualTo(0), "bytes over 100 frames");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void APcClipWithoutTheDistanceFieldIsAskedOnce()
    {
        // PcHudNavigationEntity::vfunc_4 (uiquestnavigation.cpp:218) asks the clip for m_distance; the clip does not
        // change, so a clip without it is asked once, and later frames allocate nothing
        var c = World();
        var h = Load(c);
        var pc = c.CompleteMyPc();
        h.Set(pc.Clip, "m_distance", null);
        var r = new TraceResult { Kind = TraceKind.Npc, DistanceText = "12" };
        c.Tick(r, null);
        Assert.That(pc.Shown, Is.True);
        // the least of up to 5 rounds (Alloc.Least): a round can take the loop's one-time JIT cost, not per frame
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 100; i++) c.Tick(r, null); }), Is.EqualTo(0), "bytes over 100 frames");
        Assert.That(h.RT.Errors, Is.Empty);
    }
}
