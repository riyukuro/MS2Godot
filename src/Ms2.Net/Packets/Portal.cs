// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client.
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x48, CSceneStateGame::OnFieldPortal @0x14171ee80 (scenestategame.cpp:7227): u8 mode, i32 portal id, then per
/// mode. The decompiled locals are named loosely; the field meaning here follows the server writer's order.
/// <list type="bullet">
/// <item>0 Add: u8 visible (CPortal +0x24, shown), u8 enabled (+0x28, glow), vec3 position, vec3 rotation, vec3 dimension,
/// wstr model ("urn:gamebryo-scenegraph:" + model when not empty), i32 target map (kept only when not 0), i32 object id,
/// i32 action kind, u8 minimap, i64 owner (home id), u8 type, i32 end tick, u16, i32 start tick, bool password,
/// wstr owner name, wstr feature (+0x190), wstr locale (+0x198); then WorldService::RegisterPortal.</item>
/// <item>1 Remove: RemoveFieldObject(id).</item>
/// <item>2 Update: u8 visible, u8 enabled, u8 minimap, u16 (+0x188); then SetFieldObjectFlags.</item>
/// <item>3 Move: vec3 position, vec3 rotation.</item>
/// </list>
/// </summary>
public sealed class FieldPortalMessage
{
    public byte Mode; public int Id;
    public bool Visible, Enabled, Minimap, Password;
    public Vector3 Position, Rotation, Dimension;
    public string Model = "", OwnerName = "", Feature = "", Locale = "";
    public int TargetMap, ObjectId, ActionKind, EndTick, StartTick;
    public ushort Extra;
    public long Owner;
    public byte Type;

    public static FieldPortalMessage Read(ByteReader r)
    {
        var m = new FieldPortalMessage { Mode = r.ReadByte(), Id = r.ReadInt() };
        switch (m.Mode)
        {
            case 0:
                m.Visible = r.ReadByte() != 0; m.Enabled = r.ReadByte() != 0;
                m.Position = r.ReadVec3(); m.Rotation = r.ReadVec3(); m.Dimension = r.ReadVec3();
                m.Model = r.ReadUnicodeString();
                m.TargetMap = r.ReadInt(); m.ObjectId = r.ReadInt(); m.ActionKind = r.ReadInt();
                m.Minimap = r.ReadByte() != 0; m.Owner = r.ReadLong(); m.Type = r.ReadByte();
                m.EndTick = r.ReadInt(); m.Extra = (ushort)r.ReadShort(); m.StartTick = r.ReadInt();
                m.Password = r.ReadByte() != 0;
                m.OwnerName = r.ReadUnicodeString(); m.Feature = r.ReadUnicodeString(); m.Locale = r.ReadUnicodeString();
                break;
            case 1:
                break;
            case 2:
                m.Visible = r.ReadByte() != 0; m.Enabled = r.ReadByte() != 0; m.Minimap = r.ReadByte() != 0;
                m.Extra = (ushort)r.ReadShort();
                break;
            case 3:
                m.Position = r.ReadVec3(); m.Rotation = r.ReadVec3();
                break;
            default:
                return m;   // the client reads nothing more for another mode
        }
        r.ExpectEnd("FieldPortal"); return m;
    }
}

/// <summary>
/// Inbound 0xAA, CSceneStateGame::OnEnterUgcMap @0x1417373d0 (scenestategame.cpp:4581): u8 mode. Mode 3 (the home's
/// password box) reads i32 portal, i32 a, i32 b, i64 c, i32 d, u8 with-password; mode 4 (a wrong password) reads i32
/// portal, i32 a, i32 b, i64 c. The other modes carry nothing.
/// </summary>
public sealed class EnterUgcMapMessage
{
    public byte Mode; public int PortalId, A, B, D; public long C; public bool WithPassword;

    public static EnterUgcMapMessage Read(ByteReader r)
    {
        var m = new EnterUgcMapMessage { Mode = r.ReadByte() };
        if (m.Mode is 3 or 4)
        {
            m.PortalId = r.ReadInt(); m.A = r.ReadInt(); m.B = r.ReadInt(); m.C = r.ReadLong();
            if (m.Mode == 3) { m.D = r.ReadInt(); m.WithPassword = r.ReadByte() != 0; }
        }
        r.ExpectEnd("EnterUgcMap"); return m;
    }
}

/// <summary>
/// Outbound RequestMoveField (0x6C) mode 2, MyPC::SendRequestMoveFieldMode2 @0x141529c80 (mypc.cpp:3082): u8 2, i32 portal,
/// i32 a, i32 b, i64 c, wstr password. Sent for a home portal (type 0xb with an owner, actionkeyobject.cpp) and by the
/// password box of <see cref="EnterUgcMapMessage"/> mode 3 when it asked for a password.
/// </summary>
public sealed record RequestMoveFieldHome(int PortalId, int A, int B, long C, string Password = "") : IOutPacket {
    public Outbound Op => Outbound.RequestMoveField;
    public void Write(ByteWriter w) {
        w.WriteByte(2); w.WriteInt(PortalId); w.WriteInt(A); w.WriteInt(B); w.WriteLong(C);
        w.WriteUnicodeString(Password ?? "");
    }
}

/// <summary>PortalAction::SendRoomDungeon @0x140956310 (actionkeyobject.cpp:1091), a type 9 portal: RoomDungeon u8 9.</summary>
public sealed record RoomDungeonPortalRequest : IOutPacket {
    public Outbound Op => Outbound.RoomDungeon;
    public void Write(ByteWriter w) => w.WriteByte(9);
}

/// <summary>
/// FUN_140dff580 @0x140dff580 (unit_140dfef30_ugcoxevent.cpp:232), PortalAction::UsePortal's type 0x10 (actionkeyobject.cpp:1063):
/// HomeAction u8 10, u8 5, i32 target map, i32 portal id. The UGC OX quiz event's portal request; the Maple2 server has no
/// handler for sub command 10, so it answers nothing (STILL-OPEN: the reply's reader, FUN_140dff600's sub types, is not ported).
/// </summary>
public sealed record HomeActionPortalRequest(int Target, int PortalId) : IOutPacket {
    public Outbound Op => Outbound.HomeAction;
    public void Write(ByteWriter w) { w.WriteByte(10); w.WriteByte(5); w.WriteInt(Target); w.WriteInt(PortalId); }
}
