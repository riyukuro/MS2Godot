using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Outbound InteractObject (SendOp 0x31, commonfiles/opcodes.h:354) mode 0x0b:
/// CInteractObject::vfunc_8 (@0x1413fe370, interactobject.cpp:596-606) sends count=1 with the object's lowercase
/// entity id; StateInteraction::SendInteractObjectMode11 (@0x1417d1ed0, stateinteractdata.cpp:432-446) sends all
/// usable gathering objects in reach. Wire: u8 0x0b, i32 count, count x str (ASCII/UTF-8, COutPacket::WriteString).
/// </summary>
public sealed record InteractObjectStartRequest(IReadOnlyList<string> Names) : IProfileOutPacket
{
    public Outbound Op => Outbound.InteractObject;

    public InteractObjectStartRequest(string name) : this(new[] { name }) { }

    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    /// <summary>GMS2 has no count: u8 mode, then one name (every recorded request). STILL-OPEN: a GMS2 request for several
    /// gathering objects at once is not in the recordings; the GMS2 form writes the first name.</summary>
    public void Write(ByteWriter w, ProtocolProfile profile)
    {
        w.WriteByte(0x0b);
        if (profile.IsGms2) { w.WriteString(Names.Count > 0 ? Names[0] ?? "" : ""); return; }
        w.WriteInt(Names.Count);
        for (int i = 0; i < Names.Count; i++) w.WriteString(Names[i] ?? "");
    }
}

/// <summary>
/// Outbound InteractObject (SendOp 0x31, commonfiles/opcodes.h:354) mode 0x0c:
/// CInteractObject::vfunc_10 (@0x1413fe440, interactobject.cpp:614-624) sends count=1 (either immediately when
/// reactTime == 0 or on cast completion); StateInteraction::SendInteractObjectMode12 (@0x1417d2070,
/// stateinteractdata.cpp:449-464) sends all still-usable gathering objects. Wire: u8 0x0c, i32 count, count x str.
/// </summary>
public sealed record InteractObjectCompleteRequest(IReadOnlyList<string> Names) : IProfileOutPacket
{
    public Outbound Op => Outbound.InteractObject;

    public InteractObjectCompleteRequest(string name) : this(new[] { name }) { }

    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    /// <summary>GMS2 has no count: u8 mode, then one name (every recorded request). STILL-OPEN: a GMS2 request for several
    /// gathering objects at once is not in the recordings; the GMS2 form writes the first name.</summary>
    public void Write(ByteWriter w, ProtocolProfile profile)
    {
        w.WriteByte(0x0c);
        if (profile.IsGms2) { w.WriteString(Names.Count > 0 ? Names[0] ?? "" : ""); return; }
        w.WriteInt(Names.Count);
        for (int i = 0; i < Names.Count; i++) w.WriteString(Names[i] ?? "");
    }
}

/// <summary>
/// Inbound InteractObject (RecvOp 0x63, commonfiles/opcodes.h:107), CSceneStateGame::OnInteractObject
/// (@0x14172d0f0, scenestategame.cpp:6218-6494): u8 mode, then per mode:
/// <list type="bullet">
/// <item>4 State (SSG_INTERACT_STATE, :6433): str name, u8 state, u8 kind.</item>
/// <item>5 Message (SSG_INTERACT_MSG, :6223): str name, u8 kind; for kind 6 (CInteractGatheringObject::vfunc_2
///   @0x141402490, interactobject.cpp:1450-1478) also i16 gatherType, i32 gatherCount.</item>
/// <item>6 KindState (SSG_INTERACT_KIND_STATE, :6456): i32 rowId, u8 state.</item>
/// <item>7 AllState (SSG_INTERACT_ALL_STATE, :6463): u8 state.</item>
/// <item>8 States (SSG_INTERACT_STATES, :6441): i32 count, repeated (str name, u8 state, u8 kind, and if kind == 6
///   i32 respawnTick).</item>
/// <item>9 Add (SSG_INTERACT_ADD, :6310): str name, u8 state, u8 kind, i32 rowId, vec3 pos, vec3 rot, wstr model,
///   wstr anim, wstr assetA, wstr assetB, f32 scale, bool flag; if kind == 8 (billboard, :6383) also i64 boardOwner,
///   wstr boardText. (ReadUnicodeStringA @commonfiles/packet.cpp:537 reads UTF-16LE via ReadUnicodeString.)</item>
/// <item>10 Remove (SSG_INTERACT_REMOVE, :6408): str name, wstr effect.</item>
/// <item>13 Result (SSG_INTERACT_RESULT, :6236): u8 result, str name, u8 kind.</item>
/// <item>14 Notice (SSG_INTERACT_NOTICE, :6469): u16 channel, i32 mapId, i32 nameId, wstr arg.</item>
/// <item>15 Weapon (SSG_INTERACT_WEAPON, :6482): i32 pcId, i32 weaponId.</item>
/// </list>
/// </summary>
public sealed class InteractObjectMessage
{
    public const byte ModeState = 4, ModeMessage = 5, ModeKindState = 6, ModeAllState = 7, ModeStates = 8,
        ModeAdd = 9, ModeRemove = 10, ModeResult = 13, ModeNotice = 14, ModeWeapon = 15;

    public const byte KindMesh = 1, KindTelescope = 2, KindUi = 3, KindWeb = 4, KindDisplayImage = 5,
        KindGathering = 6, KindGuildPoster = 7, KindBillBoard = 8, KindWatchTower = 9;

    public readonly record struct StateEntry(string Name, byte State, byte Kind, int RespawnTick);

    public byte Mode;
    public string Name = "";
    public byte State, Kind, Result;
    public int RowId;
    public short GatherType;
    public int GatherCount;
    public List<StateEntry> Entries;
    public Vector3 Position, Rotation;
    public string Model = "", Anim = "", AssetA = "", AssetB = "", Effect = "", BoardText = "", NoticeArg = "";
    public float Scale;
    public bool Flag;
    public long BoardOwner;
    public ushort Channel;
    public int MapId, NameId, PcId, WeaponId;

    public static InteractObjectMessage Read(ByteReader r)
    {
        var m = new InteractObjectMessage { Mode = r.ReadByte() };
        switch (m.Mode)
        {
            case ModeState:
                m.Name = r.ReadString();
                m.State = r.ReadByte();
                m.Kind = r.ReadByte();
                break;
            case ModeMessage:
                m.Name = r.ReadString();
                m.Kind = r.ReadByte();
                if (m.Kind == KindGathering)
                {
                    m.GatherType = r.ReadShort();
                    m.GatherCount = r.ReadInt();
                }
                break;
            case ModeKindState:
                m.RowId = r.ReadInt();
                m.State = r.ReadByte();
                break;
            case ModeAllState:
                m.State = r.ReadByte();
                break;
            case ModeStates:
            {
                int count = r.ReadInt();
                m.Entries = new List<StateEntry>(Math.Max(0, count));
                for (int i = 0; i < count; i++)
                {
                    string name = r.ReadString();
                    byte state = r.ReadByte();
                    byte kind = r.ReadByte();
                    int respawn = kind == KindGathering ? r.ReadInt() : 0;
                    m.Entries.Add(new StateEntry(name, state, kind, respawn));
                }
                break;
            }
            case ModeAdd:
                m.Name = r.ReadString();
                m.State = r.ReadByte();
                m.Kind = r.ReadByte();
                m.RowId = r.ReadInt();
                m.Position = r.ReadVec3();
                m.Rotation = r.ReadVec3();
                m.Model = r.ReadUnicodeString();
                m.Anim = r.ReadUnicodeString();
                m.AssetA = r.ReadUnicodeString();
                m.AssetB = r.ReadUnicodeString();
                m.Scale = r.ReadFloat();
                m.Flag = r.ReadBool();
                if (m.Kind == KindBillBoard)
                {
                    m.BoardOwner = r.ReadLong();
                    m.BoardText = r.ReadUnicodeString();
                }
                break;
            case ModeRemove:
                m.Name = r.ReadString();
                m.Effect = r.ReadUnicodeString();
                break;
            case ModeResult:
                m.Result = r.ReadByte();
                m.Name = r.ReadString();
                m.Kind = r.ReadByte();
                break;
            case ModeNotice:
                m.Channel = (ushort)r.ReadShort();
                m.MapId = r.ReadInt();
                m.NameId = r.ReadInt();
                m.NoticeArg = r.ReadUnicodeString();
                break;
            case ModeWeapon:
                m.PcId = r.ReadInt();
                m.WeaponId = r.ReadInt();
                break;
            default:
                return m;   // client switch has no case for other modes: leaves remaining bytes unread
        }
        r.ExpectEnd("InteractObject");
        return m;
    }
}
