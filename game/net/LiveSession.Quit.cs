using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2Godot.Flow;

namespace Ms2Godot.Net;

/// <summary>
/// The way back to character select: the system menu's OnSelectCharacter sends RequestQuit (SendRequestQuit) and the menu stays; the server
/// answers GameToLogin 0x0F, which this part turns into the login server's character list.
/// </summary>
public partial class LiveSession
{
    /// <summary>The login server's character list came back after a GameToLogin: the flow returns to character select.</summary>
    public event Action ReturnedToCharacterSelect;

    /// <summary>
    /// CNetworkService::OnGameToLogin @0x14153bc00 (networkservice.cpp:928). An error shows GetFieldMoveErrorMessage's box and
    /// changes nothing. On 0 the client tears the field down at once (WorldService FUN_141bbc580(0, 2): every object and the own
    /// MyPC go), keeps the address, port and tokens (DAT_143ab2a48/2848/284c/2850), sets DAT_1439f4a78 to 1 (the login server is the
    /// target) and connects there (CSceneStateGame vfunc_4 -> ConnectTo, 10 s timer). Here the own player stops being live and the
    /// game connection goes; the key check, the ServerEnter and the character list follow (<see cref="LoginClient.ReturnAsync"/>).
    /// STILL-OPEN: (1) the client switches to character select (FUN_1417e25a0(0x620000d)) on MoveResult 0 (networkservice.cpp:901),
    /// before ServerEnter and the list; here the flow moves when the list has arrived (up to the 15 s deadline on the field). (2) The
    /// field teardown (FUN_141bbc580(0, 2)): the field scene stays up, offline, until the flow moves; after a refused key or a failed
    /// connect it stays. (3) The reconnect timer and retry count (CSceneStateGame m_nRetry, 5 tries) and the 0x20f / 7 block text and
    /// process end after the last one: a failed connect shows a move error box once and stays offline. (4) The
    /// MyPC::FUN_1415308d0 debug switch reset (none is ported).
    /// </summary>
    void OnGameToLogin(GameToLogin m)
    {
        if (!m.Ok)
        {
            GD.Print($"live: GameToLogin error {m.Error}");
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(m.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
            return;
        }
        GD.Print($"live: GameToLogin to {m.Address}:{m.Port}");
        if (player != null && IsInstanceValid(player)) player.Live = false;
        player = null;
        selfObject = -1;
        bindPending = false;
        PortalAction.Current = null;
        var old = game;
        game = null;
        old?.Dispose();
        _ = ReturnAsync(m, exitCts.Token);
    }

    async System.Threading.Tasks.Task ReturnAsync(GameToLogin m, System.Threading.CancellationToken exit)
    {
        var next = new TcpTransport();
        try
        {
            var o = await new LoginClient(next).ReturnAsync(host ?? m.Address.ToString(), m.Port, m.Token, accountId, machine, exit);
            if (exit.IsCancellationRequested) { next.Dispose(); return; }
            login?.Dispose();
            login = next;
            Session.Characters = System.Linq.Enumerable.ToList(o.Characters);
            if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
            GD.Print($"live: back at character select, {o.Characters.Count} character(s)");
            ReturnedToCharacterSelect?.Invoke();
        }
        catch (MoveRefusedException e)
        {
            next.Dispose();
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(e.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
        }
        catch (System.Exception e)
        {
            next.Dispose();
            if (exit.IsCancellationRequested) return;
            GD.PushWarning($"live: return to character select failed: {e.Message}");
            // the client's failed connect ends in a message box (0x20f / 7, then it ends the process): here the box shows the
            // move error text with the failure's code, and the session stays offline
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(e is System.Net.Sockets.SocketException se ? se.ErrorCode : 7, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
        }
    }
}
