// Layouts from the restored client's readers (CSceneStateGame::OnFieldAddItem @0x141711e70, OnFieldRemoveItem @0x141712340), not the server.
using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x2B, the packet CSceneStateGame::OnFieldAddItem (scenestategame.cpp:9576) reads: i32 object id, i32 item id, i32 amount,
/// i64 owner uid (the one entry of the drop's owner list), vec3 position, i32 source object id (the npc or player the drop flies out
/// of), u8 owner type (2 = anyone may pick it up, 0 / 1 = only the owner list), i32 rarity, i16 drop kind, bool, bool, then the item
/// record (CItem::ReadFromStream) unless the id is one of the three meso ids (FUN_14053f8c0, itemdata.cpp:1889), whose amount is
/// the meso value and which carry no record.
/// </summary>
public sealed class FieldAddItem {
    public int ObjectId, ItemId, Amount;
    public long OwnerUid;
    public Vector3 Position;
    /// <summary>The object the drop flies out of (CDropItem m_nOwnerObjId, set by InitDrop).</summary>
    public int SourceObjectId;
    public byte OwnerType;
    /// <summary>The item's rarity (the grade key of the drop model, GetGradeNif).</summary>
    public int Rarity;
    /// <summary>CDropItem m_nDropKind (the ctor's second argument).</summary>
    public short DropKind;
    /// <summary>CDropItem m_n28c (FUN_14118fe60): the drop is thrown at a fixed offset instead of a random direction.</summary>
    public bool FixedThrow;
    /// <summary>CDropItem m_n298 (FUN_14118fe80): a pet trap's drop, hidden for a delay.</summary>
    public bool PetTrap;
    /// <summary>The item record; null for a meso drop.</summary>
    public EquipItem Item;

    /// <summary>FUN_14053f8c0: the three meso item ids, 90000001 to 90000003.</summary>
    public static bool IsMesoId(int itemId) => itemId is 0x55d4a81 or 0x55d4a82 or 0x55d4a83;

    public bool IsMeso => IsMesoId(ItemId);

    /// <summary>
    /// CDropItem::IsPickupAllowedForLocalPlayer (dropitem.cpp:887): owner type 2 is anyone's; 0 and 1 are the owner list's, which holds
    /// the one uid of the packet. InitDrop returns 0 for the others and OnFieldAddItem makes no drop.
    /// </summary>
    public bool IsPickupAllowed(long ownCharacterId) => OwnerType == 2 || (OwnerType <= 1 && OwnerUid == ownCharacterId);

    public static FieldAddItem Read(ByteReader r) {
        var d = new FieldAddItem {
            ObjectId = r.ReadInt(), ItemId = r.ReadInt(), Amount = r.ReadInt(), OwnerUid = r.ReadLong(), Position = r.ReadVec3(),
            SourceObjectId = r.ReadInt(), OwnerType = r.ReadByte(), Rarity = r.ReadInt(), DropKind = r.ReadShort(),
            FixedThrow = r.ReadBool(), PetTrap = r.ReadBool(),
        };
        if (!IsMesoId(d.ItemId)) d.Item = ItemReader.ReadItem(r, d.ItemId, d.Rarity);
        return d;
    }
}

/// <summary>Inbound 0x2C, OnFieldRemoveItem (scenestategame.cpp:2402): i32 object id.</summary>
public readonly record struct FieldRemoveItem(int ObjectId) {
    public static FieldRemoveItem Read(ByteReader r) => new(r.ReadInt());
}
