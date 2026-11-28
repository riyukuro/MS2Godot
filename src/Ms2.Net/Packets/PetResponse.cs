// Inbound 0xCC ResponsePet: CPetManager::OnResponsePet (petmanager.cpp:722).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>What a ResponsePet packet carries, by the client's mode (CPetManager::OnResponsePet, petmanager.cpp:722).</summary>
public enum PetResponseKind
{
    /// <summary>A mode the client has no case for: nothing more is read.</summary>
    Unknown,
    /// <summary>0, OnPetSummoned: i32 owner object id, i32 value, then the pet item (ReadPetItem).</summary>
    Summoned,
    /// <summary>1, OnPetUnsummoned: i32 owner object id, i64 pet item uid.</summary>
    Unsummoned,
    /// <summary>2, OnPetSummonSound: i32 owner object id.</summary>
    SummonSound,
    /// <summary>4, OnPetRenamed: i32 owner object id, the pet identity.</summary>
    Renamed,
    /// <summary>5, OnPetOptionsUpdate: i32 owner object id, the pet options.</summary>
    OptionsUpdate,
    /// <summary>6, OnPetFlagsUpdate: i32 owner object id, the pet flags.</summary>
    FlagsUpdate,
    /// <summary>7, OnPetCountList: i32 count, then (i32 key, u16 value).</summary>
    CountList,
    /// <summary>8, OnPetCountUpdate: i32 key, u16 value.</summary>
    CountUpdate,
    /// <summary>9, OnPetInfo: i32 owner object id, then identity, options and flags (the own pet's info).</summary>
    Info,
    /// <summary>10: i32 owner object id, i64 experience, i64 pet item uid.</summary>
    Experience,
    /// <summary>0xB, OnPetLevelUp: i32 owner object id, i32 level, i64 pet item uid.</summary>
    LevelUp,
    /// <summary>0xC: i32 count (PC::FUN_141663a70, the fusion count).</summary>
    FusionCount,
    /// <summary>0xF, OnPetAutoChanged: u8 flag.</summary>
    AutoChanged,
    /// <summary>0x10, OnOtherPetInfo: i32 owner object id, i32 item id, i64 uid, i32 rarity, then the item body.</summary>
    OtherPetInfo,
    /// <summary>0x11, OnPetItemDataUpdate: i32 owner object id, i64 uid, u8 flag, then the body of the item found by the uid.</summary>
    ItemDataUpdate,
    /// <summary>0x12: i32 owner object id, i32 value, i64 pet item uid (stored on the item by FUN_140528bb0).</summary>
    ItemValue,
    /// <summary>0x13, OnPetError: i32 error.</summary>
    Error,
    /// <summary>0x14, OnPetTrapSkillConsumed: i32 skill id.</summary>
    TrapSkillConsumed,
    /// <summary>0x15: i32 owner object id, then the pet item (ReadPetItem), when the owner has a pet.</summary>
    PetItem,
}

/// <summary>Unk_PetIdentity::Read (@0x1401fef40): unicode name, i64 experience, i32 value, i16 level, u16 state, u16 extra.</summary>
public sealed record PetIdentity(string Name, long Exp, int Value, short Level, ushort State, ushort Extra)
{
    public static PetIdentity Read(ByteReader r) =>
        new(r.ReadUnicodeString(), r.ReadLong(), r.ReadInt(), (short) r.ReadShort(), (ushort) r.ReadShort(), (ushort) r.ReadShort());
}

/// <summary>One Unk_PetOption (Unk_PetOptions::Read @0x1401ff4d0): i32 type, f32 value, i32 enabled.</summary>
public readonly record struct PetOption(int Type, float Value, int Enabled)
{
    public const int Size = 12;
}

/// <summary>ReadPetFlags (@0x1401ff930): eight u8, an i32, a u8 (the client reads them into one block, in the order of the wire here).</summary>
public sealed record PetFlags(byte F0, byte F1, byte F2, byte F3, byte F4, byte F5, byte F6, byte F7, int Amount, byte Last)
{
    public const int Size = 13;

    public static PetFlags Read(ByteReader r) =>
        new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadInt(), r.ReadByte());
}

/// <summary>One option list (Unk_PetOptions::Read): u8 count, then that many options.</summary>
public static class PetOptionsReader
{
    public static IReadOnlyList<PetOption> Read(ByteReader r)
    {
        int count = r.ReadByte();
        if (count * PetOption.Size > r.Available) throw new InvalidDataException($"pet option count {count}");
        var list = new List<PetOption>(count);
        for (int i = 0; i < count; i++) list.Add(new PetOption(r.ReadInt(), r.ReadFloat(), r.ReadInt()));
        return list;
    }
}

/// <summary>The pet item of ReadPetItem (PC::ReadPetItem @0x141663150): a flag, then item id, uid, rarity and the body.</summary>
public sealed record PetItemRef(int ItemId, long Uid, int Rarity, EquipItem Item);

/// <summary>
/// Inbound 0xCC / GMS2 0xCE (OnResponsePet, petmanager.cpp:722): u8 mode, then by <see cref="Kind"/> (see
/// <see cref="PetResponseKind"/> for each body). The mode numbers are the same on KMS and GMS2; only the item body differs by profile.
/// <para>The item body of OtherPetInfo and PetItem is read when the item data knows the id (CItem::FUN_140523c40 fails otherwise and
/// the client reads no body, petmanager.cpp:1058, pc.cpp:3375). ItemDataUpdate's item is the inventory one found by uid, so its
/// body can only be read through <paramref name="findItem"/>; without it (or when it finds nothing) the client returns with the body
/// unread and so does this.</para>
/// </summary>
public sealed record PetResponse(
    PetResponseKind Kind, byte Mode, int OwnerId, int Value, long Uid, long Exp, byte Flag,
    PetIdentity Identity, IReadOnlyList<PetOption> Options, PetFlags Flags,
    IReadOnlyList<KeyValuePair<int, ushort>> Counts, PetItemRef Item)
{
    /// <summary>
    /// GMS2 Summoned only: the i64 that follows the item body in every recorded summon (it equals the item's uid in all of them); 0 when
    /// the packet ends after the body. STILL-OPEN: the KMS client's reader (pc.cpp:3357) stops after the body, so what the GMS2 client does with it is unknown.
    /// </summary>
    public long TrailerUid { get; init; }

    static readonly IReadOnlyList<PetOption> NoOptions = Array.Empty<PetOption>();
    static readonly IReadOnlyList<KeyValuePair<int, ushort>> NoCounts = Array.Empty<KeyValuePair<int, ushort>>();

    static PetResponse Of(PetResponseKind kind, byte mode) =>
        new(kind, mode, 0, 0, 0, 0, 0, null, NoOptions, null, NoCounts, null);

    /// <summary>What the client's switch makes of a mode number.</summary>
    public static PetResponseKind KindOf(byte mode) => mode switch
    {
        0 => PetResponseKind.Summoned,
        1 => PetResponseKind.Unsummoned,
        2 => PetResponseKind.SummonSound,
        4 => PetResponseKind.Renamed,
        5 => PetResponseKind.OptionsUpdate,
        6 => PetResponseKind.FlagsUpdate,
        7 => PetResponseKind.CountList,
        8 => PetResponseKind.CountUpdate,
        9 => PetResponseKind.Info,
        10 => PetResponseKind.Experience,
        0xB => PetResponseKind.LevelUp,
        0xC => PetResponseKind.FusionCount,
        0xF => PetResponseKind.AutoChanged,
        0x10 => PetResponseKind.OtherPetInfo,
        0x11 => PetResponseKind.ItemDataUpdate,
        0x12 => PetResponseKind.ItemValue,
        0x13 => PetResponseKind.Error,
        0x14 => PetResponseKind.TrapSkillConsumed,
        0x15 => PetResponseKind.PetItem,
        _ => PetResponseKind.Unknown,
    };

    /// <summary>ReadPetItem: false (no item) or true with id, uid, rarity and, for an id the item data knows, the body.</summary>
    static PetItemRef ReadPetItem(ByteReader r, ProtocolProfile profile)
    {
        if (!r.ReadBool()) return null;
        int id = r.ReadInt();
        long uid = r.ReadLong();
        int rarity = r.ReadInt();
        return new PetItemRef(id, uid, rarity, ReadBodyOf(r, id, uid, rarity, profile));
    }

    static EquipItem ReadBodyOf(ByteReader r, int id, long uid, int rarity, ProtocolProfile profile)
    {
        if (ItemReader.Known != null && !ItemReader.Known(id)) return null;
        var item = ItemReader.ReadItem(r, id, rarity, profile);
        item.Uid = uid;
        return item;
    }

    public static PetResponse Read(ByteReader r, ProtocolProfile profile, Func<long, EquipItem> findItem = null)
    {
        byte mode = r.ReadByte();
        var kind = KindOf(mode);
        var o = Of(kind, mode);
        switch (kind)
        {
            case PetResponseKind.Summoned:
            {
                int owner = r.ReadInt();
                int value = r.ReadInt();
                var item = ReadPetItem(r, profile);
                long trailer = 0;
                // The client reads up to the item body (pc.cpp:3357); a packet that ends there (the upstream server's
                // PetPacket.Summon) is read the same way.
                if (item?.Item != null && profile.IsGms2 && r.Available > 0) trailer = r.ReadLong();
                if (item == null || item.Item != null) r.ExpectEnd("ResponsePet summoned");
                return o with { OwnerId = owner, Value = value, Item = item, TrailerUid = trailer };
            }
            case PetResponseKind.Unsummoned:
            {
                int owner = r.ReadInt();
                long uid = r.ReadLong();
                r.ExpectEnd("ResponsePet unsummoned");
                return o with { OwnerId = owner, Uid = uid };
            }
            case PetResponseKind.SummonSound:
            {
                int owner = r.ReadInt();
                r.ExpectEnd("ResponsePet summon sound");
                return o with { OwnerId = owner };
            }
            case PetResponseKind.Renamed:
            {
                int owner = r.ReadInt();
                var id = PetIdentity.Read(r);
                r.ExpectEnd("ResponsePet renamed");
                return o with { OwnerId = owner, Identity = id };
            }
            case PetResponseKind.OptionsUpdate:
            {
                int owner = r.ReadInt();
                var opts = PetOptionsReader.Read(r);
                r.ExpectEnd("ResponsePet options");
                return o with { OwnerId = owner, Options = opts };
            }
            case PetResponseKind.FlagsUpdate:
            {
                int owner = r.ReadInt();
                var flags = PetFlags.Read(r);
                r.ExpectEnd("ResponsePet flags");
                return o with { OwnerId = owner, Flags = flags };
            }
            case PetResponseKind.CountList:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 6) throw new InvalidDataException($"ResponsePet count list {count}");
                var list = new List<KeyValuePair<int, ushort>>(count);
                for (int i = 0; i < count; i++) list.Add(new(r.ReadInt(), (ushort) r.ReadShort()));
                r.ExpectEnd("ResponsePet count list");
                return o with { Counts = list };
            }
            case PetResponseKind.CountUpdate:
            {
                int key = r.ReadInt();
                ushort v = (ushort) r.ReadShort();
                r.ExpectEnd("ResponsePet count update");
                return o with { Counts = new[] { new KeyValuePair<int, ushort>(key, v) } };
            }
            case PetResponseKind.Info:
            {
                int owner = r.ReadInt();
                var id = PetIdentity.Read(r);
                var opts = PetOptionsReader.Read(r);
                var flags = PetFlags.Read(r);
                r.ExpectEnd("ResponsePet info");
                return o with { OwnerId = owner, Identity = id, Options = opts, Flags = flags };
            }
            case PetResponseKind.Experience:
            {
                int owner = r.ReadInt();
                long exp = r.ReadLong();
                long uid = r.ReadLong();
                r.ExpectEnd("ResponsePet experience");
                return o with { OwnerId = owner, Exp = exp, Uid = uid };
            }
            case PetResponseKind.LevelUp:
            case PetResponseKind.ItemValue:
            {
                int owner = r.ReadInt();
                int value = r.ReadInt();
                long uid = r.ReadLong();
                r.ExpectEnd("ResponsePet level");
                return o with { OwnerId = owner, Value = value, Uid = uid };
            }
            case PetResponseKind.FusionCount:
            case PetResponseKind.Error:
            case PetResponseKind.TrapSkillConsumed:
            {
                int value = r.ReadInt();
                r.ExpectEnd("ResponsePet value");
                return o with { Value = value };
            }
            case PetResponseKind.AutoChanged:
            {
                byte flag = r.ReadByte();
                r.ExpectEnd("ResponsePet auto");
                return o with { Flag = flag };
            }
            case PetResponseKind.OtherPetInfo:
            {
                int owner = r.ReadInt();
                int id = r.ReadInt();
                long uid = r.ReadLong();
                int rarity = r.ReadInt();
                var body = ReadBodyOf(r, id, uid, rarity, profile);
                if (body != null) r.ExpectEnd("ResponsePet other pet info");
                return o with { OwnerId = owner, Uid = uid, Item = new PetItemRef(id, uid, rarity, body) };
            }
            case PetResponseKind.ItemDataUpdate:
            {
                int owner = r.ReadInt();
                long uid = r.ReadLong();
                byte flag = r.ReadByte();
                var found = findItem?.Invoke(uid);
                if (found != null)
                {
                    ItemReader.ReadBodyInto(r, found, profile);
                    r.ExpectEnd("ResponsePet item data");
                }
                return o with { OwnerId = owner, Uid = uid, Flag = flag, Item = found == null ? null : new PetItemRef(found.Id, uid, found.Rarity, found) };
            }
            case PetResponseKind.PetItem:
            {
                int owner = r.ReadInt();
                var item = ReadPetItem(r, profile);
                if (item == null || item.Item != null) r.ExpectEnd("ResponsePet pet item");
                return o with { OwnerId = owner, Item = item };
            }
            default:
                return o;
        }
    }
}
