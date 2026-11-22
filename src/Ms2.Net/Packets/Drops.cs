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
    /// <summary>CDropItem m_nDropKind (the ctor's second argument): an unsigned 16-bit value (scenestategame.cpp:9588).</summary>
    public ushort DropKind;
    /// <summary>CDropItem m_n28c (FUN_14118fe60): the drop is thrown at a fixed offset instead of a random direction.</summary>
    public bool FixedThrow;
    /// <summary>CDropItem m_n298 (FUN_14118fe80): a pet trap's drop, hidden for a delay.</summary>
    public bool PetTrap;
    /// <summary>The item record; null for a meso drop.</summary>
    public EquipItem Item;

    /// <summary>FUN_14053f8c0: the three meso item ids, 90000001 to 90000003.</summary>
    public static bool IsMesoId(int itemId) => itemId is 0x55d4a81 or 0x55d4a82 or 0x55d4a83;

    /// <summary>CDropItem::IsMeso (dropitem.cpp:744): the meso amount is above 0. The add packet's item body is read by id alone (<see cref="IsMesoId"/>).</summary>
    public bool IsMeso => IsMesoId(ItemId) && Amount > 0;

    /// <summary>
    /// CDropItem::IsPickupAllowedForLocalPlayer (dropitem.cpp:887): owner type 2 is anyone's; 0 and 1 are the owner list's, which holds
    /// the one uid of the packet. InitDrop returns 0 for the others and OnFieldAddItem makes no drop.
    /// </summary>
    public bool IsPickupAllowed(long ownCharacterId) => OwnerType == 2 || (OwnerType <= 1 && OwnerUid == ownCharacterId);

    public static FieldAddItem Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>
    /// GMS2 (upstream FieldPacket.AddItem): i32 object id, i32 item id, i32 amount, bool has receiver and then its i64 uid, vec3
    /// position, i32 owner object id, i32 0, u8 drop type (0 default, 1, 2 player; 2 = anyone may pick it up), i32 rarity, i16, bool
    /// fixed position, bool, then the GMS2 item body unless meso. Without a receiver there is no owner list, so the drop is anyone's.
    /// </summary>
    public static FieldAddItem Read(ByteReader r, ProtocolProfile profile) {
        if (profile.IsGms2) {
            var g = new FieldAddItem { ObjectId = r.ReadInt(), ItemId = r.ReadInt(), Amount = r.ReadInt() };
            bool hasReceiver = r.ReadBool();
            if (hasReceiver) g.OwnerUid = r.ReadLong();
            g.Position = r.ReadVec3(); g.SourceObjectId = r.ReadInt(); r.ReadInt();
            byte type = r.ReadByte();
            g.OwnerType = hasReceiver ? (byte) (type == 2 ? 1 : 0) : (byte) 2;
            g.Rarity = r.ReadInt(); g.DropKind = (ushort) r.ReadShort(); g.FixedThrow = r.ReadBool(); g.PetTrap = r.ReadBool();
            if (!IsMesoId(g.ItemId)) { g.Item = new EquipItem { Id = g.ItemId, Rarity = g.Rarity, Slot = ItemReader.SlotOf?.Invoke(g.ItemId) ?? EquipSlot.Unknown }; ItemReader.ReadBodyInto(r, g.Item, profile); }
            return g;
        }
        var d = new FieldAddItem {
            ObjectId = r.ReadInt(), ItemId = r.ReadInt(), Amount = r.ReadInt(), OwnerUid = r.ReadLong(), Position = r.ReadVec3(),
            SourceObjectId = r.ReadInt(), OwnerType = r.ReadByte(), Rarity = r.ReadInt(), DropKind = (ushort)r.ReadShort(),
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

/// <summary>
/// Outbound 0x1C, CDropItem::SendRequestItemPickup (dropitem.cpp:936): i32 object id, bool "a picker is named", then i32 the picker's
/// object id when it is (a pet picks up for its owner; the player's own pickup sends 0 and so a single false).
/// </summary>
public sealed record RequestItemPickup(int ObjectId, int PickerId) : Ms2.Net.Wire.IOutPacket {
    public Outbound Op => Outbound.RequestItemPickup;
    public void Write(ByteWriter w) {
        w.WriteInt(ObjectId);
        w.WriteBool(PickerId != 0);
        if (PickerId != 0) w.WriteInt(PickerId);
    }
}

/// <summary>
/// Outbound 0x1D, MyPC::FUN_1415310e0 (mypc.cpp:4797): u8 count, then that many i32 object ids of the meso drops; at most
/// <see cref="MaxCount"/> (0x80) go in one packet, the client stops adding at that count.
/// </summary>
public sealed record RequestMesoPickup(IReadOnlyList<int> ObjectIds) : Ms2.Net.Wire.IOutPacket {
    public const int MaxCount = 0x80;
    public Outbound Op => Outbound.RequestMesoPickup;
    public void Write(ByteWriter w) {
        int n = Math.Min(ObjectIds.Count, MaxCount);
        w.WriteByte((byte)n);
        for (int i = 0; i < n; i++) w.WriteInt(ObjectIds[i]);
    }
}

/// <summary>
/// Inbound 0x2D, CSceneStateGame::OnFieldPickupItem (scenestategame.cpp:2420): u8 picked, i32 object id. A drop the client does not
/// have ends the read there. Picked: i32 the picker's object id, then i64 the meso value when the drop is a meso, then u32 the EP
/// gained when the drop is the EP ball (item 0x55d4a8a). Not picked: i32 mode (1 clears the drop's request flag).
/// </summary>
public sealed class FieldPickupItem {
    public bool Picked, DropKnown;
    public int ObjectId, PickerId, Mode;
    public long Meso;
    public uint Ep;
    public const int EpItemId = 0x55d4a8a;

    /// <param name="find">Whether the field has the drop <c>ObjectId</c> and if so its kind: (is meso, is the EP ball).</param>
    public static FieldPickupItem Read(ByteReader r, Func<int, (bool Known, bool IsMeso, bool IsEp)> find) {
        var d = new FieldPickupItem { Picked = r.ReadBool(), ObjectId = r.ReadInt() };
        var (known, meso, ep) = find(d.ObjectId);
        d.DropKnown = known;
        if (!known) return d;
        if (d.Picked) {
            d.PickerId = r.ReadInt();
            if (meso) d.Meso = r.ReadLong();
            if (ep) d.Ep = (uint)r.ReadInt();
        } else d.Mode = r.ReadInt();
        return d;
    }
}

/// <summary>
/// Inbound 0xBA, CSceneStateGame::OnItemDropNotice (scenestategame.cpp:8023): the dropper's name (u16 length + UTF-16), then the
/// item's id, grade and uid (FUN_140d2e2b0's arguments, the item link). The client shows common string 0x342 of the name and the
/// link as a system line of kind 0x14 (FUN_1412751b0).
/// </summary>
public sealed record ItemDropNotice(string Name, int ItemId, int Grade, long Uid) {
    public const int MessageId = 0x342;
    public static ItemDropNotice Read(ByteReader r) => new(r.ReadUnicodeString(), r.ReadInt(), r.ReadInt(), r.ReadLong());
}

/// <summary>
/// Inbound 0x2A, CSceneStateGame::OnItemUpdate (scenestategame.cpp:3214): the player's object id and an item uid. A player the
/// field does not have, or one that does not wear the uid, ends the handler. The local player's equip is left to the item event
/// dispatcher (no more is read); another player's equip has its item read again from the rest of the packet.
/// </summary>
public sealed class ItemUpdate {
    public int ObjectId; public long Uid;
    public bool PcKnown, EquipKnown, Own, Updated;

    /// <param name="find">The player's worn equip of the uid: whether the player is here, whether it is the local one, and the equip (null when not worn).</param>
    public static ItemUpdate Read(ByteReader r, Func<int, long, (bool PcKnown, bool Own, EquipItem Equip)> find) {
        var u = new ItemUpdate { ObjectId = r.ReadInt(), Uid = r.ReadLong() };
        var (pc, own, equip) = find(u.ObjectId, u.Uid);
        u.PcKnown = pc;
        if (!pc) return u;
        u.EquipKnown = equip != null;
        if (equip == null) return u;
        u.Own = own;
        if (own) return u;
        ItemReader.ReadBodyInto(r, equip);
        u.Updated = true;
        return u;
    }
}
