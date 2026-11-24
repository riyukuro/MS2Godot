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
/// WorldService::SetIdleInterval and SendSyncWorld (worldservice_1.cpp:732-780), BypassKey's value in ResponseLogin, and
/// these packets on the login connection (LoginClient reads none of them).
/// </summary>
public partial class LiveSession
{
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
        if (game.Is(p, Inbound.WorldTimeSync)) { WorldTimeSync.Read(p.Reader()); NotApplied.Print("live: WorldTimeSync not ported (WorldService::SetIdleInterval, worldservice_1.cpp:771)"); return true; }
        // CNetworkService::OnNpsInfo (networkservice.cpp:1075) feeds CNPSMan, the publisher's analytics; CSceneStateMulti::OnServerTimePacket
        // (scenestatemulti.cpp:766) only stores the time for the character select scene. Neither shows anything.
        if (game.Is(p, Inbound.NpsInfo)) { NpsInfo.Read(p.Reader()); return true; }
        if (game.Is(p, Inbound.SyncWorld)) { SyncWorldMessage.Read(p.Reader()); return true; }
        return false;
    }
}
