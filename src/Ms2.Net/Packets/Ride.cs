using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound RequestRide mode 1 (SendOp 0x40, commonfiles/opcodes.h:369), MyPC::SendRequestRideMode1 (@0x14152c280,
/// mypc.cpp:3566) with a RideOffActionReact (rideonaction.cpp:759): u8 1, the action's u8 type 20 and u8 argument
/// (RideOffAction::vfunc_1 @0x14077e520), then the reactor's data id and name (RideOffActionReact::vfunc_1 @0x140780ba0,
/// WriteUnicodeStringA: widened, then a unicode string). ReactorAction::vfunc_2 (actionkeyobject.cpp:2915) sends it in place
/// of the react cast when the own pc rides.
/// </summary>
public sealed record RideOffReactRequest(int ReactorId, string Name) : IOutPacket
{
    public const byte ActionType = 20;

    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(ActionType);
        w.WriteByte(0);     // m_nArg: RideOffActionReact(0)
        w.WriteInt(ReactorId);
        w.WriteUnicodeString(Name ?? "");
    }
}

/// <summary>
/// Outbound RequestRide mode 1 for a skill use (MyPC::FUN_141525580, mypc.cpp:2447-2470, through SendRequestRideMode1): u8 1,
/// then the action's vfunc_1: RideOffAction (@0x14077e520: u8 type, u8 argument), RideOffActionUseSkill (@0x14077f2f0: i32
/// skill id, bool, i32 key) or RideOffActionUseGlideItem (@0x14077f470: i64 item uid, bool, i32 key).
/// </summary>
public sealed record RideOffSkillRequest(RideOffAction Action) : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(Action.Type);
        w.WriteByte(Action.Arg);
        switch (Action)
        {
            case RideOffUseSkill u: w.WriteInt(u.Value28); w.WriteBool(u.Flag2C); w.WriteInt(u.Value30); break;
            case RideOffUseGlideItem g: w.WriteLong(g.Value28); w.WriteBool(g.Flag30); w.WriteInt(g.Value34); break;
            case RideOffInteract i: w.WriteInt(i.Id); w.WriteUnicodeString(i.Name ?? ""); break;
            case RideOffTaxi t: w.WriteInt(t.TaxiId); break;
            case RideOffCashCall c: w.WriteInt(c.Value28); w.WriteByte(c.Value2C); w.WriteLong(c.Value30); break;
            case RideOffTakeLR l: w.WriteByte(l.Value28); w.WriteInt(l.Value2C); break;
            case RideOffCoupleEmotion c: w.WriteInt(c.Value28); w.WriteLong(c.Value30); break;   // vfunc_1 @0x140780a20 (rideonaction.cpp:745-750)
        }
    }
}

/// <summary>
/// A ride-off action as ReadRideOffAction (@0x14077e5b0, commonfiles/rideonaction.cpp:159) reads it: u8 type, then
/// RideOffAction::vfunc_2 (@0x14077e570) u8 argument, then the subclass fields. The reader has no case for type 20
/// (RideOffActionReact, :759): that type, like any type it has no case for, reads as null and nothing after the type byte
/// is read.
/// </summary>
public record RideOffAction(byte Type, byte Arg)
{
    /// <summary>ReadRideOffAction: null for a type without a case (20 among them).</summary>
    public static RideOffAction Read(ByteReader r)
    {
        byte type = r.ReadByte();
        if (!Known(type)) return null;
        byte arg = r.ReadByte();
        switch (type)
        {
            case 1: return new RideOffUseSkill(arg, r.ReadInt(), r.ReadBool(), r.ReadInt());                     // :293
            case 25: return new RideOffUseGlideItem(arg, r.ReadLong(), r.ReadBool(), r.ReadInt());               // :324
            case 2: return new RideOffInteract(arg, r.ReadInt(), r.ReadUnicodeString());                         // :353
            case 3: return new RideOffTaxi(arg, r.ReadInt());                                                    // :379
            case 4: return new RideOffCashCall(arg, r.ReadInt(), r.ReadByte(), r.ReadLong());                    // :408
            case 6: return new RideOffTakeLR(arg, r.ReadByte(), r.ReadInt());                                    // :437
            case 8: return new RideOffRecall(arg, r.ReadLong());                                                 // :507
            case 9: return new RideOffSummonPetOn(arg, r.ReadLong(), r.ReadByte());                              // :534
            case 10: return new RideOffSummonPetTransfer(arg, r.ReadLong());                                     // :560
            case 11: return new RideOffHomeConvenient(arg, r.ReadByte());                                        // :584
            case 16: return new RideOffHomemade(arg, r.ReadUnicodeString(), r.ReadInt(), (ushort)r.ReadShort(), r.ReadInt());  // :662
            case 22: return new RideOffNurturing(arg, r.ReadUnicodeString(), r.ReadInt());                       // :692
            case 17: return new RideOffAutoInteraction(arg, r.ReadInt(), (ushort)r.ReadShort(), r.ReadInt());    // :722
            case 19: return new RideOffCoupleEmotion(arg, r.ReadInt(), r.ReadLong());                            // :751
            case 21: return new RideOffUseFunctionItem(arg, r.ReadLong(), r.ReadUnicodeString());                // :806
            // 0, 5 BeautyShop, 7 Hold, 18 AutoClimb, 12 DisableField, 13 Dead, 14 AdditionalEffect, 15 RidingUI, 23 Groggy,
            // 24 UnRideSkill, 26 HideAndSeek: the argument only
            default: return new RideOffAction(type, arg);
        }
    }

    /// <summary>The types ReadRideOffAction has a case for: 0-19, 21-26 (no 20).</summary>
    public static bool Known(byte type) => type <= 26 && type != 20;
}

public sealed record RideOffUseSkill(byte Arg, int Value28, bool Flag2C, int Value30) : RideOffAction(1, Arg);
public sealed record RideOffUseGlideItem(byte Arg, long Value28, bool Flag30, int Value34) : RideOffAction(25, Arg);
public sealed record RideOffInteract(byte Arg, int Id, string Name) : RideOffAction(2, Arg);
public sealed record RideOffTaxi(byte Arg, int TaxiId) : RideOffAction(3, Arg);
public sealed record RideOffCashCall(byte Arg, int Value28, byte Value2C, long Value30) : RideOffAction(4, Arg);
public sealed record RideOffTakeLR(byte Arg, byte Value28, int Value2C) : RideOffAction(6, Arg);
public sealed record RideOffRecall(byte Arg, long Value28) : RideOffAction(8, Arg);
public sealed record RideOffSummonPetOn(byte Arg, long Value28, byte Value30) : RideOffAction(9, Arg);
public sealed record RideOffSummonPetTransfer(byte Arg, long Value28) : RideOffAction(10, Arg);
public sealed record RideOffHomeConvenient(byte Arg, byte Value28) : RideOffAction(11, Arg);
public sealed record RideOffHomemade(byte Arg, string Text28, int Value30, ushort Value34, int Value38) : RideOffAction(16, Arg);
public sealed record RideOffNurturing(byte Arg, string Text28, int Value30) : RideOffAction(22, Arg);
public sealed record RideOffAutoInteraction(byte Arg, int Value28, ushort Value2C, int Value30) : RideOffAction(17, Arg);
public sealed record RideOffCoupleEmotion(byte Arg, int Value28, long Value30) : RideOffAction(19, Arg);
public sealed record RideOffUseFunctionItem(byte Arg, long ItemUid, string Text30) : RideOffAction(21, Arg);

/// <summary>
/// Inbound ResponseRide (RecvOp 0x79, commonfiles/opcodes.h), CSceneStateGame::OnResponseRide (@0x14172a8f0,
/// scenestategame.cpp:12026-12124): u8 mode.
/// 0: i32 object id, a <see cref="RideOnAction"/> (null for a type above 3), the actor's ride on (vfunc_120).
/// 1: i32 object id, a <see cref="RideOffAction"/> (null for type 20), the actor's ride off (vfunc_121).
/// 2: i32 object id, ride off with no action; for the own PC (class 0x6200004) then i32 ridee id and i64 item uid, the
///    summon state 0x26 (another PC's packet carries neither).
/// 3: i32 owner id, i32 rider id, u8 seat: the owner's passenger (FUN_141676670); the own PC as rider holds on (state 0x20).
/// 4: i32 owner id, i32 rider id: the passenger leaves (RemovePassenger). 5: i32 owner id: all passengers leave.
/// 6: i32 object id, u8 flag: PC m_b1224 and the ride-on action's m_b28.
/// The client stops reading when the actor is missing; the reader reads the whole mode body (mode 1's action comes after
/// the actor lookup there, mode 2's tail is read only for the own PC, so it takes the own object id).
/// </summary>
public sealed record ResponseRide(byte Mode, int ObjectId, RideOnAction RideOn, RideOffAction RideOff, int RideeId, long ItemUid,
    int RiderId, byte Seat, bool Flag)
{
    public static ResponseRide Read(ByteReader r, int selfObjectId, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        int id = 0, rideeId = 0, riderId = 0; long uid = 0; byte seat = 0; bool flag = false;
        RideOnAction on = null; RideOffAction off = null;
        switch (mode)
        {
            case 0: id = r.ReadInt(); on = RideOnAction.TryRead(r, profile); break;
            case 1: id = r.ReadInt(); off = RideOffAction.Read(r); break;
            case 2:
                id = r.ReadInt();
                if (id == selfObjectId && selfObjectId != 0) { rideeId = r.ReadInt(); uid = r.ReadLong(); }
                break;
            case 3: id = r.ReadInt(); riderId = r.ReadInt(); seat = r.ReadByte(); break;
            case 4: id = r.ReadInt(); riderId = r.ReadInt(); break;
            case 5: id = r.ReadInt(); break;
            case 6: id = r.ReadInt(); flag = r.ReadByte() != 0; break;
        }
        return new ResponseRide(mode, id, on, off, rideeId, uid, riderId, seat, flag);
    }
}

/// <summary>
/// Outbound RequestRide mode 0 from a ride item (StateSummon, statesummon.cpp:340-357): u8 0, then
/// RideOnActionUseItem::vfunc_1 (@0x14077e1d0): u8 type 1, i32 ridee id, i32 object id 0, u8 the game option flag
/// (FUN_14124ab20()+2), i32 0, i64 the item uid, and a cleared CUgcItemLook (CUgcItemLook::Clear, ugcdef.cpp:1464).
/// </summary>
public sealed record RideOnItemRequest(int RideeId, long ItemUid, bool OptionFlag) : IProfileOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    /// <summary>The GMS2 recordings (570, all with zeros there) have 8 bytes between the ridee id and the item uid where KMS writes
    /// i32 0, the option flag and i32 0 (9): the GMS2 form has no flag byte. STILL-OPEN: what the flag is in GMS2.</summary>
    public void Write(ByteWriter w, ProtocolProfile profile)
    {
        w.WriteByte(0);
        w.WriteByte(1);                   // RideOnActionUseItem m_nType
        w.WriteInt(RideeId);              // m_n1c
        w.WriteInt(0);                    // m_n20
        if (!profile.IsGms2) w.WriteBool(OptionFlag);   // m_b28
        w.WriteInt(0);                    // m_n30
        w.WriteLong(ItemUid);             // m_n38
        ItemReader.WriteUgcLook(w, new UgcLook());
    }
}

/// <summary>Outbound RequestRide mode 1 with a plain RideOffAction(0) (MyPC::SendRequestRideMode1(null), @0x14152c280):
/// u8 1, u8 type 0, u8 argument 0.</summary>
public sealed record RideOffRequest : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(0);
        w.WriteByte(0);
    }
}

/// <summary>Outbound RequestRide mode 1 with a RideOffActionHold (rideonaction.cpp:445-466): u8 1, u8 type 7, u8 argument 0
/// (its vfunc_1 writes the base fields only). HoldPCAction::vfunc_2 (actionkeyobject.cpp:2357-2373) sends it when the own pc
/// rides; the answer's type 7 makes MyPC::vfunc_121 board the nearest mount (mypc.cpp:3446-3466).</summary>
public sealed record RideOffHoldRequest : IOutPacket
{
    public const byte ActionType = 7;

    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(ActionType);
        w.WriteByte(0);
    }
}

/// <summary>Outbound RequestRide mode 1 with a RideOffActionRecall (rideonaction.cpp:489-512): u8 1, u8 type 8, u8 argument 0,
/// i64 the recall's target (the scroll's uid, m_n28). CCashCallManager::FUN_140b04430 (cashcallmanager.cpp:349-354) sends it
/// when the own pc rides; the answer's type 8 starts StateRecall with that target (mypc.cpp:3467-3472).</summary>
public sealed record RideOffRecallRequest(long Target) : IOutPacket
{
    public const byte ActionType = 8;

    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(ActionType);
        w.WriteByte(0);
        w.WriteLong(Target);
    }
}

/// <summary>Outbound RequestRide mode 1 with a RideOffActionCashCall (rideonaction.cpp:386-415, writer @0x14077f8d0): u8 1, u8 type 4,
/// u8 argument 0, i32 the field (m_n28), u8 the call kind (m_n2c), i64 the ticket item's uid (m_n30, 0 for none).
/// CCashCallManager::FUN_140b030b0 sends it when the own pc rides (exe 0x140b033f0-0x140b034af); the answer's type 4 starts
/// StateCashCall with the three (mypc.cpp:3420-3425).</summary>
public sealed record RideOffCashCallRequest(int FieldId, byte Kind, long ItemUid) : IOutPacket
{
    public const byte ActionType = 4;

    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(1);
        w.WriteByte(ActionType);
        w.WriteByte(0);
        w.WriteInt(FieldId);
        w.WriteByte(Kind);
        w.WriteLong(ItemUid);
    }
}

/// <summary>Outbound RequestRide mode 2, ride along (MyPC, mypc.cpp:4684-4692): u8 2, then WriteRideRequest (@0x1401fcc90):
/// i32 ridee id, i64 item uid.</summary>
public sealed record RideAlongRequest(int RideeId, long ItemUid) : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(2);
        w.WriteInt(RideeId);
        w.WriteLong(ItemUid);
    }
}

/// <summary>Outbound RequestRide mode 3, board another PC's mount (MyPC::SendRequestRideMode3 @0x14152dee0): u8 3, i32 the
/// owner's object id. The client sends nothing (notice 0x2f7) when content 0x57 is shut down.</summary>
public sealed record RideBoardRequest(int OwnerObjectId) : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(3);
        w.WriteInt(OwnerObjectId);
    }
}

/// <summary>Outbound RequestRide mode 4, leave a mount as passenger (MyPC::SendRequestRideMode4 @0x14152df80): u8 4.</summary>
public sealed record RideLeaveRequest : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w) => w.WriteByte(4);
}

/// <summary>Outbound RequestRide mode 6 (MyPC::SendRequestRideMode6 @0x14152dff0): u8 6, u8 the option value.</summary>
public sealed record RideOptionRequest(byte Value) : IOutPacket
{
    public Outbound Op => Outbound.RequestRide;

    public void Write(ByteWriter w)
    {
        w.WriteByte(6);
        w.WriteByte(Value);
    }
}
