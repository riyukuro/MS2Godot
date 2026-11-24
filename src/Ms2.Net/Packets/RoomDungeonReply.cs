// Inbound 0x1A RoomDungeon: CSceneStateGame::OnRoomDungeon (scenestategame.cpp:4531, @0x141737360) -> DungeonRecordManagerCli::OnPacket (@0x140b20be0).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>What a RoomDungeon (0x1A) packet carries, by the client's mode (DungeonRecordManagerCli::OnPacket, dungeonrecordcli.cpp:203).</summary>
public enum RoomDungeonKind
{
    /// <summary>A mode the client has no case for (or one this profile's number table does not name): nothing more is read.</summary>
    Unknown,
    /// <summary>KMS 5, OnRecords: the whole record table.</summary>
    Records,
    /// <summary>KMS 6, OnAddRecord: one record, stored by room id.</summary>
    AddRecord,
    /// <summary>KMS 7, OnRewardNotice: u8 kind, i32 value.</summary>
    RewardNotice,
    /// <summary>KMS 10, OnClosingPhoto: the end-of-dungeon record screen's body.</summary>
    ClosingPhoto,
    /// <summary>KMS 11, OnOpenDialog: opens dialog 0x4ef9, no body.</summary>
    OpenDialog,
    /// <summary>KMS 0x12, OnError: i32 error, i32 argument.</summary>
    Error,
    /// <summary>KMS 0x13, OnRewards: the whole rank reward table.</summary>
    Rewards,
    /// <summary>KMS 0x14, OnAddReward: one rank reward.</summary>
    AddReward,
    /// <summary>KMS 0x16, OnBoostState: two i64.</summary>
    BoostState,
}

/// <summary>
/// One DungeonRecord (operator>> @0x14031bc00, dungeonrecord.cpp:200), 49 bytes: u32 room id, i64 union cooldown, u8 union clears,
/// u8 union sub clears, i64 union sub cooldown, u8 extra sub clears, u8 extra clears, i64 clear time, i32 total clears, i16 lifetime
/// record, i64 cooldown, i16 current record, u8 flag. The order is the recorded one (every recorded record decodes with it, with
/// -1 in both record fields when unset).
/// <para>STILL-OPEN: the client's decomp reads the timed 16-bit field as value then expire time, which does not fit the wire
/// (the i64 comes between the two shorts); the field names follow the wire, not the client's member offsets.</para>
/// </summary>
public sealed record DungeonRoomRecord(
    uint RoomId, long UnionCooldown, byte UnionClears, byte UnionSubClears, long UnionSubCooldown,
    byte ExtraSubClears, byte ExtraClears, long ClearTime,
    int TotalClears, short LifetimeRecord, long Cooldown, short CurrentRecord,
    byte Flags)
{
    public const int Size = 49;

    /// <summary>The key the record table stored it under (ReadRecordMap, @0x140b22f40: the u32 before the record); 0 for a record from AddRecord.</summary>
    public uint Key { get; init; }

    public static DungeonRoomRecord Read(ByteReader r) => new(
        (uint) r.ReadInt(), r.ReadLong(), r.ReadByte(), r.ReadByte(), r.ReadLong(),
        r.ReadByte(), r.ReadByte(), r.ReadLong(),
        r.ReadInt(), (short) r.ReadShort(), r.ReadLong(), (short) r.ReadShort(),
        r.ReadByte());
}

/// <summary>One DungeonRankReward (operator>> @0x14031be60): u32 id, i32 value, i64 time. In the table it follows its u32 key (the id again).</summary>
public sealed record DungeonRankRewardEntry(uint Id, int Value, long Time)
{
    public const int Size = 16;

    /// <summary>The key the reward table stored it under (ReadRankRewardMap, @0x140b22ea0: the u32 before the reward); 0 for AddReward.</summary>
    public uint Key { get; init; }

    public static DungeonRankRewardEntry Read(ByteReader r) => new((uint) r.ReadInt(), r.ReadInt(), r.ReadLong());
}

/// <summary>One member's line of the closing photo (UIDungeonClosingPhotoDialog::OnClosingPhotoPacket @0x140e0eb40): character uid, rank, record type, value.</summary>
public readonly record struct ClosingPhotoEntry(long CharacterId, int Rank, int RecordType, int Value);

/// <summary>
/// Inbound 0x1A (OnRoomDungeon, scenestategame.cpp:4531): the dungeon room records. u8 mode, then by <see cref="Kind"/> (see
/// <see cref="RoomDungeonKind"/> for each body). The KMS client's modes are 5, 6, 7, 10, 11, 0x12, 0x13, 0x14 and 0x16. The GMS2
/// server numbers the modes from 10 on one higher (0x0B, 0x14, 0x15, 0x17 are the recorded ones: closing photo, rank rewards, add
/// reward, boost state); 5, 6 and 7 are the same. A mode the profile's table does not name, or the client has no case for, reads
/// nothing more, as the client's switch does.
/// <para>STILL-OPEN: the GMS2 numbers of OnOpenDialog (0x0C if the shift is uniform) and OnError (0x13 if uniform; the Maple2 server
/// sends 0x12) are in no recording, so neither is read on GMS2.</para>
/// </summary>
public sealed record RoomDungeonReply(
    RoomDungeonKind Kind, byte Mode,
    IReadOnlyList<DungeonRoomRecord> Records,
    DungeonRoomRecord Record,
    byte NoticeKind, int NoticeValue,
    byte Result, IReadOnlyList<ClosingPhotoEntry> Closing,
    int Error, int ErrorArg,
    IReadOnlyList<DungeonRankRewardEntry> Rewards,
    DungeonRankRewardEntry Reward,
    long BoostA, long BoostB)
{
    static readonly IReadOnlyList<DungeonRoomRecord> NoRecords = Array.Empty<DungeonRoomRecord>();
    static readonly IReadOnlyList<ClosingPhotoEntry> NoClosing = Array.Empty<ClosingPhotoEntry>();
    static readonly IReadOnlyList<DungeonRankRewardEntry> NoRewards = Array.Empty<DungeonRankRewardEntry>();

    static RoomDungeonReply Of(RoomDungeonKind kind, byte mode) =>
        new(kind, mode, NoRecords, null, 0, 0, 0, NoClosing, 0, 0, NoRewards, null, 0, 0);

    /// <summary>What the number the profile sent means: KMS as is; on GMS2 only the recorded numbers (5, 6, 7, then the client's modes from 10 on, sent one higher).</summary>
    public static RoomDungeonKind KindOf(byte mode, ProtocolProfile profile)
    {
        if (profile.IsGms2)
            return mode switch
            {
                5 => RoomDungeonKind.Records,
                6 => RoomDungeonKind.AddRecord,
                7 => RoomDungeonKind.RewardNotice,
                0x0B => RoomDungeonKind.ClosingPhoto,
                0x14 => RoomDungeonKind.Rewards,
                0x15 => RoomDungeonKind.AddReward,
                0x17 => RoomDungeonKind.BoostState,
                _ => RoomDungeonKind.Unknown,
            };
        return mode switch
        {
            5 => RoomDungeonKind.Records,
            6 => RoomDungeonKind.AddRecord,
            7 => RoomDungeonKind.RewardNotice,
            10 => RoomDungeonKind.ClosingPhoto,
            11 => RoomDungeonKind.OpenDialog,
            0x12 => RoomDungeonKind.Error,
            0x13 => RoomDungeonKind.Rewards,
            0x14 => RoomDungeonKind.AddReward,
            0x16 => RoomDungeonKind.BoostState,
            _ => RoomDungeonKind.Unknown,
        };
    }

    public static RoomDungeonReply Read(ByteReader r, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        var kind = KindOf(mode, profile);
        switch (kind)
        {
            case RoomDungeonKind.Records:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / (4 + DungeonRoomRecord.Size)) throw new InvalidDataException($"RoomDungeon record count {count}");
                var list = new List<DungeonRoomRecord>(count);
                for (int i = 0; i < count; i++)
                {
                    uint key = (uint) r.ReadInt();   // the table key, the record's own room id follows
                    list.Add(DungeonRoomRecord.Read(r) with { Key = key });
                }
                r.ExpectEnd("RoomDungeon records");
                return Of(kind, mode) with { Records = list };
            }
            case RoomDungeonKind.AddRecord:
            {
                var rec = DungeonRoomRecord.Read(r);
                r.ExpectEnd("RoomDungeon add record");
                return Of(kind, mode) with { Record = rec };
            }
            case RoomDungeonKind.RewardNotice:
            {
                byte nk = r.ReadByte();
                int v = r.ReadInt();
                r.ExpectEnd("RoomDungeon reward notice");
                return Of(kind, mode) with { NoticeKind = nk, NoticeValue = v };
            }
            case RoomDungeonKind.ClosingPhoto:
            {
                byte res = r.ReadByte();
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 20) throw new InvalidDataException($"RoomDungeon closing photo count {count}");
                var list = new List<ClosingPhotoEntry>(count);
                for (int i = 0; i < count; i++) list.Add(new ClosingPhotoEntry(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadInt()));
                r.ExpectEnd("RoomDungeon closing photo");
                return Of(kind, mode) with { Result = res, Closing = list };
            }
            case RoomDungeonKind.Error:
            {
                int e = r.ReadInt();
                int a = r.ReadInt();
                r.ExpectEnd("RoomDungeon error");
                return Of(kind, mode) with { Error = e, ErrorArg = a };
            }
            case RoomDungeonKind.Rewards:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / (4 + DungeonRankRewardEntry.Size)) throw new InvalidDataException($"RoomDungeon reward count {count}");
                var list = new List<DungeonRankRewardEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    uint key = (uint) r.ReadInt();
                    list.Add(DungeonRankRewardEntry.Read(r) with { Key = key });
                }
                r.ExpectEnd("RoomDungeon rewards");
                return Of(kind, mode) with { Rewards = list };
            }
            case RoomDungeonKind.AddReward:
            {
                var rw = DungeonRankRewardEntry.Read(r);
                r.ExpectEnd("RoomDungeon add reward");
                return Of(kind, mode) with { Reward = rw };
            }
            case RoomDungeonKind.BoostState:
            {
                long a = r.ReadLong();
                long b = r.ReadLong();
                r.ExpectEnd("RoomDungeon boost state");
                return Of(kind, mode) with { BoostA = a, BoostB = b };
            }
            case RoomDungeonKind.OpenDialog:
                // OnOpenDialog reads nothing (dungeonrecordcli.cpp:296); a body, if any, is left unread as the client does
                return Of(kind, mode);
            default:
                return Of(kind, mode);
        }
    }
}
