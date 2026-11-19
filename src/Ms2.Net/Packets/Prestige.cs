using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Prestige (0x11C, the adventure level; CSceneStateGame::OnPrestige, scenestategame.cpp:8815). The payload reader
/// FUN_1407a8b20 (unit_1407a4f20_packetserializers.cpp:922) reads a u8 kind and then: 1 the exp in this level and the gain
/// (i64, i64); 2 a player's object id and new adventure level (i32, i32); 0 the exp, the filled level, the next level's
/// exp and the claimed level rewards (i64, i32, i64, i32 n × i32); 4 a claim's result and the levels it claimed (u8, i32
/// n × i32). Kinds 6 and 7 are re-read from the kind by CSAdventureLevelMission::FUN_1407a90e0 (:986): 6 a u8 result and
/// the missions, 7 the missions, each i32 n × (i64 id, i64 progress, u8 complete). Other kinds are read as null.
/// </summary>
public abstract record PrestigeMessage {
    public sealed record ExpGain(long Value, long Gain) : PrestigeMessage;
    public sealed record LevelUp(int ObjectId, int Value) : PrestigeMessage;
    public sealed record Loaded(long Exp, int FilledLevel, long NextExp, IReadOnlyCollection<uint> Claimed) : PrestigeMessage;
    public sealed record ClaimResult(byte Result, IReadOnlyCollection<uint> Levels) : PrestigeMessage;
    /// <summary>Kind 6 (with a result) or 7 (Result 1). The ids are the i64s cut to u32 and, by std::map::insert, the first of
    /// an id kept.</summary>
    public sealed record MissionList(byte Kind, byte Result, IReadOnlyDictionary<uint, (uint Progress, bool Complete)> List) : PrestigeMessage;

    public static PrestigeMessage Read(ByteReader r) {
        byte kind = r.ReadByte();
        switch (kind) {
            case 1: { long exp = r.ReadLong(); return new ExpGain(exp, r.ReadLong()); }
            case 2: { int id = r.ReadInt(); return new LevelUp(id, r.ReadInt()); }
            case 0: {
                long exp = r.ReadLong(); int filled = r.ReadInt(); long next = r.ReadLong();
                return new Loaded(exp, filled, next, ReadSet(r));
            }
            case 4: { byte ok = r.ReadByte(); return new ClaimResult(ok, ReadSet(r)); }
            case 6: { byte ok = r.ReadByte(); return new MissionList(6, ok, ReadMissions(r)); }
            case 7: return new MissionList(7, 1, ReadMissions(r));
        }
        return null;
    }

    /// <summary>i32 n, then n × i32 into a std::set&lt;unsigned int&gt; (m_20).</summary>
    static SortedSet<uint> ReadSet(ByteReader r) {
        var set = new SortedSet<uint>();
        int n = r.ReadInt();
        for (int i = 0; i < n; i++) set.Add((uint) r.ReadInt());
        return set;
    }

    static SortedDictionary<uint, (uint, bool)> ReadMissions(ByteReader r) {
        var map = new SortedDictionary<uint, (uint, bool)>();
        uint n = (uint) r.ReadInt();
        for (uint i = 0; i < n; i++) {
            uint id = (uint) r.ReadLong();
            uint progress = (uint) r.ReadLong();
            bool complete = r.ReadByte() != 0;
            map.TryAdd(id, (progress, complete));
        }
        return map;
    }
}

/// <summary>Outbound 0xAC mode 3 (UIAdventureLevelDialog::ClaimLevelReward @0x140ba4960, uiadventureleveldialog.cpp:525; the
/// writer FUN_140202290): u8 3, i32 the level. Claims an item reward of the adventure level guide.</summary>
public sealed record PrestigeLevelRewardRequest(int Level) : IOutPacket {
    public Outbound Op => Outbound.Prestige;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteInt(Level); }
}

/// <summary>Outbound 0xAC mode 5 (UIAdventureLevelDialog::ClaimMissionReward @0x140ba4a10, :543; the writer
/// FUN_140202380): u8 5, i32 the mission id. Claims a completed mission's reward.</summary>
public sealed record PrestigeMissionRewardRequest(int MissionId) : IOutPacket {
    public Outbound Op => Outbound.Prestige;
    public void Write(ByteWriter w) { w.WriteByte(5); w.WriteInt(MissionId); }
}
