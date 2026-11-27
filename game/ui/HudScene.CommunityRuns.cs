using System;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>The live run's friend list steps: the community dialog's own callbacks, as its buttons send them.</summary>
public partial class HudScene
{
    /// <summary>The friend list (0x4e4d) is up.</summary>
    public bool CommunityOpenScripted => CommunityOpen;
    /// <summary>The friend query dialog (0x4e4e) is up.</summary>
    public bool CommunityQueryOpenScripted => CommunityQueryOpen;

    /// <summary>The game menu's friend list toggle (CUIService::ToggleDialog(0x4e4d)); true when the list is up after.</summary>
    public bool ToggleCommunityScripted()
    {
        ToggleCommunity();
        return CommunityOpen;
    }

    /// <summary>One of the friend list movie's callbacks (onaddbuddy, onacceptbuddy, ondeletebuddy ...) with its arguments;
    /// false when the list is closed or did not take it.</summary>
    public bool CommunityCallbackScripted(string callback, params object[] args) =>
        CommunityOpen && community.OnCallback(callback, args);

    /// <summary>The query dialog's OnOk(mode, name, memo), as its OK button sends it; false when it is closed.</summary>
    public bool CommunityQueryOkScripted(int mode, string name, string memo) =>
        CommunityQueryOpen && communityQuery.OnCallback("OnOk", new object[] { mode, name, memo });

    /// <summary>The popup menu's unblock item (FUN_141a83ed0: UIMsg(1, 7, name, the entry id, the character id) to the query
    /// dialog's OnMessage); true when the query took it.</summary>
    public bool CommunityUnblockScripted(string name, long entryId, long characterId) =>
        CommunityQuery().OnQuery(1, 7, name, entryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            characterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
