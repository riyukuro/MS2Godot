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
