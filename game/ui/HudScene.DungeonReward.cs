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

    /// <summary>The client's dialog object keeps the state a mode 2 packet stored until the next packet or an unload resets it.</summary>
    DungeonRewardDialogController dungeonRewardStored;

    bool DungeonRewardOpen => dungeonReward != null && !dungeonReward.IsClosed;

    /// <summary>OnPacket (@0x140e22130, :386): close an open window, reset, then the mode's state and open.</summary>
    void OnDungeonReward(DungeonRewardPacket packet)
    {
        CloseDungeonReward();
        menuModel ??= NewModel();
        var c = NewDungeonReward();
        dungeonRewardStored = null;
        if (!c.OnPacket(packet)) { if (packet.Mode == 2) dungeonRewardStored = c; return; }   // mode 2 stores only
        OpenDungeonReward(c);
    }

    /// <summary>OnOpenDialog (@0x140b21190): OpenDialog(0x4ef9) with the state the last packet stored (reset after a close).</summary>
    void OnDungeonRewardOpenRequested()
    {
        if (DungeonRewardOpen) return;
        menuModel ??= NewModel();
        var c = dungeonRewardStored ?? NewDungeonReward();
        dungeonRewardStored = null;
        OpenDungeonReward(c);
    }

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
    void OpenDungeonReward(DungeonRewardDialogController c)
    {
        if (!hudModel.HasLocalPlayer) return;
        if (GuideRefusesOpen("DungeonReward")) return;
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

    /// <summary>vfunc_72 (@0x140e21bd0), per frame.</summary>
    void TickDungeonReward()
    {
        if (DungeonRewardOpen) dungeonReward.Tick();
    }

    /// <summary>Scripted runs: the state of the window, for the log.</summary>
    public string DungeonRewardText() => !DungeonRewardOpen ? "closed" : $"open type={dungeonReward.Type} win={dungeonReward.Win} load={dungeonRewardMovie?.Loaded} tip={dungeonReward.Tip.Replace('\n', '|')}";
}
