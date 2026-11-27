using Godot;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The player's dungeon stage (CMyCharacter::m_spStage, a RoomStageDungeonCli). The server's RoomStageDungeon packet creates or clears it
/// (CNetworkService::OnRoomStageDungeon @0x14153e180, networkservice.cpp:1082; CMyCharacter::CreateStage @0x140d9f400, ClearStage @0x140d9f4b0) and
/// its DungeonMission packet (RecvOp 0x100, registered by the stage's ctor and dropped by its dtor) changes the missions of the stage that exists.
/// STILL-OPEN: the stage is not cleared at a scene leave (no client clear was found; only the server's kind 1 clears it).
/// </summary>
public partial class LiveSession
{
    /// <summary>CMyCharacter::GetStage (@0x140d9f4e0): null while no stage exists.</summary>
    public RoomStageDungeonCli Stage { get; private set; }

    /// <summary>Raised after the stage was created, cleared or changed by a mission packet.</summary>
    public event Action StageChanged;

    void OnRoomStageDungeon(RoomStageDungeonPacket m)
    {
        if (m == null) return;   // a kind the client has no case for
        if (!m.Create) { Stage = null; GD.Print("live: dungeon stage cleared"); StageChanged?.Invoke(); return; }
        var t = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
        var stage = new RoomStageDungeonCli(unchecked((uint)m.RoomId), m.Stage, m.Flag, t.DungeonRooms, t.DungeonMissions);
        stage.GaveUp += () => SystemNoticeSink?.Invoke(MessageBoxData.CommonByKey("s_party_notify_dungeon_giveup") ?? "");   // OnGiveUp (@0x140c9d670): FUN_1412751b0 type 0x14
        Stage = stage;
        GD.Print($"live: dungeon stage created room {m.RoomId} stage {m.Stage} flag {m.Flag} known room {stage.Room != null}");
        StageChanged?.Invoke();
    }

    /// <summary>RoomStageDungeonCli::OnPacket (@0x140c9d0c0) for the DungeonMission packet; nothing listens while no stage exists.</summary>
    void OnDungeonMission(ByteReader r)
    {
        if (Stage == null) { GD.Print("live: dungeon mission packet without a stage"); return; }
        Stage.OnPacket(r);
        GD.Print($"live: dungeon mission: {Stage.Missions.Count} mission(s) score {Stage.Score}/{Stage.MaxScore} rank {Stage.Rank} stage {Stage.Stage} flag {Stage.Flag}");
        StageChanged?.Invoke();
    }
}
