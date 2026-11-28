using Maple2.PacketLib.Tools;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// DungeonMissionCli and its subclasses (dungeonmissioncli.cpp, factories registered by name in the dynamic initializers @0x1400c7840): the score a
/// mission has earned so far plus its <see cref="DungeonMissionData"/> row. Create (@0x140b1dd30) looks the row up and builds the class named
/// "DungeonMission" + type; an unknown id or a type the client has no class for gives null. The body (vfunc_2 @0x140b1df30) is an unsigned
/// short score; PlayTime (@0x140b1e2e0) adds two u64 times, the 13 Event subclasses (@0x140b1e520) a u16 count, the rest read the score only.
/// STILL-OPEN: the per-type value attributes of the data row (see <see cref="DungeonMissionTable"/>) are not read, so the text of
/// DungeonMissionCli::vfunc_4 (@0x140b1df70, @0x140b1e560) is not ported here.
/// </summary>
public sealed class DungeonMissionCli
{
    /// <summary>Which body the class reads after the score (the class the type names).</summary>
    public enum BodyKind { Score, PlayTime, Event }

    // The types with a client class (the registrars of dungeonmissioncli.cpp) and their body.
    static readonly Dictionary<string, BodyKind> Classes = new()
    {
        ["PlayTime"] = BodyKind.PlayTime,
        ["LimitUserCount"] = BodyKind.Score,
        ["Event"] = BodyKind.Event,
        ["DeathCount"] = BodyKind.Event, ["KillNpc"] = BodyKind.Event, ["HealingPotion"] = BodyKind.Event,
        ["DamageBySkill"] = BodyKind.Event, ["UseEp"] = BodyKind.Event, ["UseSp"] = BodyKind.Event,
        ["UseSkill"] = BodyKind.Event, ["GainBuff"] = BodyKind.Event, ["LastHitNpc"] = BodyKind.Event,
        ["UseInteractObject"] = BodyKind.Event, ["DamageCount"] = BodyKind.Event, ["DamageToNpc"] = BodyKind.Event,
        ["Trigger"] = BodyKind.Event,
    };

    public DungeonMissionData Data { get; }
    public BodyKind Body { get; }
    /// <summary>m_score (the earned score).</summary>
    public short Score { get; private set; }
    /// <summary>DungeonMissionEvent::m_count (the counted steps; 0 for the other classes).</summary>
    public short Count { get; private set; }
    /// <summary>DungeonMissionPlayTime::m_time1 and m_time2 (0 for the other classes).</summary>
    public ulong Time1 { get; private set; }
    public ulong Time2 { get; private set; }

    DungeonMissionCli(DungeonMissionData data, BodyKind body) { Data = data; Body = body; }

    /// <summary>Create (@0x140b1dd30); null when the table has no such mission or the client has no class of its type (it logs and the callers stop).</summary>
    public static DungeonMissionCli Create(DungeonMissionTable table, uint id)
    {
        var data = table?.GetMission(id);
        if (data == null) return null;
        return Classes.TryGetValue(data.Type, out var body) ? new DungeonMissionCli(data, body) : null;
    }

    /// <summary>vfunc_2 (@0x140b1df30 and the overrides): reads the body.</summary>
    public void Read(ByteReader r)
    {
        Score = unchecked((short)r.ReadShort());
        switch (Body)
        {
            case BodyKind.PlayTime: Time1 = unchecked((ulong)r.ReadLong()); Time2 = unchecked((ulong)r.ReadLong()); break;
            case BodyKind.Event: Count = r.ReadShort(); break;
        }
    }

    /// <summary>GetId (@0x140b1e0e0).</summary>
    public uint Id => Data.Id;
    /// <summary>GetMaxScore (@0x140b1e030).</summary>
    public short MaxScore => Data.MaxScore;

    /// <summary>IsMaxScore (@0x140b1e110): not for a mission without steps or a penalty mission.</summary>
    public bool IsMaxScore => Data.ApplyCount != 0 && !Data.IsPenaltyType && Score == Data.MaxScore;

    /// <summary>IsPenaltyCleared (@0x140b1e180): the client's test is a step count below 0 with no score left.</summary>
    public bool IsPenaltyCleared => Data.ApplyCount != 0 && Data.ApplyCount < 0 && Score == 0;
}

/// <summary>
/// RoomStageDungeonCli (roomstagedungeoncli.cpp): the client's state of the dungeon room it stands in. CMyCharacter::CreateStage (@0x140d9f400)
/// builds it from the RoomStageDungeon packet's room id and stage and sets the flag; the ctor (@0x140c9cc60) looks the room up and registers its
/// OnPacket (@0x140c9d0c0) for RecvOp DungeonMission (0x100), which <see cref="OnPacket"/> is. ClearStage (@0x140d9f4b0) drops it, and its dtor
/// unregisters the handler, so a mission packet with no stage does nothing (the owner calls <see cref="OnPacket"/> only while a stage exists).
/// The mode byte: 0 init the missions, 1 update some, 2 sync all, 3 stage, 4 flag, 5 give up; another mode does nothing. Each mode reads in the
/// client's order and stops where the client returns or breaks, with the earlier changes kept.
/// The owner clears it at the server's kind 1 and at every server enter (OnServerEnter -> FUN_14092f420 -> CMyCharacter::ClearDungeonState -> ClearStage,
/// scenestategame.cpp:2070, notifydebugstate.cpp:121-129).
/// </summary>
public sealed class RoomStageDungeonCli
{
    readonly DungeonMissionTable table;
    readonly DungeonRoomTable rooms;
    readonly List<DungeonMissionCli> missions = new();

    public RoomStageDungeonCli(uint roomId, int stage, bool flag, DungeonRoomTable rooms, DungeonMissionTable table)
    {
        this.rooms = rooms;
        this.table = table;
        Room = rooms?.GetRoom(roomId);
        Stage = stage;
        Flag = flag;
    }

    /// <summary>m_spRoom (null for a room the table does not have).</summary>
    public DungeonRoomData Room { get; }
    /// <summary>GetStage (@0x140c9d050).</summary>
    public int Stage { get; private set; }
    /// <summary>m_bFlag (SetFlag @0x140c9ce60).</summary>
    public bool Flag { get; private set; }
    /// <summary>GetMissions (@0x140c9ce80), in the order the server sent them.</summary>
    public IReadOnlyList<DungeonMissionCli> Missions => missions;

    /// <summary>OnGiveUp (@0x140c9d670): the notice s_party_notify_dungeon_giveup of type 0x14 (FUN_1412751b0); the owner shows it.</summary>
    public event Action GaveUp;

    /// <summary>GetScore (@0x140c9ceb0): the sum of the missions' scores.</summary>
    public int Score { get { int n = 0; foreach (var m in missions) n += m.Score; return n; } }
    /// <summary>GetMaxScore (@0x140c9cf50).</summary>
    public int MaxScore { get { int n = 0; foreach (var m in missions) n += m.MaxScore; return n; } }

    /// <summary>GetRank (@0x140c9cff0): 0 without the room, else the room's rank of the score (DungeonRoomTable.GetRank, which is -1 without a rank table).</summary>
    public int Rank => Room == null ? 0 : rooms.GetRank(Room, unchecked((short)Score));

    /// <summary>IsStageFlagSet (@0x140c9d6f0): only in stage 0 and with the flag set.</summary>
    public bool IsStageFlagSet => Stage == 0 && Flag;

    /// <summary>FindMission (@0x140c9d720): the first mission of an id.</summary>
    public DungeonMissionCli FindMission(uint id)
    {
        foreach (var m in missions) if (m.Id == id) return m;
        return null;
    }

    /// <summary>OnPacket (@0x140c9d0c0).</summary>
    public void OnPacket(ByteReader r)
    {
        switch (r.ReadByte())
        {
            case 0: OnInitMissions(r); break;
            case 1: OnUpdateMissions(r); break;
            case 2: OnSyncMissions(r); break;
            case 3: Stage = r.ReadByte(); break;     // OnStage (@0x140c9d5d0)
            case 4: Flag = r.ReadBool(); break;      // OnFlag (@0x140c9d620)
            case 5: GaveUp?.Invoke(); break;
        }
    }

    /// <summary>OnInitMissions (@0x140c9d1b0): the old missions go first; a mission the client cannot create ends the list there (the bytes after it stay unread).</summary>
    void OnInitMissions(ByteReader r)
    {
        missions.Clear();
        uint count = unchecked((uint)r.ReadInt());
        for (uint i = 0; i < count; i++)
        {
            var m = DungeonMissionCli.Create(table, unchecked((uint)r.ReadInt()));
            if (m == null) break;
            m.Read(r);
            missions.Add(m);
        }
    }

    /// <summary>OnUpdateMissions (@0x140c9d320): an id the stage does not have stops the packet.</summary>
    void OnUpdateMissions(ByteReader r)
    {
        uint count = unchecked((uint)r.ReadInt());
        for (uint i = 0; i < count; i++)
        {
            var m = FindMission(unchecked((uint)r.ReadInt()));
            if (m == null) return;
            m.Read(r);
        }
    }

    /// <summary>OnSyncMissions (@0x140c9d460): a count or an id that differs from the stage's stops the packet, with the earlier missions updated.</summary>
    void OnSyncMissions(ByteReader r)
    {
        uint count = unchecked((uint)r.ReadInt());
        if (missions.Count != count) return;
        for (int i = 0; i < missions.Count; i++)
        {
            if (missions[i].Id != unchecked((uint)r.ReadInt())) return;
            missions[i].Read(r);
        }
    }
}
