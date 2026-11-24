using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// What the live client feeds UIRevivalDialog: the own player's state, the field's death-state map properties
/// (CFieldDataCli, uirevivaldialog.cpp:397, 412, 438, 493) and the string notices. The game scene calls this for the dialog it
/// opens, so a test can run the same wiring. Without <see cref="RevivalPaidInputs"/> the paid revive (merat and meso) is refused,
/// as the client sends only the free revive (mode 0) without them.
/// STILL-OPEN (left at its default): the platform's red-merat check (vfunc_41, feature 0x43 with the admin level DAT_143ab2914
/// at 52 or more; the port has no admin level).
/// </summary>
/// <param name="RoomId">DAT_143ab2884: the dungeon room id of the field (0 outside a dungeon).</param>
/// <param name="RoomOf">DungeonRoomTable::GetRoom (null for an unknown id).</param>
/// <param name="RoomLimitFeature">CFeatureManager::IsFeatureEnabled(0x5d), DungeonLimitMesoRevival (featuremanager.cpp:316).</param>
public sealed record RevivalRoomInputs(Func<int> RoomId, Func<int, DungeonRoom> RoomOf, Func<bool> RoomLimitFeature);
/// <param name="Merat">DAT_143ab2978 (FUN_1409304d0): the merat of the four wallet kinds summed.</param>
/// <param name="Buffs">FUN_1416749a0(): the own player's additional effects.</param>
/// <param name="Constants">The client constants (Constants +0x51d8, +0x8cc0, +0x90f8, +0xa278, +0xcfe0).</param>
/// <param name="MesoRevivalRule">CGameRule::FUN_140477d10(PC::FUN_141658f30()) (uirevivaldialog.cpp:566, :721): the game
/// events' flat meso revive rule (<see cref="Ms2.Ui.MesoRevivalRule.Rule"/>); null is 0.</param>
/// <param name="AbilityRate">CEquipment::GetAbilityRate(type) of the own player (uirevivaldialog.cpp:510, :687, :723): read for
/// <see cref="MeratFeeAbility"/> and <see cref="MesoFeeAbility"/>; null is 0.</param>
/// <param name="HasItemWithTag">Unk_Inventory::FindItemByTag(tag) found an item (uirevivaldialog.cpp:449, :503, :754): read for
/// <see cref="CouponTag"/>; null finds none.</param>
public sealed record RevivalPaidInputs(Func<long> Merat, AdditionalBuffList Buffs, ClientConstants Constants,
    Func<int> MesoRevivalRule = null, Func<int, float> AbilityRate = null, Func<int, bool> HasItemWithTag = null)
{
    /// <summary>SAType 0x42, reduce_merat_revival_fee (the special ability string table's row 66).</summary>
    public const int MeratFeeAbility = 0x42;
    /// <summary>SAType 0xa4, reduce_meso_revival_fee (row 164).</summary>
    public const int MesoFeeAbility = 0xa4;
    /// <summary>The item tag FindItemByTag looks for: 4, FreeReviveCoupon (InitItemTags' fourth name).</summary>
    public const int CouponTag = 4;
}

public static class RevivalWiring
{
    public static void Connect(RevivalEnvironment e, MyCharacter me, Func<int?> fieldId, Func<int, FieldData> fieldOf,
        Action<IOutPacket> send, Action<string> notice, Func<int, string> common, Func<int, string> mapName,
        RevivalPaidInputs paid = null, RevivalRoomInputs room = null)
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
        // CFieldData::FUN_140374410 (fielddata.h:75, @0x140374410): the field's mapType is 3 (a home); its meso revive is free
        // (FUN_141abcb90 and RefreshMesoButton, uirevivaldialog.cpp:643, :727)
        e.FieldRevivesForFree = id => fieldOf(id) is { MapType: 3 };
        if (room != null) ConnectRoom(e, room);
        e.FieldName = mapName;                          // CStringTable table 3 (uirevivaldialog.cpp:363, :418)
        e.PaidRevivalWired = () => paid != null;
        if (paid != null) ConnectPaid(e, me, paid);
        e.Send = send;
        e.Notice = notice;
        e.NoticeById = id => notice(common(id) ?? "");
    }

    /// <summary>MyPC::vfunc_137's dead branch (mypc.cpp:1347-1355): 0x4e38 opens only when the field has neither auto-revival type 1
    /// nor 2 (CFieldData::HasAutoRevivalType1 / 2, fielddata.cpp:827, 830). A field without data opens it (the client always has
    /// the data; ours has none only on a failed table read).</summary>
    public static bool OpensDialog(Ms2.Formats.World.FieldData field) =>
        field == null || !(field.HasAutoRevivalType1 || field.HasAutoRevivalType2);

    /// <summary>MesoRevivalDailyMaxCount: Lua getMesoRevivalDailyMaxCount (MyPC::GetMesoRevivalRemainingCount, mypc.cpp:2169),
    /// whose body in Data/lua/Precompiled.m2d luapack64.o (source lines 333-335, the same chunk in both data sets) is
    /// <c>return 3</c> with no branch.</summary>
    public const int MesoRevivalDailyMax = 3;

    /// <summary>The dungeon room the dialog reads (GetFirstButtonLabel, FUN_141abc7f0, FUN_141abc930; uirevivaldialog.cpp:574-581,
    /// :595-600, :779-792): the room's row by DAT_143ab2884, none for 0 or an unknown id.</summary>
    static void ConnectRoom(RevivalEnvironment e, RevivalRoomInputs inputs)
    {
        int lastId = 0;
        RevivalRoom last = null;
        e.Room = () =>
        {
            int id = inputs.RoomId();
            if (id == 0) return null;
            if (id != lastId)
            {
                lastId = id;
                last = inputs.RoomOf(id) is { } r ? new RevivalRoom(r.LimitMesoRevival, r.LimitMeratRevival, r.DefaultRevivalLimitCount) : null;
            }
            return last;
        };
        e.RoomLimitFeature = inputs.RoomLimitFeature;
    }

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
        if (paid.MesoRevivalRule is { } rule) e.MesoRevivalRule = rule;
        if (paid.AbilityRate is { } rate)
        {
            e.MeratAbilityRate = () => rate(RevivalPaidInputs.MeratFeeAbility);
            e.MesoAbilityRate = () => rate(RevivalPaidInputs.MesoFeeAbility);
        }
        if (paid.HasItemWithTag is { } has) e.HasCoupon = () => has(RevivalPaidInputs.CouponTag);
    }
}
