using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The own player's status events as the party frame sees them (UIStatusEventListener::NotifyStatusChanged,
/// uistatuseventlistener.cpp:257): after the listeners ran, event 0 (Hp) calls CPartyManager::UpdateMemberHp @0x14162a910, event 4
/// (Job) UpdateMemberJob @0x14162a840 and event 6 (Level) UpdateMemberLevel @0x14162aa10. Each sets the own member's value
/// from the player and sends the window its flag (2 / 4 / 8), so the own frame follows the HUD without a server packet;
/// the other members' frames follow the server's mode 0x13 / 0xc packets. No party or no own member: nothing, as in the client.
/// </summary>
public static class PartyOwnStatus
{
    public static void Apply(PartyBook book, long selfCharacterId, PlayerState p, StatusEvent e)
    {
        switch (e)
        {
            case StatusEvent.Hp: book.SetHp(selfCharacterId, (int) p.HpCur, (int) p.HpMax); break;
            case StatusEvent.Job: book.SetJobGroup(selfCharacterId, p.JobGroup); break;
            case StatusEvent.Level: book.SetLevel(selfCharacterId, (short) p.Level); break;
        }
    }
}
