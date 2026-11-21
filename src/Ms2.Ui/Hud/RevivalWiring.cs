using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// What the live client feeds UIRevivalDialog: the own player's state, the field's death-state map properties
/// (CFieldDataCli, uirevivaldialog.cpp:397, 412, 438, 493) and the string notices. The game scene calls this for the dialog it
/// opens, so a test can run the same wiring.
/// STILL-OPEN: the paid revive (merat and meso) is refused here, because three inputs of its send are not in the Godot data:
/// the buffs' IsDisableRevivalHere (uirevivaldialog.cpp:813-816), the meso revivals left per day (MyPC::GetMesoRevivalRemainingCount,
/// from a constant), and the platform's merat check (vfunc_40, :631). The client sends the free revive (mode 0) and nothing else
/// from these without them; ours sends no more than that.
/// </summary>
public static class RevivalWiring
{
    public static void Connect(RevivalEnvironment e, MyCharacter me, Func<int?> fieldId, Func<int, FieldData> fieldOf,
        Action<IOutPacket> send, Action<string> notice, Func<int, string> common, Func<int, string> mapName)
    {
        e.HasPlayer = () => me.ObjectId >= 0;
        e.IsDead = () => me.Dead;
        e.Level = () => me.User?.Level ?? 1;
        e.Meso = () => me.Meso;
        e.RevivalBlocked = () => me.RevivalBlocked;
        e.RoomRevivalsUsed = () => me.DungeonRevivalsUsed;
        e.FieldId = () => fieldId() ?? 0;
        e.Field = () => fieldId() is int id && fieldOf(id) is { } f
            ? new RevivalField(f.RevivalReturnId, f.DeathPenalty, f.DoNotRevivalHere, f.InfiniteMeratRevival) : null;
        e.FieldName = mapName;                          // CStringTable table 3 (uirevivaldialog.cpp:363, :418)
        e.PaidRevivalWired = () => false;
        e.Send = send;
        e.Notice = notice;
        e.NoticeById = id => notice(common(id) ?? "");
    }
}
