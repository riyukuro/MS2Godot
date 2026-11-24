using Godot;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The move to another game server (a dungeon room, a channel change): GameToGame 0x10, then the next server's key check and
/// field entry. The client's path: CNetworkService::OnGameToGame stores the address and tokens, tears the field down and goes
/// to CSceneStateMove; its OnUpdate connects to the stored address; OnRequestKey answers with the tokens (ResponseKey);
/// OnMoveResult 0 goes back to the game state (DAT_1439f4a78 is 2: state 0x620000c, no SendServerEnter); the next server's
/// RequestFieldEnter loads the field there.
/// </summary>
public partial class LiveSession
{
    /// <summary>A GameToGame is in progress: the old connection is gone and the next server's entry has not come yet.</summary>
    public bool Migrating { get; private set; }
    /// <summary>The GameToGame moves the next server took (a scripted run counts them).</summary>
    public int Migrations { get; private set; }

    /// <summary>CSceneStateMove's reconnect timer (m_timer.Start(10000), GameClient.MoveConnectWaitMs) and its retry limit (m_nRetry &lt; 5).</summary>
    const int ReconnectWaitMs = GameClient.MoveConnectWaitMs, ReconnectRetries = 5;

    /// <summary>Cancelled when the session leaves the tree (_ExitTree): a move still connecting stops and leaves nothing behind.</summary>
    readonly System.Threading.CancellationTokenSource exitCts = new();

    /// <summary>
    /// CNetworkService::OnGameToGame @0x14153be30 (networkservice.cpp:942). An error shows GetFieldMoveErrorMessage's box and
    /// changes nothing. On 0: the address, port, tokens, field (DAT_143ab2890) and kind (DAT_143ab284a) are kept;
    /// WorldService FUN_141bbc580(0, field) tears the field down (every object and the own MyPC go, the loading screen starts
    /// for <c>field</c>); CStateService FUN_1417e25a0(0x620003b) goes to CSceneStateMove; MyPC::FUN_1415308d0 resets the
    /// debug switches (none is ported). Here the teardown is the field move FieldContents.ServerMove starts (its loading
    /// screen), held until the next server's entry came; the old connection is let go (the server closes it after the
    /// packet, and the client's ConnectTo replaces its socket).
    /// </summary>
    void OnGameToGame(GameToGame m)
    {
        if (!m.Ok)
        {
            GD.Print($"live: GameToGame error {m.Error}");
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(m.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
            return;
        }
        GD.Print($"live: GameToGame to {m.Address}:{m.Port} field {m.FieldId} kind {m.Kind}");
        // FUN_141bbc580: SetMyPC(0), the field's objects go; no input reaches the old field from now
        Ms2.Ui.FieldMoveCover.Raise();
        fieldEntry.TornDown();   // m_state = 6: the next server's entry is a fresh load, the cube map values go
        DropOwnPc();
        PortalAction.Current = null;
        moveClock.Restart();
        ResetReconnect();
        var old = game;
        game = null;
        old?.Dispose();
        Migrating = true;
        Ms2Godot.World.FieldContents.ServerMove(GetTree(), m.FieldId, () => !Migrating);
        _ = MigrateAsync(m, exitCts.Token);
    }

    /// <summary>
    /// CSceneStateMove::OnUpdate @0x14174cb70 (scenestatemove.cpp:79): ConnectTo the stored address, a 10 s timer; when it runs
    /// out unconnected, m_nRetry goes up and the next update connects again (the connect at the top runs whatever m_nRetry
    /// is, so the fifth failure still makes a sixth attempt, given up when its timer runs out). Each connect here is bounded by
    /// that 10 s (a connect not made by then is a failed attempt). The address is the packet's (DAT_143ab2a48,
    /// networkservice.cpp:951), except that the command line's --ip host, when set, replaces it (<c>host</c>: a test setup whose
    /// servers announce an address this machine cannot reach). Connected, the key check and the entry run as at login (GameClient.EnterAsync:
    /// OnRequestKey's ResponseKey with the GameToGame tokens, OnMoveResult, then the packets up to RequestFieldEnter).
    /// A MoveResult error shows GetFieldMoveErrorMessage's box (OnMoveResult, networkservice.cpp:905) and the state stays. The
    /// loading screen stays up over it, as in the client: CSceneStateMove takes nothing down (scenestatemove.cpp), and the
    /// loading screen's movie is in a higher depth layer than the box's (UILoadingProgress m_2c 0x29, CommonDepth.ClearScreen,
    /// uiloadingprogress.cpp:63; UIMessageBox m_2c 25, CommonDepth.Dialog, uimessagebox.cpp:374). STILL-OPEN: whether the
    /// client's loading movie takes the mouse over the box (ours does not: the box still takes clicks and keys).
    /// STILL-OPEN: the first attempt's chat line (Unk_SSGFormatMsg 0x2dd with the address and port) and the retry text on the
    /// loading screen (UILoadingProgress::SetMoveChannelMessage, "s_reconnect_to_server" with the attempt number); after the
    /// sixth attempt, RestoreServerAddress (kind 0x11 only: once more to the saved address, "s_reconnect_to_server_fail") and
    /// else the 0x20f/7 message box and TerminateProcess (<see cref="GiveUp"/>). The restore of kind 0x11 is not done here.
    /// </summary>
    async Task MigrateAsync(GameToGame m, System.Threading.CancellationToken exit)
    {
        for (int attempt = 0; ; attempt++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                GD.Print($"live: GameToGame connects to {GameClient.EndpointText(GameClient.HostOf(m.Address, host), m.Port)} (attempt {attempt + 1})");
                var (e, t) = await GameClient.EnterAsync(m, accountId, machine, host, ct: exit, requestGlobalFactor: !Factors.Applied);
                // the session left the tree while the entry ran: nothing is taken, the connection goes
                if (exit.IsCancellationRequested) { t.Dispose(); return; }
                (entry, game) = (e, t);
                AttachGameReconnect(game);
                Migrations++;
                GD.Print($"live: GameToGame entry after {moveClock.ElapsedMilliseconds} ms ({attempt + 1} attempt(s))");
                TakeEntry();
                // the loading screen was for the GameToGame's field (DAT_143ab2890); the field built is the entry's
                if (entry.Field.MapId != m.FieldId) Ms2Godot.World.FieldContents.ServerMove(GetTree(), entry.Field.MapId);
                FieldMoveStarted?.Invoke(entry.Field);
                Migrating = false;
                return;
            }
            catch (MoveRefusedException e)
            {
                GD.Print($"live: GameToGame key refused, move error {e.Error}");
                Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.FieldMoveError.Text(e.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey), 0, null, "");
                return;
            }
            catch (System.Net.Sockets.SocketException e) when (attempt < ReconnectRetries)
            {
                GD.PushWarning($"live: GameToGame connect attempt {attempt + 1} failed: {e.Message}");
                int left = ReconnectWaitMs - (int)clock.ElapsedMilliseconds;
                try { if (left > 0) await Task.Delay(left, exit); }
                catch (OperationCanceledException) { return; }
            }
            catch (OperationCanceledException) when (exit.IsCancellationRequested)
            {
                return;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                // the run out of attempts: the 0x20f / 7 box, then the process ends (scenestategame.cpp:1348-1357 and its CSceneStateMove twin)
                GD.PushWarning($"live: GameToGame connect attempt {attempt + 1} failed: {e.Message}");
                GiveUp();
                return;
            }
            catch (Exception e)
            {
                GD.PushWarning($"live: GameToGame failed: {e.Message}; STILL-OPEN: the client's give-up path is not ported");
                return;
            }
        }
    }
}
