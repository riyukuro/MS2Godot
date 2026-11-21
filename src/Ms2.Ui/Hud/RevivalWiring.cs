using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// What the live client feeds UIRevivalDialog: the own player's state, the field's death-state map properties
/// (CFieldDataCli, uirevivaldialog.cpp:397, 412, 438, 493) and the string notices. The game scene calls this for the dialog it
/// opens, so a test can run the same wiring. Without <see cref="RevivalPaidInputs"/> the paid revive (merat and meso) is refused,
/// as the client sends only the free revive (mode 0) without them.
/// STILL-OPEN (left at their defaults): CGameRule's meso revival rule (FUN_140477d10), the dungeon room (DungeonRoomTable), the
/// revival coupon (FindItemByTag(4)), the equipment's cost reductions (GetAbilityRate 0x42 / 0xa4), and the platform's red-merat
/// check (vfunc_41, feature 0x43 with the admin level DAT_143ab2914 at 52 or more; the port has no admin level).
/// </summary>
/// <param name="Merat">DAT_143ab2978 (FUN_1409304d0): the merat of the four wallet kinds summed.</param>
/// <param name="Buffs">FUN_1416749a0(): the own player's additional effects.</param>
/// <param name="Constants">The client constants (Constants +0x51d8, +0x8cc0, +0x90f8, +0xa278, +0xcfe0).</param>
public sealed record RevivalPaidInputs(Func<long> Merat, AdditionalBuffList Buffs, ClientConstants Constants);

public static class RevivalWiring
{
    public static void Connect(RevivalEnvironment e, MyCharacter me, Func<int?> fieldId, Func<int, FieldData> fieldOf,
        Action<IOutPacket> send, Action<string> notice, Func<int, string> common, Func<int, string> mapName,
        RevivalPaidInputs paid = null)
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
        e.PaidRevivalWired = () => paid != null;
        if (paid != null) ConnectPaid(e, me, paid);
        e.Send = send;
        e.Notice = notice;
        e.NoticeById = id => notice(common(id) ?? "");
    }

    /// <summary>MesoRevivalDailyMaxCount: Lua getMesoRevivalDailyMaxCount (MyPC::GetMesoRevivalRemainingCount, mypc.cpp:2169),
    /// whose body in Data/lua/Precompiled.m2d luapack64.o (source lines 333-335, the same chunk in both data sets) is
    /// <c>return 3</c> with no branch.</summary>
    public const int MesoRevivalDailyMax = 3;

    static void ConnectPaid(RevivalEnvironment e, MyCharacter me, RevivalPaidInputs paid)
    {
        var buffs = paid.Buffs;
        var k = paid.Constants;
        uint debuff = (uint)k.Int("MeratRevivalDebuffCode", 100000001);
        e.RevivalBlocked = () => me.RevivalBlocked || buffs.IsDisableRevivalHere;     // PC::IsRevivalBlocked (pc.cpp:2009)
        e.BuffsDisableRevival = () => buffs.IsDisableRevivalHere;
        e.RevivalOverlapCount = () => buffs.OverlapCountOf(debuff, 0);
        e.PaidReviveStopped = () => buffs.Has(debuff, 0, 0, 1);
        e.MesoRevivalRemaining = () => Math.Max(MesoRevivalDailyMax - me.MesoRevivalsUsed, 0);
        e.CanPayMerat = cost => paid.Merat() - cost >= 0;                              // vfunc_40: GetMeratBalance() - cost >= 0
        float meratCap = k.Float("MeratRevivalFeeReduceLimit", 0.5f), mesoCap = k.Float("MesoRevivalFeeReduceLimit", 0.5f);
        bool meratOff = k.Bool("LimitMeratRevival", false);
        int leave = k.Int("RoomExitWaitSecond", 10);
        e.MeratRateCap = () => meratCap;
        e.MesoRateCap = () => mesoCap;
        e.MeratRevivalOff = () => meratOff;
        e.LeaveBoxSeconds = () => leave;
    }
}
