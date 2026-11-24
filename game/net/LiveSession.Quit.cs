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
    /// MyPC go, and the loading screen starts for map 2, <see cref="World.FieldContents.LeaveForLogin"/>), keeps the address, port and tokens (DAT_143ab2a48/2848/284c/2850), sets DAT_1439f4a78 to 1 (the login server is the
    /// target) and connects there (CSceneStateGame OnUpdate, scenestategame.cpp:1327: a 10 s timer, five ConnectTo calls; after the
    /// fifth timer the 0x20f / 7 box and the process ends, <see cref="GiveUp"/>). The good MoveResult switches to character select at
    /// once (<see cref="ReturnedToCharacterSelect"/>), then ServerEnter and the list follow (<see cref="LoginClient.ReturnWithRetriesAsync"/>).
    /// Each retry after the first connect puts s_reconnect_to_server with the retry count on the loading screen
    /// (scenestategame.cpp:1336-1342; <see cref="RetryLine"/>). MyPC::FUN_1415308d0 (mypc.cpp:4528) resets five debug switches,
    /// none of which this client has: DAT_143ae0fb0 the SetDebugMode key's mode, DAT_143ae0fb8 the world map's show-all key
    /// (uiscenegame.cpp:1215-1226), DAT_143ae0fb4 the god mode the housing and god mode tooltips read, DAT_143ae0fbc the user
    /// command that skips skill cooldowns (usercommandcli.cpp:2110, mypc.cpp:2543), g_nDebugUI the UI debug info
    /// (uiobject.cpp:202); so nothing is reset here.
    /// The field's nodes stay under the loading screen, out of input, until the scene switch frees them; the client's objects go at once.
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
        StopCombatEffects();   // the game scene state goes, and its WorldService's time scale with it; the blink ends on the own PC first
        LeaveCharacter();
        ResetCubeService();   // the game scene's services shut down: CubeMapInformationService::OnShutdown @0x1410cef40 -> FUN_1410d0310 (cubemapinformationservice_3.cpp:1107, 5455-5475)
        PortalAction.Current = null;
        World.FieldContents.LeaveForLogin(GetTree(), LoginProgressMap);
        ResetReconnect();
        var old = game;
        game = null;
        old?.Dispose();
        _ = ReturnAsync(m, exitCts.Token);
    }

    async System.Threading.Tasks.Task ReturnAsync(GameToLogin m, System.Threading.CancellationToken exit)
    {
        try
        {
            // the packet's endpoint, as OnGameToLogin stores it (networkservice.cpp:928-937); the launch host only for 0.0.0.0
            string to = GameClient.HostOf(m.Address, host);
            GD.Print($"live: return connects to {GameClient.EndpointText(to, m.Port)}");
            var (o, next) = await LoginClient.ReturnWithRetriesAsync(to, m.Port, m.Token, accountId, machine,
                onMoveResult: () => Callable.From(() => { if (!exit.IsCancellationRequested) { Session.Characters = new(); Ms2.Ui.FieldMoveCover.Lower(); ReturnedToCharacterSelect?.Invoke(); } }).CallDeferred(),
                ct: exit,
                onAttempt: n =>
                {
                    if (n < 2) return;   // the first connect is vfunc_4's (scenestategame.cpp:2023), which sets no text
                    string line = RetryLine(n - 1);
                    GD.Print($"live: return attempt {n}: {line}");
                    Callable.From(() => { if (!exit.IsCancellationRequested) World.FieldContents.MoveChannelMessage(line); }).CallDeferred();
                });
            if (exit.IsCancellationRequested) { next.Dispose(); return; }
            login?.Dispose();
            UseLogin(next);
            // deferred like the scene switch, so it runs after it: the switch empties the list, this fills it
            Callable.From(() =>
            {
                if (exit.IsCancellationRequested) return;
                Session.Characters = System.Linq.Enumerable.ToList(o.Characters);
                if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
                if (o.CharacterRows > 0) CharacterRows = o.CharacterRows;
                CheckNames = o.CheckNames;
                GD.Print($"live: back at character select, {o.Characters.Count} character(s)");
                CharacterListReturned?.Invoke();
            }).CallDeferred();
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
        catch (System.Exception e) { GD.PushError($"live: return to character select failed: {e.Message}"); }
    }

    /// <summary>
    /// What goes with the character at the return to character select, so the next character entered starts from nothing of
    /// this one's. FUN_141bbc580(0, 2) drops every field object and the own MyPC (SetMyPC(0), worldservice_1.cpp:10098; ~MyPC,
    /// mypc.cpp:897-904): <see cref="DropOwnPc"/>, and a new <see cref="Me"/> (the own PC's user block, look, stats, titles and
    /// revival state; the next one comes from the next entry's own FieldAddUser) with its skill system's cooldowns
    /// (CSkillSystem is the MyPC's, MyPC::GetSkillSystem); the region skill objects (:10029-10056, 10073), the field properties
    /// (ClearFieldProperties, :10104) and the current map (m_284 / m_290, :10123-10126), which is <see cref="entry"/>: with
    /// none, the character select's fields build no game layers (<see cref="OnFieldBuilt"/>). The game UI scene's leave
    /// (UISceneGame::vfunc_49, uiscenegame.cpp:209-216) clears the party and its listing (CPartyManager::ClearPartyAndRecruitment,
    /// partymanager.cpp:425-441, with the battle records' party groups), the guild (CGuildManager::FUN_1413b5f90,
    /// guildmanager.cpp:236-255) and the group chats (CGroupChatManager::FUN_14137e190, groupchatmanager.cpp:388); its personal
    /// rankings (CGameRankingCli::ClearPersonal) and clubs (ClearClubsAndInvites) have no port here. The client keeps the rest
    /// (the item, quest, key table, trophy and other managers): the next entry's load packets replace them.
    /// </summary>
    void LeaveCharacter()
    {
        DropOwnPc();
        Me = new MyCharacter();
        WireMe();
        cooltimes = null;
        regionSkills.Clear(); RegionSkillLog.Clear(); ClearFieldEffects(); FieldProperties.Clear(); HitEffects.Clear();
        entry = null;
        chosen = null;
        Party.ClearPartyAndRecruitment();
        Guilds.LeaveGame();
        GroupChats.Clear();
    }

    /// <summary>The map OnGameToLogin's FUN_141bbc580 starts the loading screen for (networkservice.cpp:935).</summary>
    public const int LoginProgressMap = 2;

    /// <summary>The loading screen's line at retry <paramref name="retry"/> (m_nRetry after its increment, 1 to 5):
    /// FUN_14028dfa0(L"s_reconnect_to_server", m_nRetry), the common string formatted with the count.</summary>
    public static string RetryLine(int retry) =>
        Ms2.Quest.ClientFormat.Format(Ms2.Ui.Hud.MessageBoxData.CommonByKey("s_reconnect_to_server") ?? "", retry);

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
