namespace Ms2Godot;

// The own pc's state flags (PCBase m_unk_158): features hold the pc with them (the auto-find taxi camera 0x100,
// autofindtaxistation.cpp:226; the others below are not ported yet), and while any is held PCBase::vfunc_63 is 0, so the
// move gate (Ms2.Ui.MoveGate) is closed and the hit and fall damage states read no move.
public partial class Player
{
    /// <summary>PCBase m_unk_158 (+0x158).</summary>
    public uint StateFlags { get; private set; }

    /// <summary>PCBase::AddStateFlags (@0x14166cce0, pcbase.cpp:1233): m_unk_158 |= flags (the rider's and the mount's).</summary>
    public void AddStateFlags(uint flags) => StateFlags |= flags;

    /// <summary>PCBase::RemoveStateFlags (@0x14166cd70, pcbase.cpp:1243): m_unk_158 &amp;= ~flags.</summary>
    public void RemoveStateFlags(uint flags) => StateFlags &= ~flags;

    /// <summary>PCBase::vfunc_63 (@0x14166ce00; MyPC::vfunc_63 @0x141521670 is the same): no state flag held.</summary>
    public bool CanAct => Ms2.Ui.MoveGate.CanAct(StateFlags);
}

public partial class Player
{
    // WorldService::GetLocalPlayer for the key input channels (Ms2.Ui.KeyInput) and the own key reads: the player in the field.
    // A new player starts with no flags, as a new PC does in the client: the MyPC constructor (@0x141520680) leaves the
    // PCBase flag word (m_unk_158) at 0, and nothing on the field-enter path re-applies 0x20. Traced:
    //  - 0x20 is added only by CMyPCKeyInputController::FUN_141901070 (uicommon.cpp:1613, plus the mount at :1622); no other
    //    AddStateFlags caller passes 0x20 (the others pass 4, 8, 0x10, 0x80, 0x100, 0x200, 0x400, 1).
    //  - WorldService::OnObjectCreated (@0x141bbb1e0, worldservice_1.cpp:3126) for a MyPC does SetMyPC, the camera, the fog,
    //    the HUD and posts NotifyCreateMyPC; it touches no key layer and copies no flags from the old PC. The old PC is dropped
    //    by WorldService::FUN_141bbb7f0 (:3242, SetMyPC(0)).
    //  - NotifyCreateMyPC has one subscriber, CQuestNavigator::OnCreateMyPC (questnavigator.cpp:130), which restores the quest.
    //  - The move gate (PCBase::vfunc_63) reads only the PC's own flag word; no global layer count is read there.
    //  - CMyPCKeyInputController::FUN_1419012a0 (forget all layers) has no caller in the recovered client.
    // So a layer held across a same-server field change leaves the layer counts set and the new PC's flag clear, and the
    // client moves. The port does the same. STILL-OPEN: the exe's own field-enter entity factory (the egf create of the MyPC
    // entity, which builds the PC outside OnObjectCreated) is not in the recovered source; a flag copy there would show only in
    // a client capture of the move key held through a same-server warp with a box open.
    static Player own;

    void AttachKeyInput()
    {
        own = this;
        Ms2.Ui.InputBlock.OwnStateFlags = () => own is { } p && IsInstanceValid(p) ? p.StateFlags : 0;
        // CMyPCKeyInputController::FUN_141901070 (@0x141901070): AddStateFlags(0x20) or RemoveStateFlags(0x20) on the local pc, if any
        Ms2.Ui.KeyInput.Shared.LocalPcFlag = block =>
        {
            if (own is not { } p || !IsInstanceValid(p)) return;
            if (block) p.AddStateFlags(Ms2.Ui.KeyInputLayers.MoveStateFlag);
            else p.RemoveStateFlags(Ms2.Ui.KeyInputLayers.MoveStateFlag);
        };
    }

    public override void _ExitTree()
    {
        if (own == this) own = null;
    }
}
