namespace Ms2Godot;

/// <summary>
/// The own pc's StateTriggerFollowNpc (0x46, stateemotionidle.cpp:497-583), which the FollowNpc packet enters
/// (CSceneStateGame::OnFollowNpc, scenestategame.cpp:5862-5869: vfunc_59(0x46, StateDataTriggerFollowNpc(npc), 0, 0)). The
/// server sends it with a dummy npc it walks along a patrol path, so the pc rides along: each frame the pc stands where the npc
/// is, turned as it is, and plays the sequence the npc's last sync named.
/// On a mount the state's owner is the ridee (FUN_141468360, :525-527: the Ridee class 0x6200045) and the sequence name
/// comes from the rider's actor manager: the owner moves and turns, which here is the pc's own node (a mount moves the same
/// body, the Ridee being a PCBase), the ridee plays the sequence of that name when its kfm has one
/// (<see cref="Mount.PlaySequenceOwnCycle"/>), and the rider keeps the riding pose StateRide gives it.
/// State::vfunc_4's and vfunc_3's base work (the speed setup, state.cpp:600-611) is not ported, as for the other trigger state.
/// </summary>
public partial class Player
{
    int followNpc;         // m_spData2->m_nNpc: the npc's object id while the state runs, else 0
    short followAnim;      // m_nAnim: the last npc sequence key played

    /// <summary>The npc of an object id for the state (WorldService::FindNpcById): its position in the client's z-up
    /// coordinates (vfunc_18), its rotation z in degrees (vfunc_20) and its sync sequence key (CNpc::FUN_141093510,
    /// m_nDE8); null when it is not in the field.</summary>
    public System.Func<int, (System.Numerics.Vector3 Pos, float RotZ, short AnimKey)?> FindNpc;

    /// <summary>The npc the state follows, 0 outside it.</summary>
    public int FollowedNpc => State == MoveState.TriggerFollowNpc ? followNpc : 0;

    /// <summary>
    /// vfunc_59(0x46, data(npc), 0, 0) on the own pc. PC::vfunc_59 (pc.cpp:1189-1194) refuses it while dead;
    /// CStateManager::ChangeState keeps the running state when vfunc_25 (@0x1417c4fe0, :565-575) answers true, which it does
    /// only for the same npc. Then vfunc_4 (@0x1417c4b40, :497-519): SendRequestCube with a held item, the data kept and
    /// m_nAnim 0, and back to StateIdle (vfunc_59(1)) when no npc of the id is here.
    /// </summary>
    public void EnterTriggerFollowNpc(int npcObjectId)
    {
        if (DeadFlag) return;
        if (State == MoveState.TriggerFollowNpc && followNpc == npcObjectId) return;
        Enter(MoveState.TriggerFollowNpc);
        if (State != MoveState.TriggerFollowNpc) return;
        HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.TriggerFollowNpc);
        followNpc = npcObjectId;
        followAnim = 0;
        if (FindNpc?.Invoke(npcObjectId) == null) Enter(MoveState.Idle);
    }

    /// <summary>
    /// vfunc_3 (@0x1417c4c90, :521-562): with the npc gone, back to StateIdle; else the pc is placed at the npc's position
    /// (vfunc_19) and turned to its rotation at once (SetRotationAndTargetYaw), and when the npc's sequence key is set and
    /// differs from the last one, the pc plays Jump_Down_A for the jump keys (-2, -3) or its own sequence of that key
    /// (NiActorManager::GetSequenceData on the pc's body), at speed 1 without bForce (PCBase::PlayAnimation(name, 1, false,
    /// false)): the kf's own cycle decides whether it loops (<see cref="ActionAnims.SequenceOwnCycle"/>); on a mount the ridee
    /// plays it.
    /// </summary>
    void TriggerFollowNpcUpdate()
    {
        kVel = Godot.Vector3.Zero;
        if (FindNpc?.Invoke(followNpc) is not { } npc) { Enter(MoveState.Idle); return; }
        GlobalPosition = new Godot.Vector3(npc.Pos.X, npc.Pos.Z, -npc.Pos.Y);
        yawDeg = targetYawDeg = npc.RotZ;
        short key = npc.AnimKey;
        if (key == 0 || key == followAnim) return;
        followAnim = key;
        string name = key is -2 or -3 ? "Jump_Down_A" : Anims?.SequenceName(key);
        if (string.IsNullOrEmpty(name) || Anims == null || Anim == null) return;
        if (Mount != null) { Mount.PlaySequenceOwnCycle(name); return; }
        string seq = Anims.SequenceOwnCycle(name);
        if (seq != null) PlaySequence(seq, 1f, restart: false);
    }
}
