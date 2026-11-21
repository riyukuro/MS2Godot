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
    /// <summary>OnMoveResult 0 for the login target (networkservice.cpp:899-901): the client switches the scene to character select (FUN_1417e25a0(0x620000d))
    /// at once, before ServerEnter and the list. The list is empty until <see cref="CharacterListReturned"/>.</summary>
    public event Action ReturnedToCharacterSelect;

    /// <summary>The login server's character list arrived after the switch (the client's RefreshLobby on the list messages).</summary>
    public event Action CharacterListReturned;

    /// <summary>
    /// CNetworkService::OnGameToLogin @0x14153bc00 (networkservice.cpp:928). An error shows GetFieldMoveErrorMessage's box and
    /// changes nothing. On 0 the client tears the field down at once (WorldService FUN_141bbc580(0, 2): every object and the own
    /// MyPC go), keeps the address, port and tokens (DAT_143ab2a48/2848/284c/2850), sets DAT_1439f4a78 to 1 (the login server is the
    /// target) and connects there (CSceneStateGame OnUpdate, scenestategame.cpp:1327: a 10 s timer, five ConnectTo calls; after the
    /// fifth timer the 0x20f / 7 box and the process ends, <see cref="GiveUp"/>). The good MoveResult switches to character select at
    /// once (<see cref="ReturnedToCharacterSelect"/>), then ServerEnter and the list follow (<see cref="LoginClient.ReturnWithRetriesAsync"/>).
    /// STILL-OPEN: (1) the field teardown (FUN_141bbc580(0, 2)): the field scene stays up, offline, until the MoveResult switches the scene.
    /// (2) The retry text on the loading screen (s_reconnect_to_server with the attempt number, SetMoveChannelMessage). (3) The
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
        try
        {
            var (o, next) = await LoginClient.ReturnWithRetriesAsync(host ?? m.Address.ToString(), m.Port, m.Token, accountId, machine,
                onMoveResult: () => Callable.From(() => { if (!exit.IsCancellationRequested) { Session.Characters = new(); ReturnedToCharacterSelect?.Invoke(); } }).CallDeferred(),
                ct: exit);
            if (exit.IsCancellationRequested) { next.Dispose(); return; }
            login?.Dispose();
            login = next;
            Session.Characters = System.Linq.Enumerable.ToList(o.Characters);
            if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
            GD.Print($"live: back at character select, {o.Characters.Count} character(s)");
            CharacterListReturned?.Invoke();
        }
        catch (MoveRefusedException e)
        {
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(e.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
        }
        catch (ReconnectGaveUpException)
        {
            if (!exit.IsCancellationRequested) GiveUp();
        }
        catch (System.OperationCanceledException) when (exit.IsCancellationRequested) { }
    }

    /// <summary>
    /// The client's give-up after the last reconnect timer (CSceneStateGame::OnUpdate, scenestategame.cpp:1348-1357): BlockFormat(0x20f, 7)
    /// in a box titled MS2, then TerminateProcess(GetCurrentProcess(), 2). The box here is the in-game one; its button ends the process with code 2.
    /// </summary>
    void GiveUp()
    {
        GD.PushWarning("live: no connection after the last attempt: the client ends here");
        string text = Ms2.Quest.ClientFormat.Format(Ms2.Ui.Hud.MessageBoxData.Common(0x20f), 7);
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, new GiveUpListener(this), "");
    }

    sealed class GiveUpListener(LiveSession s) : Ms2.Ui.Hud.IMessageBoxListener
    {
        public bool OnMessageBox(Ms2.Ui.Hud.MessageBoxController sender, int button, string tag)
        {
            s.GetTree().Quit(2);
            return true;
        }
    }
}
