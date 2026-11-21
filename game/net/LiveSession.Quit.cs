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
    /// <summary>A GameToLogin is in progress: the game connection is gone and the login server has not listed the characters yet.</summary>
    public bool Returning { get; private set; }

    /// <summary>
    /// CNetworkService::OnGameToLogin @0x14153bc00 (networkservice.cpp:928). An error shows GetFieldMoveErrorMessage's box and
    /// changes nothing. On 0: WorldService FUN_141bbc580(0, 2) tears the field down (every object and the own MyPC go), the
    /// address, port and tokens are kept (DAT_143ab2a48/2848/284c/2850), DAT_1439f4a78 becomes 1 (the login server is the
    /// target) and the client connects there (CSceneStateGame vfunc_4 -> ConnectTo, 10 s timer). The old connection goes; the
    /// key check, the ServerEnter and the character list follow (<see cref="LoginClient.ReturnAsync"/>).
    /// STILL-OPEN: the client's reconnect timer and retry count for this connect (CSceneStateGame m_nRetry) are not ported: one
    /// failed connect stays on the field, offline; the MyPC::FUN_1415308d0 debug switch reset is not ported (none is).
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
        Returning = true;
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
            Returning = false;
            ReturnedToCharacterSelect?.Invoke();
        }
        catch (MoveRefusedException e)
        {
            next.Dispose();
            Returning = false;
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(e.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
        }
        catch (System.Exception e)
        {
            next.Dispose();
            Returning = false;
            if (!exit.IsCancellationRequested) GD.PushWarning($"live: return to character select failed: {e.Message}");
        }
    }
}
