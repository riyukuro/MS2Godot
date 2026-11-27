using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The character's dungeon room records, DungeonRecordManagerCli (client/ms2/dungeonrecordcli.cpp, @0x140b20aa0) with its DungeonRecordCli
/// rows (commonfiles/dungeonrecord.cpp). The table keeps the records the server sent (0x1A modes 5 and 6) as the packet reader decoded
/// them, and every question the dungeon room window asks is answered here with the client's own rule, at the server time.
/// <para>The record fields follow the client's order of decode (operator>> @0x14031bc00): m_expire is the union cooldown, m_bonusRewardCount
/// the union clears, m_clearCount the sub clears and sub cooldown, m_bonusCount the extra sub clears, m_extraCount the extra clears and
/// clear time, m_5c the total clears, m_60 the lifetime record, m_68 the 16-bit timed value (read as the u16 value then the i64 expire
/// from the same ten bytes the wire reader splits as cooldown and current record), m_flags the flag byte (bit 0 the helper claim, bit 1 a
/// season room).</para>
/// STILL-OPEN: OnRates (GetRateA and GetRateB) has no packet number on either profile in the reader, so the rates are not kept; the used
/// bonus count (m_usedBonusCount) is not serialised and has no setter in the client's code, so it is not kept either.
/// </summary>
public sealed class DungeonRecordBook
{
    readonly Dictionary<uint, DungeonRoomRecord> records = new();
    readonly Dictionary<uint, DungeonRankRewardEntry> rewards = new();

    /// <summary>GetServerTime (unix seconds).</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>The room table (DungeonRoomTable::GetInstance), which every record resolves its room from (ResolveRoom @0x14031bb80).</summary>
    public DungeonRoomTable Rooms;
    /// <summary>CGameRule::GetDungeonExtraRewardCount(room) (@0x140b20990 asks it): the count an extra reward event asks for, null when no event runs for the room.</summary>
    public Func<uint, uint?> ExtraRewardEventCount = _ => null;

    /// <summary>DAT_143ab2828, the entered character's id, which the boost's owner is compared with.</summary>
    public Func<long> CharacterId = () => 0;

    /// <summary>The boost state of OnBoostState (@0x140b220a0): m_90 (the end time) and m_98 (the owner).</summary>
    public long BoostA { get; private set; }
    public long BoostB { get; private set; }

    /// <summary>GetRecordCount (@0x140b20b70).</summary>
    public int RecordCount => records.Count;
    public int RankRewardCount => rewards.Count;

    /// <summary>Clear (@0x140b20ba0): the records, the rank rewards and the boost are dropped.</summary>
    public void Clear() { records.Clear(); rewards.Clear(); BoostA = BoostB = 0; }

    /// <summary>OnPacket (@0x140b20be0) for the modes that touch the tables: OnRecords (@0x140b20d40, RemoveAll then the map),
    /// OnAddRecord (@0x140b20d90, the record's own room id), OnRewards, OnAddReward and OnBoostState. Returns whether a table changed.</summary>
    public bool Apply(RoomDungeonReply m)
    {
        switch (m.Kind)
        {
            case RoomDungeonKind.Records:
                records.Clear();
                foreach (var rec in m.Records) records[rec.Key] = rec;
                return true;
            case RoomDungeonKind.AddRecord:
                records[m.Record.RoomId] = m.Record;
                return true;
            case RoomDungeonKind.Rewards:
                rewards.Clear();
                foreach (var rw in m.Rewards) rewards[rw.Key] = rw;
                return true;
            case RoomDungeonKind.AddReward:
                rewards[m.Reward.Id] = m.Reward;
                return true;
            case RoomDungeonKind.BoostState:
                BoostA = m.BoostA; BoostB = m.BoostB;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The record as the server sent it, null when no packet named the room (the table is not touched, unlike <see cref="GetRecord"/>).</summary>
    public DungeonRoomRecord Peek(uint roomId) => records.TryGetValue(roomId, out var r) ? r : null;

    /// <summary>GetRecord (@0x140b22160): the record of the room, a new zeroed one (m_60 -1) stored under the id when the table has none.</summary>
    public DungeonRoomRecord GetRecord(uint roomId)
    {
        if (records.TryGetValue(roomId, out var r)) return r;
        r = new DungeonRoomRecord(roomId, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0) { Key = roomId };
        records[roomId] = r;
        return r;
    }

    DungeonRoomData RoomOf(uint roomId) => Rooms?.GetRoom(roomId);

    // ---- DungeonRecord (commonfiles/dungeonrecord.cpp) ----

    /// <summary>Unk_DungeonTimedValue::Get (@0x14031c2b0): 0 once the time has reached the expiry.</summary>
    static int Timed(int value, long expire, long now) => now >= expire ? 0 : value;

    /// <summary>
    /// GetWeeklyRewardCountText (@0x140e14d70): "" without a record, else "{count} / {max}" and, while the extra reward event runs for
    /// the room, the event's count formatted with <paramref name="eventCountFormat"/> (s_dungeonRoom_event_reward_count: this
    /// record's extra count, the event's count).
    /// </summary>
    public string WeeklyRewardCountText(uint id, string eventCountFormat)
    {
        var r = Peek(id);
        if (r == null) return "";
        string text = $"{BonusRewardCountNow(r)} / {MaxCount(r)}";
        uint? ev = ExtraRewardEventCount(id);
        if (ev != null) text += " " + Ms2.Quest.ClientFormat.Format(eventCountFormat ?? "", ExtraCountNow(r), (int) ev.Value);
        return text;
    }

    /// <summary>GetBonusRewardCount (@0x14031b9a0): the union clears, 0 once now >= m_expire.</summary>
    public static int BonusRewardCount(DungeonRoomRecord r, long now) => now >= r.UnionCooldown ? 0 : r.UnionClears;

    /// <summary>GetClearCount (@0x14031b9d0): the sub clears, timed by the sub cooldown.</summary>
    public static int ClearCount(DungeonRoomRecord r, long now) => Timed(r.UnionSubClears, r.UnionSubCooldown, now);

    /// <summary>GetExtraCount (@0x14031ba30): the extra clears, timed by the clear time.</summary>
    public static int ExtraCount(DungeonRoomRecord r, long now) => Timed(r.ExtraClears, r.ClearTime, now);

    /// <summary>GetBonusCount (@0x14031bb00): m_bonusCount, 0 once now >= m_expire.</summary>
    public static int BonusCount(DungeonRoomRecord r, long now) => now >= r.UnionCooldown ? 0 : r.ExtraSubClears;

    /// <summary>The u16 of m_68 (GetRaw68 @0x140b20a40): the low two bytes of the ten bytes after m_60, which the wire reader holds as cooldown and current record.</summary>
    public static ushort Raw68(DungeonRoomRecord r) => (ushort) r.Cooldown;

    /// <summary>The expiry of m_68: the eight bytes after its u16 (the rest of the reader's cooldown, then its current record).</summary>
    public static long Expire68(DungeonRoomRecord r) => (long) (((ulong) r.Cooldown >> 16) | ((ulong) (ushort) r.CurrentRecord << 48));

    /// <summary>Get68 (@0x140b208c0): the 16-bit timed value, 0 once now >= its expiry.</summary>
    public static ushort Get68(DungeonRoomRecord r, long now) => now >= Expire68(r) ? (ushort) 0 : Raw68(r);

    /// <summary>TestFlag (@0x14031b940): a bit of the flag byte.</summary>
    public static bool TestFlag(DungeonRoomRecord r, int bit) => bit >= 0 && bit < 8 && (r.Flags & (1 << bit)) != 0;

    // ---- DungeonRecordCli (client/ms2/dungeonrecordcli.cpp) ----

    /// <summary>IsExpired (@0x140b20a60): the server time has reached m_expire.</summary>
    public bool IsExpired(DungeonRoomRecord r) => ServerTime() >= r.UnionCooldown;

    /// <summary>GetBonusRewardCountNow (@0x140b20750).</summary>
    public int BonusRewardCountNow(DungeonRoomRecord r) => BonusRewardCount(r, ServerTime());

    /// <summary>GetExtraCountNow (@0x140b207f0).</summary>
    public int ExtraCountNow(DungeonRoomRecord r) => ExtraCount(r, ServerTime());

    /// <summary>GetBonusCountNow (@0x140b20840): m_bonusCount, 0 once expired.</summary>
    public int BonusCountNow(DungeonRoomRecord r) => IsExpired(r) ? 0 : r.ExtraSubClears;

    /// <summary>GetMaxCount (@0x140b20780): 0 without a room row, else the room's reward count (plus the bonus count while not expired).</summary>
    public int MaxCount(DungeonRoomRecord r)
    {
        var room = RoomOf(r.RoomId);
        if (room == null) return 0;
        return IsExpired(r) ? (int) room.RewardCount : (int) room.RewardCount + r.ExtraSubClears;
    }

    /// <summary>IsExtraExhausted (@0x140b20990): an extra reward event for the room asks for a count the extra clears have not reached.
    ///</summary>
    public bool IsExtraExhausted(DungeonRoomRecord r)
    {
        uint? ev = ExtraRewardEventCount(r.RoomId);
        return ev != null && (uint) ExtraCountNow(r) < ev.Value;
    }

    /// <summary>HasCountLeft (@0x140b208f0): the extra event test, then, when the room has sub rewards, no more once the sub clears reach them,
    /// else the union clears are under the maximum count. STILL-OPEN: the client reads the room through m_pRoom without a null check; a record
    /// of a room with no row counts its sub reward count as 0 and its maximum count as 0 here.</summary>
    public bool HasCountLeft(DungeonRoomRecord r)
    {
        if (IsExtraExhausted(r)) return true;
        uint sub = RoomOf(r.RoomId)?.SubRewardCount ?? 0;
        if (sub > 0 && (uint) ClearCount(r, ServerTime()) >= sub) return false;
        return (uint) BonusRewardCountNow(r) < (uint) MaxCount(r);
    }

    // ---- DungeonRecordManagerCli ----

    /// <summary>CanEnter (@0x140b22290).</summary>
    public bool CanEnter(uint roomId) => HasCountLeft(GetRecord(roomId));

    /// <summary>HasProgress (@0x140b22320): union clears or extra clears left to claim.</summary>
    public bool HasProgress(uint roomId)
    {
        var r = GetRecord(roomId);
        return BonusRewardCountNow(r) > 0 || ExtraCountNow(r) > 0;
    }

    /// <summary>CanClaimHelperReward (@0x140b223d0), without the local player check (the caller has one): the room row, its helper clear
    /// count, the record's total clears (Get5c) at least that and flag bit 0.</summary>
    public bool CanClaimHelperReward(uint roomId)
    {
        var room = RoomOf(roomId);
        if (room == null || room.DungeonHelperRequireClearCount == 0) return false;
        var r = GetRecord(roomId);
        return r.TotalClears >= room.DungeonHelperRequireClearCount && TestFlag(r, 0);
    }

    /// <summary>IsBoostActive (@0x140b22530): the boost's owner (m_98) is the entered character and the server time is before its end (m_90).</summary>
    public bool IsBoostActive() => CharacterId() == BoostB && ServerTime() < BoostA;

    /// <summary>IsSeasonRoom (@0x140b225a0): flag bit 1.</summary>
    public bool IsSeasonRoom(uint roomId) => TestFlag(GetRecord(roomId), 1);

    /// <summary>GetRoomScore60 (@0x140b22660): the lifetime record, -1 when unset.</summary>
    public short GetRoomScore60(uint roomId) => GetRecord(roomId).LifetimeRecord;

    /// <summary>GetRoomScore68 (@0x140b226f0).</summary>
    public ushort GetRoomScore68(uint roomId) => Get68(GetRecord(roomId), ServerTime());

    /// <summary>GetRoomValue5c (@0x140b22890): the total clears.</summary>
    public int GetRoomValue5c(uint roomId) => GetRecord(roomId).TotalClears;

    /// <summary>DungeonRecord::IsSeasonRankRoom (@0x14031b900): the record's room is a season rank room.</summary>
    bool IsSeasonRankRoom(DungeonRoomRecord r)
    {
        var room = RoomOf(r.RoomId);
        return room != null && Rooms.IsSeasonRankRoom(room);
    }

    /// <summary>GetTotalScore (@0x140b22780): the sum of the positive scores (Get68) of the season rank rooms' records.</summary>
    public int GetTotalScore()
    {
        long now = ServerTime();
        int total = 0;
        foreach (var r in records.Values)
        {
            if (!IsSeasonRankRoom(r)) continue;
            int score = Get68(r, now);
            if (score <= 0) continue;
            total += score;
        }
        return total;
    }

    /// <summary>GetRankRewardCount (@0x140b22910): the rank reward's value while the server time is before its time, else 0.</summary>
    public int GetRankRewardCount(uint rank) =>
        rewards.TryGetValue(rank, out var rw) && ServerTime() < rw.Time ? rw.Value : 0;

    /// <summary>GetScoredRecords (@0x140b229c0): the records with a positive Get68 score.</summary>
    public List<DungeonRoomRecord> GetScoredRecords()
    {
        long now = ServerTime();
        var list = new List<DungeonRoomRecord>();
        foreach (var r in records.Values)
            if (Get68(r, now) > 0) list.Add(r);
        return list;
    }

    /// <summary>GetRoomBonusCount (@0x140b22b40).</summary>
    public int GetRoomBonusCount(uint roomId) => BonusCountNow(GetRecord(roomId));

    /// <summary>GetExtraTicketCost (@0x140b22aa0): 0 without a room, else the room's extra ticket cost for the record's bonus count.</summary>
    public uint GetExtraTicketCost(DungeonRoomData room) =>
        room == null || Rooms == null ? 0 : Rooms.ExtraRewardTicketCost(room, (uint) GetRoomBonusCount(room.Id));

    /// <summary>GetSeasonRooms (@0x140b22bc0): the rooms of the records with flag bit 1 (a record with no room row gives none).</summary>
    public List<DungeonRoomData> GetSeasonRooms()
    {
        var list = new List<DungeonRoomData>();
        foreach (var r in records.Values)
            if (TestFlag(r, 1) && RoomOf(r.RoomId) is { } room) list.Add(room);
        return list;
    }
}
