using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The dungeon lobby notice, UIDungeonLobby (0x4f21, movie uidungeonlobby; uidungeonlobby.cpp). The server's DungeonWaiting packet
/// (opcode 0xF0) reaches its OnRoomPacket (@0x140e17930), which stores the room and the lobby's size and opens the dialog
/// (FUN_141924680, uidialog.cpp:445); OnLoadComplete fills the title and sub title from them.
/// STILL-OPEN: what closes the dialog when the player leaves the lobby is not traced (the client keeps it on layer 0x16, m_2c); it is
/// closed at the field swap here. A second packet while it stays open changes the values only, as in the client.
/// </summary>
public partial class HudScene
{
    DungeonLobbyController dungeonLobby;
    MovieDialog dungeonLobbyMovie;

    bool DungeonLobbyOpen => dungeonLobby != null && !dungeonLobby.IsClosed;

    /// <summary>OnRoomPacket: the members, then the open (FUN_141924680) unless the dialog is up already.</summary>
    void OnDungeonWaiting(DungeonWaiting packet)
    {
        if (DungeonLobbyOpen) { dungeonLobby.OnRoomPacket(packet); return; }
        if (!hudModel.HasLocalPlayer) return;                     // FUN_141924680: vfunc_58 and no local player
        CloseDungeonLobby();
        menuModel ??= NewModel();
        var c = new DungeonLobbyController();
        c.OnRoomPacket(packet);
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (dungeonLobbyMovie == m) dungeonLobbyMovie = null;
        };
        dungeonLobby = c; dungeonLobbyMovie = m;
        GD.Print($"dungeon lobby: room {packet.RoomId} load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    void CloseDungeonLobby()
    {
        if (DungeonLobbyOpen) dungeonLobby.Close();
        if (dungeonLobbyMovie != null)
        {
            dungeonLobbyMovie.Host.Unload(); dungeonLobbyMovie.QueueFree(); dungeonLobbyMovie = null;
        }
        dungeonLobby = null;
    }

    /// <summary>Scripted runs: the state of the notice, for the log.</summary>
    public string DungeonLobbyText() => !DungeonLobbyOpen ? "closed" : $"open room={dungeonLobby.RoomId} size={dungeonLobby.Size} load={dungeonLobbyMovie?.Loaded}";
}
