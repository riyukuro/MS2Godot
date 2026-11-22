using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>StateReact (statereact.cpp) for the own pc: the react cast (0x9f) and the create animation (0x9e).</summary>
public class ReactStateTests
{
    sealed class Host : IReactStateHost
    {
        public bool HasPlayer { get; set; } = true;
        public bool HasBar = true;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<string> Anims = new();
        public CastingParam Cast;
        public ICastingEventListener Listener;
        public int Cancels, Stops;
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public bool StartCasting(ICastingEventListener listener, CastingParam param)
        {
            if (!HasBar) return false;
            Listener = listener;
            Cast = param;
            return true;
        }
        public void CancelCasting() => Cancels++;
        public void PlayPlayerAnim(string animName) => Anims.Add(animName);
        public void StopPlayerAnim() => Stops++;
        public string CommonByKey(string key) => "T:" + key;
    }

    // reactor 40: react time 3000, reach 150, react anims R_A/R_B, create anims C_A; state 1 has the action key s_open
    static ReactorTable Table() => new(
        XDocument.Parse(@"<ms2>
            <reactorState id='1' idleAni='Idle_A' actionStringKey='s_open'/>
        </ms2>"),
        XDocument.Parse(@"<ms2>
            <reactor id='40' reactorStateList='1' reactDistance='150' reactTimeMSec='3000' reactAnimation='R_A,R_B' createAnimation='C_A'/>
        </ms2>"));

    static (FieldReactors, FieldReactors.Reactor) Field(Vector3 pos)
    {
        var f = new FieldReactors(Table());
        var r = f.Add(new ReactorAdd(7, "box", 40, pos, Vector3.Zero, new ReactorState(0, 0, new long[0], 0)), out _);
        return (f, r);
    }

    static (ReactState, Host, FieldReactors, FieldReactors.Reactor) Start(bool bar = true)
    {
        var (f, r) = Field(new Vector3(100, 0, 0));
        var host = new Host { HasBar = bar };
        var st = new ReactState(host, () => f) { Rng = new Random(1) };
        return (st, host, f, r);
    }

    [Test]
    public void ReactCastPlaysAReactAnimationAndRunsTheBarWithTheReactTimeAndActionString()
    {
        var (st, host, _, r) = Start();
        Assert.That(st.Begin(ReactState.KindReact, r.Data, "box"), Is.True);
        Assert.That(st.Active, Is.True);
        Assert.That(st.CurStateId(1), Is.EqualTo(0x3d));
        Assert.That(host.Anims, Has.Count.EqualTo(1));
        Assert.That(host.Anims[0], Is.AnyOf("R_A", "R_B"));
        Assert.That(st.Anim, Is.EqualTo(host.Anims[0]), "the pick is kept in m_strAnim");
        Assert.That(host.Listener, Is.SameAs(st));
        Assert.That(host.Cast.Duration, Is.EqualTo(3000), "GetReactorInfo: reactTimeMSec");
        Assert.That(host.Cast.Text, Is.EqualTo("T:s_open"), "GetReactorInfo: the current state's action string");
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void WithoutTheCurrentStateDataTheCastKeepsTheDefaultTimeAndCaption()
    {
        // CReactor::GetReactorInfo (reactor.cpp:251) returns before m_nReactTimeMSec and m_strAction without the state's data
        var (st, host, f, r) = Start();
        f.Update(7, new ReactorState(5, 0, new long[0], 0));
        Assert.That(r.State, Is.Null);
        Assert.That(st.Begin(ReactState.KindReact, r.Data, "box"), Is.True);
        Assert.That(host.Cast.Duration, Is.EqualTo(0), "not the data's reactTimeMSec 3000");
        Assert.That(host.Cast.Text, Is.EqualTo(""));
    }

    [Test]
    public void TheDataAnimationWinsOverThePick()
    {
        var (st, host, _, r) = Start();
        st.Begin(ReactState.KindReact, r.Data, "box", "Given_A");
        Assert.That(host.Anims, Is.EqualTo(new[] { "Given_A" }));
    }

    [Test]
    public void AFinishedCastSendsStateReactOnceAndLeavesCancellingTheBar()
    {
        var (st, host, _, r) = Start();
        st.Begin(ReactState.KindReact, r.Data, "box");
        st.Tick(new Vector3(100, 50, 0), false);
        Assert.That(host.Sent, Is.Empty, "the cast still runs");
        st.OnCastingFinished();
        st.Tick(new Vector3(100, 50, 0), false);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        Assert.That(host.Sent[0], Is.EqualTo(new StateReactRequest(7)));
        Assert.That(st.Active, Is.False);
        Assert.That(st.CurStateId(1), Is.EqualTo(1));
        Assert.That(host.Cancels, Is.EqualTo(1), "vfunc_5 cancels the bar on leaving 0x9f");
        st.Tick(new Vector3(100, 50, 0), false);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void LeavingTheReachMovingOrLosingTheTargetEndsWithoutASend()
    {
        var (st, host, f, r) = Start();
        st.Begin(ReactState.KindReact, r.Data, "box");
        st.Tick(new Vector3(100, 151, 0), false);
        Assert.That(st.Active, Is.False, "farther than reactDistance");

        st.Begin(ReactState.KindReact, r.Data, "box");
        st.Tick(new Vector3(100, 0, 0), true);
        Assert.That(st.Active, Is.False, "moving (FUN_1416749c0) goes to state 2");

        st.Begin(ReactState.KindReact, r.Data, "box");
        f.Remove(7);
        st.OnCastingFinished();
        st.Tick(new Vector3(100, 0, 0), false);
        Assert.That(st.Active, Is.False, "FUN_140b8ded0: the target is gone");
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void NoTargetAtTheStartGoesIdleWithoutACast()
    {
        var (st, host, _, r) = Start();
        Assert.That(st.Begin(ReactState.KindReact, r.Data, "other"), Is.False);
        Assert.That(st.Active, Is.False);
        Assert.That(host.Listener, Is.Null);
    }

    [Test]
    public void WithoutACastingBarTheStateStaysAndNeverSends()
    {
        var (st, host, _, r) = Start(bar: false);
        Assert.That(st.Begin(ReactState.KindReact, r.Data, "box"), Is.True);
        for (int i = 0; i < 10; i++) st.Tick(new Vector3(100, 0, 0), false);
        Assert.That(st.Active, Is.True);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void TheHeldItemIsSentBeforeTheCast()
    {
        var (st, host, _, r) = Start();
        st.Held = new HeldItem();
        st.Held.Hold(50100001, true);
        st.Begin(ReactState.KindReact, r.Data, "box");
        Assert.That(host.Sent, Has.Count.EqualTo(1), "PC::SendRequestCube");
        Assert.That(host.Sent[0], Is.Not.InstanceOf<StateReactRequest>());
    }

    [Test]
    public void CreateAnimationEndsWithItsSequenceAndNeedsNoTarget()
    {
        var (st, host, _, r) = Start();
        Assert.That(st.Begin(ReactState.KindCreate, r.Data, "gone"), Is.True);
        Assert.That(host.Anims, Is.EqualTo(new[] { "C_A" }));
        Assert.That(host.Listener, Is.Null, "no cast for 0x9e");
        st.Tick(Vector3.Zero, false);
        Assert.That(st.Active, Is.True);
        st.SequenceEnded();
        Assert.That(st.Active, Is.False);
        Assert.That(host.Cancels, Is.EqualTo(0));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void OnReactorModeFiveStartsTheCreateAnimationOnlyWithTheOwnPc()
    {
        var (st, host, f, _) = Start();
        host.HasPlayer = false;
        Assert.That(st.BeginCreate(f.DataOf(40)), Is.False, "no local pc: assert and return");
        Assert.That(st.Active, Is.False);
        Assert.That(host.Anims, Is.Empty);

        host.HasPlayer = true;
        Assert.That(st.BeginCreate(f.DataOf(41)), Is.False, "unknown reactor data");
        Assert.That(st.BeginCreate(f.DataOf(40)), Is.True);
        Assert.That(host.Anims, Is.EqualTo(new[] { "C_A" }));
        Assert.That(st.CurStateId(1), Is.EqualTo(ReactState.StateId));
        Assert.That(host.Listener, Is.Null);
        st.Tick(new Vector3(9999, 0, 0), false);
        Assert.That(st.Active, Is.True, "no target: no reach check");
        st.SequenceEnded();
        Assert.That(st.Active, Is.False, "EndOfSequence: back to idle");

        st.BeginCreate(f.DataOf(40));
        st.Tick(Vector3.Zero, true);
        Assert.That(st.Active, Is.False, "a move ends it (state 2)");
    }

    [Test]
    public void ARunningReactRefusesANewOneThroughTheStateGate()
    {
        var (st, _, _, r) = Start();
        st.Begin(ReactState.KindReact, r.Data, "box");
        Assert.That(FieldReactors.Use(r.Data, 0, st.CurStateId(1), false), Is.EqualTo(FieldReactors.UseOutcome.None));
    }

    [Test]
    public void ARunningReactBlocksTheNpcAndObjectInteractionsButNotTheTaxiPoll()
    {
        var (st, _, _, r) = Start();
        var taxi = new TaxiStationAction { Send = _ => { } };
        var station = new Ms2.Formats.Model.Vec3(0, 0, 0);
        taxi.AddStation(station, false);
        var found = new HashSet<int> { 2000001 };

        Assert.That(InteractKey.StateId(null, null, st, 1), Is.EqualTo(1));
        Assert.That(LiveInteractObjects.StateAllowsInteraction(InteractKey.StateId(null, null, st, 1)), Is.True);

        st.Begin(ReactState.KindReact, r.Data, "box");
        int id = InteractKey.StateId(null, null, st, 1);
        Assert.That(id, Is.EqualTo(ReactState.StateId), "MyPC::GetCurStateId while StateReact runs");
        Assert.That(LiveInteractObjects.StateAllowsInteraction(id), Is.False, "FUN_141527160: npc (0x1a) and object (0x19) interactions refuse 0x3d");
        Assert.That(taxi.Poll(true, false, true, id, station, null, 2000001, found), Is.True,
            "TaxiStationAction::vfunc_1 refuses only 0x25");
        Assert.That(InteractKey.StateId(null, null, null, 1), Is.EqualTo(1), "no react state yet");
    }

    static FieldEntity InteractEntity(string id, int rowId)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2InteractObject", Name = "obj_" + id };
        raw.Props["interactID"] = rowId.ToString();
        raw.Props["Position"] = "0, 0, 0";
        raw.Props["Rotation"] = "0, 0, 0";
        var flat = new FlatModel { Name = "MS2InteractObject" };
        flat.Mixins.Add("MS2InteractObject");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    [Test]
    public void AServerStateLeavesTheReactAndTheInteractionCastsWithoutASend()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        var (st, host, _, r) = Start();
        var objHost = new LiveInteractObjectsTests.Host();
        var objects = new LiveInteractObjects(objHost, new InteractObjectTable(new Dictionary<int, InteractObjectRow>
        {
            [301] = new() { Id = 301, Type = 1, InteractionDistance = 150, ReactTimeMs = 2000 },
        }));
        objects.Attach(new[] { InteractEntity("box-1", 301) });
        objects.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "box-1", State = 1, Kind = 1 });
        var npcHost = new LiveInteractNpcTests.Host();
        var npcs = new LiveInteractNpc(npcHost);
        npcs.Add(7, LiveInteractNpcTests.Row(), talkable: true, new Ms2.Formats.Model.Vec3(100, 0, 0));
        me.StateEntered += s => InteractKey.LeaveForServerState(s, objects, npcs, st);
        var at = new Ms2.Formats.Model.Vec3(0, 0, 0);

        st.Begin(ReactState.KindReact, r.Data, "box");
        Assert.That(me.ApplyUserState(9, 0x10), Is.False);
        Assert.That(st.Active, Is.False, "vfunc_59(0x10) leaves StateReact while the move state is still idle");
        Assert.That(host.Cancels, Is.EqualTo(1), "vfunc_5 cancels the bar");
        st.OnCastingFinished();
        st.Tick(new Vector3(100, 0, 0), false);
        Assert.That(host.Sent, Is.Empty, "no StateReact after the server's state");

        st.Begin(ReactState.KindReact, r.Data, "box");
        me.ApplyUserState(9, 0x3d);
        Assert.That(st.Active, Is.False, "StateReact::vfunc_25 is false: the same state leaves it too");

        objects.Poll(at);
        Assert.That(objects.Use(at), Is.True);
        npcs.Poll(at);
        Assert.That(npcs.Use(), Is.True);
        me.ApplyUserState(9, 0x1a);
        Assert.That(objects.IsCasting, Is.False, "StateInteraction is left");
        Assert.That(objHost.CastCancels, Is.EqualTo(1));
        Assert.That(npcs.IsCasting, Is.True, "State::vfunc_25 keeps StateInteractionNpc for 0x1a");
        me.ApplyUserState(9, 0xc);
        Assert.That(npcs.IsCasting, Is.False, "the death state leaves it");
        Assert.That(npcHost.Cancels, Is.EqualTo(1));
        npcHost.NowMs += 10_000;
        objHost.NowMs += 10_000;
        npcs.Tick(at, false);
        objects.Tick(at, false);
        Assert.That(npcHost.Sent, Is.Empty);
        Assert.That(objHost.Sent, Has.Count.EqualTo(1), "only the start request, never the complete");

        Assert.That(me.ApplyUserState(9, 0x23), Is.False);
        st.Begin(ReactState.KindReact, r.Data, "box");
        me.ApplyUserState(9, 0x23);
        Assert.That(st.Active, Is.True, "an unregistered state is not entered and leaves nothing");
    }

    [Test]
    public void FindByNameUsesTheNameMap()
    {
        var (f, r) = Field(Vector3.Zero);
        Assert.That(f.FindByName("box"), Is.SameAs(r));
        Assert.That(f.FindByName("nope"), Is.Null);
        Assert.That(f.FindByName(null), Is.Null);
    }

    [Test]
    public void TickAllocatesNothing()
    {
        var (st, _, _, r) = Start();
        st.Begin(ReactState.KindReact, r.Data, "box");
        var at = new Vector3(100, 10, 0);
        for (int i = 0; i < 100; i++) st.Tick(at, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) st.Tick(at, false);
        long used = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(st.Active, Is.True);
        Assert.That(used, Is.LessThan(512), "100000 ticks");
    }
}
