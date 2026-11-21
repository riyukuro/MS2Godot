using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Inbound 0x33 (CSceneStateGame::OnRevivalConfirm, scenestategame.cpp:7356): i32 object id, i32 end tick
/// (PC::FUN_1416619d0, m_n10e8), i32 count (PC::FUN_141661a60, m_n10ec). The client ignores trailing bytes.</summary>
public sealed record RevivalConfirm(int ObjectId, uint EndTick, int Count) {
    public static RevivalConfirm Read(ByteReader r) => new(r.ReadInt(), (uint)r.ReadInt(), r.ReadInt());
}

/// <summary>Inbound 0x35 (CSceneStateGame::OnRevivalCount, scenestategame.cpp:4092): i32 used meso revivals of the local player
/// (MyPC::FUN_14152a2f0, m_n1644).</summary>
public sealed record RevivalCount(int Used) {
    public static RevivalCount Read(ByteReader r) => new(r.ReadInt());
}

/// <summary>Inbound 0xA2 (CSceneStateGame::OnAutoRevive, scenestategame.cpp:4332): i32 milliseconds until the automatic revive;
/// opens UIAutoReviveCountDownDialog (0x4ea2) and arms it (FUN_141284d70).</summary>
public sealed record AutoRevive(int DurationMs) {
    public static AutoRevive Read(ByteReader r) => new(r.ReadInt());
}

/// <summary>Inbound 0xFB (CSceneStateGame::OnDungeonReviveCount, scenestategame.cpp:5074): i32 revivals used in the dungeon room
/// (DAT_143ab2950).</summary>
public sealed record DungeonReviveCount(int Used) {
    public static DungeonReviveCount Read(ByteReader r) => new(r.ReadInt());
}
