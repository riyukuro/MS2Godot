using System;
using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>What the own pc's react state reads from the game and how it answers.</summary>
public interface IReactStateHost
{
    /// <summary>FUN_1417b84b0 is not null: the state's owner is the own pc.</summary>
    bool HasPlayer { get; }
    void Send(IOutPacket packet);
    /// <summary>GetCastingBar(0x4e72)->StartCasting(this, info) (statereact.cpp:165-168); false when there is no casting bar
    /// (the state then stays without a cast).</summary>
    bool StartCasting(ICastingEventListener listener, CastingParam param);
    /// <summary>GetCastingBar(0x4e72)->FUN_1418e1390 (statereact.cpp:203-206).</summary>
    void CancelCasting();
    /// <summary>PCBase::PlayAnimation(name, 1.0f, false, false) (statereact.cpp:334, 352).</summary>
    void PlayPlayerAnim(string animName);
    /// <summary>The state is left: the played animation no longer holds the body.</summary>
    void StopPlayerAnim();
    /// <summary>GetStringCommonByKey.</summary>
    string CommonByKey(string key);
}

/// <summary>
/// StateReact (state 0x3d, statereact.cpp) for the own pc: vfunc_59(0x3d, StateReactData(reactorId, name, anim), kind, ..).
/// Kind 0x9f (ReactorAction::vfunc_2, actionkeyobject.cpp) plays the reactor's react animation and runs the casting bar
/// for its reactTimeMSec; when the cast finishes the reactor is sent StateReact (CReactor::SendStateReact). Kind 0x9e
/// (OnReactor mode 5) plays the create animation until its sequence ends. <see cref="Tick"/> allocates nothing until the
/// send.
/// </summary>
public sealed class ReactState : ICastingEventListener
{
    public const int StateId = 0x3d;
    public const int KindCreate = 0x9e, KindReact = 0x9f;

    readonly IReactStateHost host;
    readonly Func<FieldReactors> reactors;
    int kind;               // m_n10 while the state runs, 0 otherwise
    string target = "";     // StateReactData::m_strTarget
    bool finished;          // m_n48

    public ReactState(IReactStateHost host, Func<FieldReactors> reactors)
    {
        this.host = host;
        this.reactors = reactors;
    }

    /// <summary>The own pc's held item (PC::GetHeldItem), shared with the interactions.</summary>
    public HeldItem Held { get; set; }
    /// <summary>GetRandomRange.</summary>
    public Random Rng = new();

    public bool Active => kind != 0;
    public int Kind => kind;
    public string Target => target;
    /// <summary>StateReactData::m_strAnim: the animation played (the data's, else the pick).</summary>
    public string Anim { get; private set; } = "";
    /// <summary>MyPC::GetCurStateId: 0x3d while the state runs, else <paramref name="other"/>.</summary>
    public int CurStateId(int other) => kind != 0 ? StateId : other;

    /// <summary>
    /// StateReact::vfunc_4 (@0x140b8d680, statereact.cpp:135-171). FUN_140b8e030 (@0x140b8e030) plays the data's animation,
    /// else a random entry of the reactor data's create (0x9e) or react (0x9f) list (ReactorData::GetCreateAnimation /
    /// GetReactAnimation, reactordata.cpp:223-238). For 0x9f: the held item's SendRequestCube, the target found by name
    /// (else back to idle), then the cast with GetReactorInfo's reactTimeMSec and the current state's action string
    /// (reactor.cpp:251-263). Returns false when the state did not start (no data, or no player for 0x9f).
    /// </summary>
    public bool Begin(int stateKind, ReactorTable.Reactor data, string name, string anim = "")
    {
        if (kind != 0) Leave();
        if (data == null) return false;     // FUN_140b8e030: GetReactorData fails (MS2_ASSERT_AT 0x6e)
        kind = stateKind;
        target = name ?? "";
        finished = false;
        string pick = anim ?? "";
        if (pick.Length == 0)
        {
            var list = stateKind == KindCreate ? data.CreateAnimations : stateKind == KindReact ? data.ReactAnimations : null;
            if (list != null && list.Count > 0) pick = list[Rng.Next(list.Count)];
        }
        Anim = pick;
        if (pick.Length > 0) host.PlayPlayerAnim(pick);
        if (stateKind != KindReact) return true;
        if (!host.HasPlayer) return true;   // the state stays (statereact.cpp:153-154)
        Held?.SendRequestCube(host.Send);
        var r = reactors()?.FindByName(target);
        if (r == null)
        {
            End();      // vfunc_59(1)
            return false;
        }
        var param = new CastingParam { Duration = r.Data?.ReactTimeMSec ?? 0 };
        var st = r.Data?.StateAt(r.StateIndex);
        if (st != null) param.Text = host.CommonByKey(st.ActionStringKey) ?? "";
        host.StartCasting(this, param);
        return true;
    }

    /// <summary>StateReact::OnCastingFinished (@0x140b8dfb0): m_n48 = 1.</summary>
    public void OnCastingFinished() { if (kind != 0) finished = true; }

    /// <summary>StateReact has no cancel handler: a cast another caster replaced leaves the state running.</summary>
    public void OnCastingCancelled() { }

    /// <summary>
    /// vfunc_3 (@0x140b8d900): for 0x9f, FUN_140b8ded0 ends the state when the target is gone. vfunc_8 (@0x140b8da40,
    /// statereact.cpp:212-251): with the target found, a finished cast sends (FUN_140b8dfd0: StateReact for 0x9f), else the
    /// pc farther than the data's reactDistance from it ends the state; a finished cast ends it whether the target was found
    /// or not; then moving (FUN_1416749c0) ends it (state 2).
    /// </summary>
    public void Tick(Vector3 playerPos, bool playerMoved)
    {
        if (kind == 0 || !host.HasPlayer) return;
        var all = reactors();
        var r = all?.FindByName(target);
        if (kind == KindReact && r == null)
        {
            End();
            return;
        }
        if (r != null)
        {
            if (finished)
            {
                if (kind == KindReact) host.Send(new StateReactRequest(r.ObjectId));
            }
            else if (Vector3.Distance(r.Position, playerPos) > (r.Data?.ReactDistance ?? 0f))
            {
                End();
                return;
            }
        }
        if (finished)
        {
            End();
            return;
        }
        if (playerMoved) End();
    }

    /// <summary>EndOfSequence (@0x140b8de30): the create animation's end returns 0x9e to idle.</summary>
    public void SequenceEnded()
    {
        if (kind == KindCreate && host.HasPlayer) End();
    }

    /// <summary>vfunc_6 (@0x140b8dde0), and any other state entered: the state is left.</summary>
    public void End()
    {
        if (kind != 0) Leave();
    }

    /// <summary>vfunc_5 (@0x140b8d9c0): leaving 0x9f cancels the casting bar.</summary>
    void Leave()
    {
        int was = kind;
        kind = 0;
        finished = false;
        if (was == KindReact && host.HasPlayer) host.CancelCasting();
        host.StopPlayerAnim();
    }
}
