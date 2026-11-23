// Inbound 0x24 FurnishingInventory: CSceneStateGame::OnFurnishingInventory (@0x141716200, scenestategame.cpp:9373).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x24 (OnFurnishingInventory, scenestategame.cpp:9373-9416): the housing cube stock, keyed by item uid. u8 mode, then:
/// <list type="bullet">
/// <item>0: clear the stock. No body.</item>
/// <item>1: add. i32 item id, i64 uid, i64 count (the client's name; the server writes the UGC expiry there), bool has look and,
/// when set, a UgcItemLook (<see cref="ItemReader.ReadUgcLook"/>).</item>
/// <item>2: remove. i64 uid.</item>
/// <item>3: update. i64 uid, i32 amount.</item>
/// <item>4: end of list. The GMS2 server sends it; the client has no case for it and ignores it. No body.</item>
/// </list>
/// Any other mode reads nothing more (the client's chain has no branch for it). The wire is the same on KMS and GMS2.
/// </summary>
public sealed record FurnishingInventory(byte Mode, int ItemId, long Uid, long ExpireOrCount, bool HasLook, UgcLook Look, int Amount)
{
    public static FurnishingInventory Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static FurnishingInventory Read(ByteReader r, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        switch (mode)
        {
            case 0:
            case 4:
                r.ExpectEnd("FurnishingInventory " + mode);
                return new FurnishingInventory(mode, 0, 0, 0, false, null, 0);
            case 1:
            {
                int itemId = r.ReadInt();
                long uid = r.ReadLong();
                long count = r.ReadLong();
                bool hasLook = r.ReadBool();
                var look = hasLook ? ItemReader.ReadUgcLook(r) : null;
                r.ExpectEnd("FurnishingInventory add");
                return new FurnishingInventory(mode, itemId, uid, count, hasLook, look, 0);
            }
            case 2:
            {
                long uid = r.ReadLong();
                r.ExpectEnd("FurnishingInventory remove");
                return new FurnishingInventory(mode, 0, uid, 0, false, null, 0);
            }
            case 3:
            {
                long uid = r.ReadLong();
                int amount = r.ReadInt();
                r.ExpectEnd("FurnishingInventory update");
                return new FurnishingInventory(mode, 0, uid, 0, false, null, amount);
            }
            default:
                return new FurnishingInventory(mode, 0, 0, 0, false, null, 0);
        }
    }
}
