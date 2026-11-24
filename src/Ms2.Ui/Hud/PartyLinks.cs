using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The party links of a chat line: UIMessenger::HandlePartyLink (@0x141a168a0, uimessenger.cpp:2302), run by the messenger movie's
/// getUserData "hyperLinkLeft" (vfunc_45, :4756) with the link's text split on ",". The first part names the link:
/// "dungeonRoom,room" (open the dungeon room dialog), "joinPartyDungeonHelper,party,leader,room" (:2365),
/// "joinPartyDungeonMember,party,leader,room" (:2407) and "inviteParty,name" (SendPartyMode1). A link with another part count does
/// nothing. The numbers are _wtoi (FUN_140718200).
/// STILL-OPEN: "dungeonRoom" needs UIDungeonRoomDialog (0x4ebd), which this client does not have (<see cref="OpenRoom"/> is null
/// and the link does nothing); the helper link's CanClaimHelperReward check (MyCharacter record) is not recovered
/// (<see cref="HelperRewardClaimable"/> answers null and the link stops there, with no notice and no box); the "s_party_join_only_dungeon_expert"
/// notice after it needs the room's helper clear count.
/// </summary>
public sealed class PartyLinks
{
    /// <summary>The own name (MyPC vfunc_77); null without a local player.</summary>
    public Func<string> SelfName = () => null;
    /// <summary>CPartyManager::IsInParty.</summary>
    public Func<bool> IsInParty = () => false;
    /// <summary>CMyCharacter::GetDungeonEnterState (@0x140d9f880).</summary>
    public Func<int, int> EnterState = _ => 0;
    /// <summary>DungeonRoomTable::GetRoom, for ShowEnterLimitMessage (<see cref="EnterLimitNotice"/>).</summary>
    public Func<int, DungeonRoom> RoomOf = _ => null;
    /// <summary>The dungeon room's name (CStringTable::FUN_141811980(0x14, room)).</summary>
    public Func<int, string> DungeonName = _ => "";
    /// <summary>GetStringByKey.</summary>
    public Func<string, string> Key = k => k;
    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>ShowYesNoMessageBox(callback, text): the box is shown now.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_140710590: a packet goes out.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>The own character's CanClaimHelperReward(room); null = not known (STILL-OPEN, the link stops).</summary>
    public Func<int, bool?> HelperRewardClaimable = _ => null;
    /// <summary>UIDungeonRoomDialog::OpenRoom; null = no such dialog (STILL-OPEN).</summary>
    public Action<int> OpenRoom;

    /// <summary>HandlePartyLink: the link's parts (the text split on ","); nothing for an empty list or an unknown name.</summary>
    public void Handle(string[] args)
    {
        if (args == null || args.Length == 0) return;
        switch (args[0])
        {
            case "dungeonRoom":
                if (args.Length != 2) return;
                OpenRoom?.Invoke(GraphicOptionPage.Wtoi(args[1]));          // FUN_141a16c50 (:2381)
                break;
            case "joinPartyDungeonHelper":
                if (args.Length != 4) return;
                Join(GraphicOptionPage.Wtoi(args[1]), args[2], GraphicOptionPage.Wtoi(args[3]), helper: true);
                break;
            case "joinPartyDungeonMember":
                if (args.Length != 4) return;
                Join(GraphicOptionPage.Wtoi(args[1]), args[2], GraphicOptionPage.Wtoi(args[3]), helper: false);
                break;
            case "inviteParty":
                if (args.Length != 2) return;
                Send(new PartyInviteRequest(args[1]));                       // FUN_141a17690: SendPartyMode1 (partymanager.cpp:761)
                break;
        }
    }

    /// <summary>The same for a link's text (FUN_14025a680 splits on ",").</summary>
    public void Handle(string text) => Handle((text ?? "").Split(','));

    /// <summary>FUN_141a16cc0 (:2388, helper) and FUN_141a172a0 (:2407, member): no local player does nothing; the own name as the
    /// leader, a party and a dungeon enter state of 10 or more each give a notice and stop (the state through ShowEnterLimitMessage);
    /// the helper link then needs CanClaimHelperReward; then the yes / no box of the leader and room, whose first button sends
    /// Party 0x27 (helper) or 0x2b (member) with the party id and room, unless one of them is 0.</summary>
    void Join(int partyId, string leader, int roomId, bool helper)
    {
        string self = SelfName();
        if (self == null) return;
        if (self == leader) { Notice(Key("s_party_is_my_dungeon_help")); return; }
        if (IsInParty()) { Notice(Key("s_party_has_party")); return; }
        int state = EnterState(roomId);
        if (state >= 10)
        {
            string text = EnterLimitNotice.Text(state, RoomOf(roomId), Common, Key);
            if (!string.IsNullOrEmpty(text)) Notice(text);
            return;
        }
        if (helper && HelperRewardClaimable(roomId) is not true) return;
        string msg = ClientFormat.Format(Key(helper ? "s_party_join_dungeon_helper_msg" : "s_party_join_dungeon_member_msg"), leader, DungeonName(roomId) ?? "");
        ShowBox(MessageBoxQueue.CreateWithCallback(msg, 1, (button, _) =>
        {
            if (button == 0 && partyId != 0 && roomId != 0)
                Send(helper ? new PartyDungeonHelperJoin(partyId, roomId) : new PartyDungeonMemberJoin(partyId, roomId));
            return true;
        }));
    }
}
