using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The dungeon room records (0x1A, CSceneStateGame::OnRoomDungeon @0x141737360 -> DungeonRecordManagerCli::OnPacket @0x140b20be0,
/// dungeonrecordcli.cpp:203). The record and rank reward tables and the boost state are kept as the client keeps them; the reward
/// notices of kinds 1 to 3 and the three "united reward" errors (0x1F to 0x21) are shown as system notices.
/// <para>STILL-OPEN: the closing photo dialog and the other errors are read and not applied
/// (the errors go through the party manager's keyed notice, FUN_140d73ff0, whose body is not recovered); notice kind 4 formats a
/// string table entry (FUN_141811980) not ported; the game menu's refresh after an added record has no caller.</para>
/// </summary>
public partial class LiveSession
{
    /// <summary>The record manager (TSingleton&lt;DungeonRecordManagerCli&gt;): the tables the packets build and the client's questions over them.
    /// The HUD gives it the room table.</summary>
    public readonly DungeonRecordBook DungeonRecords = new();

    /// <summary>CGameRule's dungeon extra reward counts, which the game event list's DungeonExtraReward events fill and
    /// <see cref="DungeonRecordBook.ExtraRewardEventCount"/> reads.</summary>
    public readonly Ms2.Ui.DungeonExtraRewards DungeonExtraRewards = new();

    void InitDungeonRecords()
    {
        DungeonRecords.ServerTime = () => ServerTime;
        DungeonRecords.CharacterId = () => CharacterId;
        DungeonRecords.ExtraRewardEventCount = DungeonExtraRewards.CountOf;
    }

    /// <summary>DungeonRecordManagerCli's record table: the record stored under a room id (null when no packet named it).</summary>
    public DungeonRoomRecord DungeonRecordOf(uint roomId) => DungeonRecords.Peek(roomId);

    public int DungeonRecordCount => DungeonRecords.RecordCount;
    public int DungeonRankRewardCount => DungeonRecords.RankRewardCount;

    // The HUD's dungeon room dialog (UI 0x4ebd), which the portal action opens and closes (PortalAction, actionkeyobject.cpp:1040-1180).
    // Without a HUD (no scene) they stay null and the portal does nothing.
    public Action<int> OpenDungeonRoomDialog;
    public Action<int, int> OpenDungeonRoomDialogWithMode;
    public Action<int> OpenDungeonRoomDialogRoom;
    public Action CloseDungeonRoomDialog;

    /// <summary>Raised after a record or rank reward table changed (the dungeon room window refreshes its panel).</summary>
    public event Action DungeonRecordsChanged;

    /// <summary>The dungeon room window's OnRoomPacket (@0x1412b8d60) hears the 0x1A packet after the record manager applied it:
    /// (command, notice kind). The command is the client's number of the kind (AddRecord 6, RewardNotice 7, AddReward 20).</summary>
    public event Action<int, int> DungeonRoomPacket;

    /// <summary>OnDungeonMatchError (@0x140e146f0): the match error of the server's DungeonMatch packet.</summary>
    public event Action<Ms2.Net.Packets.DungeonMatchError> DungeonMatchErrorReceived;

    /// <summary>UIDungeonLobby::OnRoomPacket (@0x140e17930): the server's DungeonWaiting packet, the lobby's room and size.</summary>
    public event Action<Ms2.Net.Packets.DungeonWaiting> DungeonWaitingReceived;

    /// <summary>FieldGameScoreBoardCli::FUN_140f9c300 (@0x140f9c300, uishadowexpeditionbossbar.cpp): the server's ShadowExpedition packet, the boss bar's open, update and close.</summary>
    public event Action<Ms2.Net.Packets.FieldGameScoreBoardPacket> ShadowExpeditionReceived;

    /// <summary>UIDungeonRewardDialog::OnPacket (@0x140e22130): the server's DungeonReward packet.</summary>
    public event Action<Ms2.Net.Packets.DungeonRewardPacket> DungeonRewardReceived;

    /// <summary>DungeonRecordManagerCli::OnOpenDialog (@0x140b21190, dungeonrecordcli.cpp:306): OpenDialog(0x4ef9) with no body.</summary>
    public event Action DungeonRewardOpenRequested;

    void OnRoomDungeon(RoomDungeonReply m)
    {
        ApplyRoomDungeon(m);
        int cmd = m.ClientCommand(game.Profile);
        if (cmd != 0) DungeonRoomPacket?.Invoke(cmd, m.NoticeKind);
    }

    void ApplyRoomDungeon(RoomDungeonReply m)
    {
        switch (m.Kind)
        {
            case RoomDungeonKind.Records:
            case RoomDungeonKind.AddRecord:
            case RoomDungeonKind.Rewards:
            case RoomDungeonKind.AddReward:
            case RoomDungeonKind.BoostState:
                if (DungeonRecords.Apply(m)) DungeonRecordsChanged?.Invoke();
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
            case RoomDungeonKind.OpenDialog:
                DungeonRewardOpenRequested?.Invoke();
                break;
            case RoomDungeonKind.ClosingPhoto:
                ReadNotApplied("RoomDungeon", m.Mode);
                break;
            default:
                ReadNotApplied("RoomDungeonUnknown", m.Mode);
                break;
        }
    }
}
