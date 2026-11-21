using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

/// <summary>
/// Another player's death bookkeeping and visuals: the revival flag and confirm count PC::IsRevivalBlocked reads, the tomb
/// (PC m_spFC8, <see cref="PlayerTombstone"/>) and its UITombStoneBar entity (<see cref="Ui.TombGaugeBar"/>, made once per PC
/// by PC::FUN_141657730).
/// </summary>
public sealed class RemoteDeath
{
    /// <summary>PC m_n10f0 (FUN_141661ba0: DeadUser's flag, Revival's flag, vfunc_137 with a confirm count) and m_n10ec
    /// (FUN_141661a60: RevivalConfirm's count).</summary>
    public bool RevivalFlag; public int RevivalCount;
    /// <summary>The buffs' IsDisableRevivalHere (the PC's AdditionalCliList).</summary>
    public Func<bool> BuffsDisableRevival = () => false;
    /// <summary>PC::IsRevivalBlocked (pc.cpp:2009): a confirm count, the revival flag or the buffs' IsDisableRevivalHere.</summary>
    public bool RevivalBlocked => RevivalCount > 0 || RevivalFlag || BuffsDisableRevival();
    /// <summary>The tomb while it stands, else null or a freed node.</summary>
    public PlayerTombstone Tomb { get; private set; }
    public Ui.TombGaugeBar Bar { get; private set; }

    /// <summary>
    /// StateDead::vfunc_4's PC part (statedead.cpp:107-146; the own-player block FUN_1417b84b0 is MyPC only): the body
    /// (<see cref="RemotePc.EnterDead"/>); the normal death builds the tomb under <paramref name="parent"/>
    /// (PC::FUN_14165cac0: MS2TombStoneDark while the revival is blocked) and shows the bar, made on the first death with
    /// <paramref name="anchor"/> (the PC's Eff_UI point).
    /// </summary>
    public void Enter(Node3D parent, PackSet packs, ModelBuilder builder, RemotePc pc, Func<Vector3> anchor, bool inPlace, int reviveAnim)
    {
        pc.EnterDead(inPlace, reviveAnim);
        if (inPlace) return;
        if (Tomb != null && GodotObject.IsInstanceValid(Tomb)) Tomb.QueueFree();
        Tomb = PlayerTombstone.Build(parent, packs, builder, pc.Body, () => pc.States.Cur == Ms2.Net.Replay.RemotePcStates.StateDead, RevivalBlocked);
        if (Bar != null) return;
        Bar = new Ui.TombGaugeBar(World.FieldContents.Ui(packs), World.FieldContents.Constants(packs), () => anchor(),
            parent.GetViewport().GetVisibleRect().Size)
        {
            Tomb = () => Tomb, Dead = () => pc.States.Dead, RevivalBlocked = () => RevivalBlocked,
        };
        parent.AddChild(Bar);
    }

    /// <summary>The Tombstone packet's PC::FUN_14165d300 (pc.cpp:3464) on the standing tomb: its hit or break and the bar's
    /// stage. Returns the stage, null without a tomb or for a break.</summary>
    public int? Hit(int kind, int value) => Tomb != null && GodotObject.IsInstanceValid(Tomb) ? Tomb.Hit(kind, value) : null;

    /// <summary>The tomb and the bar freed with the PC (UserRemoved, a replacing UserAdded).</summary>
    public void Free()
    {
        if (Tomb != null && GodotObject.IsInstanceValid(Tomb)) Tomb.QueueFree();
        if (Bar != null && GodotObject.IsInstanceValid(Bar)) Bar.QueueFree();
        Tomb = null; Bar = null;
    }
}
