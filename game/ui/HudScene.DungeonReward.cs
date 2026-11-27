using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The dungeon result window, UIDungeonRewardDialog (0x4ef9, movie uidungeonrewarddialog; uidungeonrewarddialog.cpp). The server's
/// DungeonReward packet (opcode 0x1B) reaches OnPacket (@0x140e22130): an open window is closed and reset, the mode's state is stored and
/// modes 0, 1 and 3 open the window (FUN_141924680); mode 2 only stores. DungeonRecordManagerCli::OnOpenDialog (dungeonrecordcli.cpp:306)
/// opens it without a packet's state. The per-frame work (vfunc_72) is <see cref="DungeonRewardDialogController.Tick"/>.
/// </summary>
public partial class HudScene
{
    DungeonRewardDialogController dungeonReward;
    MovieDialog dungeonRewardMovie;

    /// <summary>The dialog object's state and what a packet and OnOpenDialog do with it (<see cref="DungeonRewardWindow"/>).</summary>
    DungeonRewardWindow dungeonRewardWindow;

    bool DungeonRewardOpen => dungeonReward != null && !dungeonReward.IsClosed;

    DungeonRewardWindow RewardWindow() => dungeonRewardWindow ??= new DungeonRewardWindow
    {
        Create = () => { menuModel ??= NewModel(); return NewDungeonReward(); },
        Open = OpenDungeonReward,
        IsOpen = () => DungeonRewardOpen,
        CloseWindow = CloseDungeonReward,
    };

    /// <summary>OnPacket (@0x140e22130, :386): close an open window, reset, then the mode's state and open.</summary>
    void OnDungeonReward(DungeonRewardPacket packet) => RewardWindow().OnPacket(packet);

    /// <summary>OnOpenDialog (@0x140b21190): OpenDialog(0x4ef9) with the state the last packet stored (reset after a close).</summary>
    void OnDungeonRewardOpenRequested() => RewardWindow().OnOpenRequested();

    DungeonRewardDialogController NewDungeonReward()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        return new DungeonRewardDialogController
        {
            PreloadTables = menuModel.Tables,
            Records = live?.DungeonRecords ?? new DungeonRecordBook(),
            Send = p =>
            {
                if (live != null) live.SendParty(p);
                else GD.Print($"dungeon reward: offline, not sent: {p}");
            },
            HasLocalPlayer = () => hudModel.HasLocalPlayer,
            NowMs = () => (long)Time.GetTicksMsec(),
        };
    }

    /// <summary>FUN_141924680 (uidialog.cpp:445): the window opens unless there is no local player.</summary>
    bool OpenDungeonReward(DungeonRewardDialogController c)
    {
        if (!hudModel.HasLocalPlayer) return false;
        if (GuideRefusesOpen("DungeonReward")) return false;
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (dungeonRewardMovie == m) dungeonRewardMovie = null;
        };
        dungeonReward = c; dungeonRewardMovie = m;
        GD.Print($"dungeon reward: type {c.Type} win={c.Win} load={m.Loaded}");
        uiRoot.AddChild(m);
        return true;
    }

    void CloseDungeonReward()
    {
        if (DungeonRewardOpen) dungeonReward.Close();
        if (dungeonRewardMovie != null)
        {
            dungeonRewardMovie.Host.Unload(); dungeonRewardMovie.QueueFree(); dungeonRewardMovie = null;
        }
        dungeonReward = null;
    }

    /// <summary>
    /// The game scene's leave (<see cref="SceneChange.SceneLeft"/>: a scene state change such as the GameToGame move, and CSceneStateGame::OnLeave):
    /// UISceneGame::vfunc_49 (@0x141ac74f0, uiscenegame.cpp:218) runs FUN_141ada610(0x620001d), which calls CloseClosableDialogs
    /// (@0x141adc050, uiservice.cpp:1496) and closes every open dialog without flag 2: the dungeon lobby (flag 0, uidungeonlobby.cpp:50), the boss
    /// bar (flag 1, uishadowexpeditionbossbar.cpp:244) and the result window (no flag, uidungeonrewarddialog.cpp:293; <see cref="DungeonRewardWindow.OnSceneLeft"/>) and the home menu (flag 0, uihomemenudialog.cpp:225).
    /// A same-server field change (CSceneStateGame::OnRequestFieldEnter, scenestategame.cpp:~2340) does not reach it, so SwapField leaves them up.
    /// </summary>
    void OnSceneLeft()
    {
        CloseDungeonLobby();
        CloseBossBar();
        RewardWindow().OnSceneLeft();
    }

    /// <summary>vfunc_72 (@0x140e21bd0), per frame.</summary>
    void TickDungeonReward()
    {
        if (DungeonRewardOpen) dungeonReward.Tick();
    }

    /// <summary>Scripted runs: the state of the window, for the log.</summary>
    public string DungeonRewardText() => !DungeonRewardOpen ? "closed" : $"open type={dungeonReward.Type} win={dungeonReward.Win} load={dungeonRewardMovie?.Loaded} tip={dungeonReward.Tip.Replace('\n', '|')}";
}
