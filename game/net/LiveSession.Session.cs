using Godot;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The rest of the client's session packets on the game connection: each is read whole (its layout checked) and does what
/// the client's handler does where that is ported; the others print one not-ported line.
/// STILL-OPEN: VersionResult's box 0x214 tagged "forcelogin" and its button (uimessagebox.cpp:1206), DataPatch
/// (COnlinePatchManagerCli, onlinepatchmanagercli.cpp:110), RelocateWorld's move (CSceneStateRelocateWorld and the RequestKey
/// answer RelocateWorld 5, networkservice.cpp:861-868), the waiting ticket box (scenestatemulti.cpp:733-763),
/// BypassKey's value in ResponseLogin, and
/// these packets on the login connection (LoginClient reads none of them). The idle timer (<see cref="IdleTimer"/>) is ported
/// with its four callers; STILL-OPEN there: the Attendance packet WorldService::OnGameStart sends before ResetIdleTimer
/// (worldservice_1.cpp:5776-5779), the start's skip while the sub scene is 0x620001b (:823), and the other Unk_PCMove::IsActionKeyDown users
/// (the get key 0x493e3 mypc.cpp:4740, the ride attack 0x49401 ridee.cpp:719, the camera wheel 300008/300009 rpgcameramode.cpp:145-147).
/// </summary>
public partial class LiveSession
{
    WorldIdleTimer idleTimer;

    /// <summary>WorldService's idle timer (m_a70/m_a74/m_a78): the clock is GetTickCountMs2, the send is SendSyncWorld.</summary>
    internal WorldIdleTimer IdleTimer => idleTimer ??= new WorldIdleTimer(WorldIdleTimer.SystemTick, idle =>
    {
        if (LiveCheck) GD.Print($"live: SyncWorld sent, idle {idle}");
        Send(new SyncWorldRequest(idle));
    });

    /// <summary>WorldService::FUN_141bca1e0 (@0x141bca1e0, worldservice_1.cpp:794-800), from UIServerListDialog::EnterGame
    /// (uiserverlistdialog.cpp:634): the next world update with the own pc in runs OnGameStart.</summary>
    bool gameStartPending;

    /// <summary>WorldService::FUN_141bca240 (@0x141bca240, worldservice_1.cpp:817-834) up to its OnGameStart branch: with the own pc
    /// in and a start pending, OnGameStart (@0x141bcf9f0, :5774-5782) resets the idle timer, once per EnterGame.</summary>
    internal void TickGameStart()
    {
        if (!gameStartPending || player == null || !IsInstanceValid(player) || !player.Live) return;
        gameStartPending = false;
        IdleTimer.ResetIdleTimer();
    }

    bool HandleSessionPacket(Ms2.Net.Wire.RawPacket p)
    {
        if (game.Is(p, Inbound.AaErr))
        {
            AaErr.Read(p.Reader());
            // CNetworkService::OnAaErr @0x14153e150 (networkservice.cpp:1080): FUN_141275260(0xc07, 1, 1), a common system notice
            string text = Ms2.Ui.Hud.MessageBoxData.Common(AaErr.NoticeStringId);
            if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
            if (LiveCheck) GD.Print("live: AaErr notice 0xc07");
            return true;
        }
        if (game.Is(p, Inbound.VersionResult)) { VersionResult.Read(p.Reader()); NotApplied.Print("live: VersionResult not ported (box 0x214 forcelogin, networkservice.cpp:854)"); return true; }
        if (game.Is(p, Inbound.DataPatch)) { NotApplied.Print("live: DataPatch not ported (onlinepatchmanagercli.cpp:110)"); return true; }
        if (game.Is(p, Inbound.BypassKey)) { BypassKey.Read(p.Reader()); NotApplied.Print("live: BypassKey not ported (networkservice.cpp:891)"); return true; }
        if (game.Is(p, Inbound.RelocateWorld)) { RelocateWorldMessage.Read(p.Reader()); NotApplied.Print("live: RelocateWorld not ported (networkservice.cpp:957)"); return true; }
        if (game.Is(p, Inbound.WaitingTicketUpdate)) { WaitingTicketUpdate.Read(p.Reader()); NotApplied.Print("live: WaitingTicketUpdate not ported (scenestatemulti.cpp:733)"); return true; }
        if (game.Is(p, Inbound.WorldTimeSync))
        {
            // CNetworkService::OnWorldTimeSync @0x14153a8a0 (networkservice.cpp:806-809): WorldService::SetIdleInterval(i32)
            var sync = WorldTimeSync.Read(p.Reader());
            IdleTimer.SetIdleInterval(sync.IdleIntervalMs);
            if (LiveCheck) GD.Print($"live: WorldTimeSync idle interval {sync.IdleIntervalMs} ms");
            return true;
        }
        // CNetworkService::OnNpsInfo (networkservice.cpp:1075) feeds CNPSMan, the publisher's analytics; CSceneStateMulti::OnServerTimePacket
        // (scenestatemulti.cpp:766) only stores the time for the character select scene. Neither shows anything.
        if (game.Is(p, Inbound.NpsInfo)) { NpsInfo.Read(p.Reader()); return true; }
        if (game.Is(p, Inbound.SyncWorld)) { SyncWorldMessage.Read(p.Reader()); return true; }
        return false;
    }
}
