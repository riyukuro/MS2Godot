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
    /// <summary>PC m_n10e8, m_n10ec and m_n10f0: the confirm end tick and count (FieldAddUser, RevivalConfirm, expired by
    /// PC::FUN_141661ac0) and the revival flag (FieldAddUser, DeadUser, Revival, vfunc_137 with a confirm count).</summary>
    public readonly Ms2.Net.Packets.PcRevival Revival = new();
    /// <summary>The buffs' IsDisableRevivalHere (the PC's AdditionalCliList).</summary>
    public Func<bool> BuffsDisableRevival = () => false;
    /// <summary>PC::IsRevivalBlocked (pc.cpp:2009): a confirm count, the revival flag or the buffs' IsDisableRevivalHere.</summary>
    public bool RevivalBlocked => Revival.Blocked || BuffsDisableRevival();
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
    public int? Hit(int kind, int value, int hitter, Ms2.Formats.World.ClientConstants constants, Vector3? viewer, Func<uint> rand)
    {
        if (Tomb == null || !GodotObject.IsInstanceValid(Tomb)) return null;
        Tomb.PlayPacketSounds(kind, hitter, false, constants, viewer, rand);
        return Tomb.Hit(kind, value);
    }

    /// <summary>The tomb and the bar freed with the PC (UserRemoved, a replacing UserAdded).</summary>
    public void Free()
    {
        if (Tomb != null && GodotObject.IsInstanceValid(Tomb)) Tomb.QueueFree();
        if (Bar != null && GodotObject.IsInstanceValid(Bar)) Bar.QueueFree();
        Tomb = null; Bar = null;
    }
}
