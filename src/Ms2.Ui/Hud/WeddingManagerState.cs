using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The part of WeddingManager the item requirements read: m_proxy.m_nState, the wedding progress state (WeddingProxy +0x20,
/// weddingproxy.cpp:230) the Wedding packet's proxy modes set. IsInProgress (@0x141b89990, weddingmanager.cpp:1174) is true for the
/// states 2, 3 and 4; MyPC::vfunc_146 (@0x141523b70, mypc.cpp:1409) and the item checks (inventory.cpp:1599, astooltip.cpp:4391) ask it.
/// The state is the manager's, not the field user's wedding state (FieldAddUser): the latter only tells the other players' clients.
/// STILL-OPEN: the rest of the manager (tickets, invitations, hall list, the dialogs) is not ported.
/// </summary>
public sealed class WeddingManagerState
{
    /// <summary>The Wedding packet is read only with this feature enabled (WeddingManager::OnPacket, weddingmanager.cpp:198).</summary>
    public const int Feature = 0x104;

    /// <summary>m_proxy.m_nState; 0 from WeddingProxy's constructor (weddingproxy.cpp:186).</summary>
    public int ProxyState { get; private set; }

    /// <summary>WeddingManager::IsInProgress (@0x141b89990).</summary>
    public bool IsInProgress => InProgress(ProxyState);

    public static bool InProgress(int proxyState) => proxyState is 2 or 3 or 4;

    /// <summary>
    /// WeddingManager::OnPacket's proxy modes: 0 OnProxy (@0x141b83a20) and 6 OnProposeAccepted (:249) both run m_proxy.Read. The
    /// other modes do not touch the state here. Returns true when the state was set.
    /// </summary>
    public bool OnPacket(WeddingPacket packet)
    {
        if (packet?.Proxy == null || packet.Mode is not (0 or 6)) return false;
        ProxyState = packet.Proxy.State;
        return true;
    }
}
