using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The dungeon room records (0x1A, CSceneStateGame::OnRoomDungeon @0x141737360 -> DungeonRecordManagerCli::OnPacket @0x140b20be0,
/// dungeonrecordcli.cpp:203). The record and rank reward tables and the boost state are kept as the client keeps them; the reward
/// notices of kinds 1 to 3 and the three "united reward" errors (0x1F to 0x21) are shown as system notices.
/// <para>STILL-OPEN: the closing photo dialog, the dialog OnOpenDialog opens (0x4ef9) and the other errors are read and not applied
/// (the errors go through the party manager's keyed notice, FUN_140d73ff0, whose body is not recovered); notice kind 4 formats a
/// string table entry (FUN_141811980) not ported; the game menu's refresh after an added record has no caller.</para>
/// </summary>
public partial class LiveSession
{
    readonly Dictionary<uint, DungeonRoomRecord> dungeonRoomRecords = new();
    readonly Dictionary<uint, DungeonRankRewardEntry> dungeonRankRewards = new();
    long dungeonBoostA, dungeonBoostB;

    /// <summary>DungeonRecordManagerCli's record table: the record stored under a room id (null when no packet named it).</summary>
    public DungeonRoomRecord DungeonRecordOf(uint roomId) => dungeonRoomRecords.TryGetValue(roomId, out var r) ? r : null;

    public int DungeonRecordCount => dungeonRoomRecords.Count;
    public int DungeonRankRewardCount => dungeonRankRewards.Count;

    void OnRoomDungeon(RoomDungeonReply m)
    {
        switch (m.Kind)
        {
            case RoomDungeonKind.Records:
                dungeonRoomRecords.Clear();   // OnRecords: RemoveAll, then the map
                foreach (var rec in m.Records) dungeonRoomRecords[rec.Key] = rec;
                break;
            case RoomDungeonKind.AddRecord:
                dungeonRoomRecords[m.Record.RoomId] = m.Record;   // SetAt(record's own room id)
                break;
            case RoomDungeonKind.Rewards:
                dungeonRankRewards.Clear();
                foreach (var rw in m.Rewards) dungeonRankRewards[rw.Key] = rw;
                break;
            case RoomDungeonKind.AddReward:
                dungeonRankRewards[m.Reward.Id] = m.Reward;   // SetAt(reward's own id)
                break;
            case RoomDungeonKind.BoostState:
                dungeonBoostA = m.BoostA;
                dungeonBoostB = m.BoostB;
                break;
            case RoomDungeonKind.RewardNotice:
            {
                string key = m.NoticeKind switch
                {
                    1 => "s_room_dungeon_give_reward",
                    2 => "s_room_dungeon_give_dungeonHelperReward",
                    3 => "s_room_dungeon_reward_addExtraCount",
                    _ => null,
                };
                if (key != null) SystemNoticeSink?.Invoke(MessageBoxData.CommonByKey(key) ?? "");
                else ReadNotApplied("RoomDungeon", 7);
                break;
            }
            case RoomDungeonKind.Error:
            {
                string key = m.Error switch
                {
                    0x1F => "s_room_dungeon_error_used_reset_united_reward",
                    0x20 => "s_room_dungeon_error_still_have_united_reawrd",
                    0x21 => "s_room_dungeon_error_shutdown_united_reawrd_reset",
                    _ => null,
                };
                if (key != null) SystemNoticeSink?.Invoke(MessageBoxData.CommonByKey(key) ?? "");
                else ReadNotApplied("RoomDungeonError", m.Error);
                break;
            }
            case RoomDungeonKind.ClosingPhoto:
            case RoomDungeonKind.OpenDialog:
                ReadNotApplied("RoomDungeon", m.Mode);
                break;
            default:
                ReadNotApplied("RoomDungeonUnknown", m.Mode);
                break;
        }
    }
}
