using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>PC::SendRequestCube (@0x14165f170, pc.cpp:1929-1937): with an item held, the item is let go and RequestCube
/// (0x37) is sent with u8 0x12 and nothing else.</summary>
public sealed record HeldItemReleaseRequest : IOutPacket
{
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) => w.WriteByte(0x12);
}

/// <summary>Unk_PCCubeCraft::ConfirmPlacement (@0x1410aad50, cubecraft_2.cpp:1000-1007), no cube in the cell: RequestCube
/// (0x37) with u8 10, the cell key (FUN_1408a13d0), the craft's CubeItemInfo (FUN_1408b39c0) and the yaw as a float.</summary>
public sealed record CubePlaceRequest(int CellKey, CubeItemInfo Item, float Yaw) : IOutPacket
{
    public const byte Mode = 10;
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteInt(CellKey);
        Item.Write(w);
        w.WriteFloat(Yaw);
    }
}

/// <summary>
/// Inbound ResponseCube (RecvOp 0x69, commonfiles/opcodes.h:113), CSceneStateGame::OnResponseCube
/// (scenestategame.cpp:9876): a u8 mode. Read are the two held-item modes; other modes are read as null.
/// <list type="bullet">
/// <item>0x11 (:10145-10167): u8 error; error 0: i32 pc id, i32 cube key, i32 item id, i32 value (the cube's weapon value
/// m_n1c, then the pc holds the item); otherwise i32 the character the build notice is for.</item>
/// <item>0x12 (:10176-10192): u8 error; then i32 id, which with error 0 is the pc whose held item is let go
/// (PC::FUN_14165f1f0(0, 0)) and otherwise the character the build notice is for.</item>
/// </list>
/// </summary>
public sealed record ResponseCubeHeldItem(byte Mode, byte Error, int Id, int CubeKey = 0, int ItemId = 0, int Value = 0)
{
    public const byte ModeHold = 0x11;
    public const byte ModeRelease = 0x12;

    public static ResponseCubeHeldItem Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode is not (ModeHold or ModeRelease)) return null;
        byte error = r.ReadByte();
        if (mode == ModeHold && error == 0)
        {
            int id = r.ReadInt(), cube = r.ReadInt(), item = r.ReadInt(), value = r.ReadInt();
            return new ResponseCubeHeldItem(mode, error, id, cube, item, value);
        }
        return new ResponseCubeHeldItem(mode, error, r.ReadInt());
    }
}

/// <summary>CubeItemInfo (ugcmapoptions.cpp): a cube's item id, instance id, count and its optional UGC look.</summary>
public sealed class CubeItemInfo
{
    public int ItemId;
    public long InstanceId;
    public long Count;
    /// <summary>The uploaded look (CUgcItemLook), null without one.</summary>
    public UgcLook Look;

    /// <summary>CubeItemInfo::FUN_1408b3860 (@0x1408b3860, ugcmapoptions.cpp:132-137): an item id, a look and the look's
    /// uid (CUgcItemLook m_n18) all nonzero.</summary>
    public bool HasUploadedLook => ItemId != 0 && Look != null && Look.Uid != 0;

    /// <summary>CubeItemInfo::FUN_1408b3aa0 (@0x1408b3aa0, ugcmapoptions.cpp:181): i32 item id, i64 instance id, i64 count,
    /// u8 has look, then the look (CUgcItemLook::Read).</summary>
    public static CubeItemInfo Read(ByteReader r)
    {
        var info = new CubeItemInfo { ItemId = r.ReadInt(), InstanceId = r.ReadLong(), Count = r.ReadLong() };
        if (r.ReadByte() != 0) info.Look = ItemReader.ReadUgcLook(r);
        return info;
    }

    /// <summary>CubeItemInfo::FUN_1408b39c0 (@0x1408b39c0, ugcmapoptions.cpp:170): the same fields, the flag 1 when a look
    /// follows.</summary>
    public void Write(ByteWriter w)
    {
        w.WriteInt(ItemId);
        w.WriteLong(InstanceId);
        w.WriteLong(Count);
        w.WriteByte((byte)(Look != null ? 1 : 0));
        if (Look != null) ItemReader.WriteUgcLook(w, Look);
    }
}

/// <summary>
/// Inbound SetCraftMode (RecvOp 0x68, commonfiles/opcodes.h:112), CSceneStateGame::OnSetCraftMode (@0x141723410,
/// scenestategame.cpp:7827-7851): i32 pc id, u8 cube mode; a nonzero mode is followed by the CubeItemInfo and an i32, the
/// craft mode PC::vfunc_135 hands to the cube craft (FUN_141659250, pc.cpp:3040; Unk_PCCubeCraft's m_nMode, 2 = a held
/// lift, cubecraft_2.cpp:332).
/// </summary>
public sealed record SetCraftMode(int Id, byte CubeMode, CubeItemInfo Item, int CraftMode)
{
    public static SetCraftMode Read(ByteReader r)
    {
        int id = r.ReadInt();
        byte mode = r.ReadByte();
        if (mode == 0) { r.ExpectEnd("SetCraftMode"); return new SetCraftMode(id, 0, null, 0); }
        var item = CubeItemInfo.Read(r);
        int craft = r.ReadInt();
        r.ExpectEnd("SetCraftMode");
        return new SetCraftMode(id, mode, item, craft);
    }
}
