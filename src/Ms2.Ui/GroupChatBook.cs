using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// The UIGroupMessengerBrowser methods CGroupChatManager calls (through the game state's m_messenger, when there is one).
/// </summary>
public interface IGroupMessengerUi
{
    /// <summary>OnChatCreated: the chat this client made.</summary>
    void OnChatCreated(int oid);
    /// <summary>FUN_1419517b0: this character was invited to the chat.</summary>
    void OnInvited(int oid, string inviter, string second);
    /// <summary>RemoveGroup.</summary>
    void RemoveGroup(int oid);
    /// <summary>FUN_141951720: an invite to the chat was sent.</summary>
    void OnInviteSent(int oid, string first, string second);
    /// <summary>OnMemberInvited: <paramref name="member"/> joined, invited by <paramref name="name"/>.</summary>
    void OnMemberInvited(int oid, string name, string member);
    /// <summary>FUN_141951a70: a member left (reason 0) or refused (1).</summary>
    void OnMemberRemoved(int oid, string name, int reason);
    /// <summary>OnGroupMemberOnline.</summary>
    void OnGroupMemberOnline(int oid, string name);
    /// <summary>OnGroupMemberOffline.</summary>
    void OnGroupMemberOffline(int oid, string name);
    /// <summary>QueueTalkLog: a member's line, drawn on the next frame.</summary>
    void QueueTalkLog(int oid, string name, string message);
    /// <summary>ReadItemLinks: the items a member's line links.</summary>
    void ReadItemLinks(int oid, string name, ChatItemLinks links);
    /// <summary>AddNoticeLine: a system line in the chat's window.</summary>
    void AddNoticeLine(int oid, string name, string message);
}

/// <summary>
/// CGroupChatManager (client/ms2/groupchatmanager.cpp): the group chats this character is in (m_chats, a CRBMap by the
/// unsigned oid, at most 3), the sends (SendOp GroupChat 0x4C) and what OnGroupChat (@0x14137f2f0, :577) does on each
/// GroupChat packet (0x8B, <see cref="GroupChatMessage"/>). The UI calls go to <see cref="Messenger"/>.
/// </summary>
public sealed class GroupChatBook
{
    /// <summary>AddChat's and Create's limit (:432, :486).</summary>
    public const int MaxChats = 3;
    /// <summary>Create's and Invite's name length limit (:491, :524).</summary>
    public const int MaxNameLength = 0x18;
    /// <summary>SendGroupChatMode10's text length limit (:555).</summary>
    public const int MaxTextLength = 0x800;

    readonly SortedDictionary<uint, GroupChat> chats = new();

    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>The game state's m_messenger (UIGroupMessengerBrowser), or null.</summary>
    public Func<IGroupMessengerUi> Messenger = () => null;
    /// <summary>GetLocalPlayerName @0x14137e410 (:421): the local player's name (vfunc_77), "" without one.</summary>
    public Func<string> LocalName = () => "";
    /// <summary>The Buddy settings' refuse group chat invite option (Unk_ChatSettings m_refuseInvite, CGameOption).
    /// STILL-OPEN: the option dialog's setting is not ported, so false.</summary>
    public Func<bool> RefuseInvite = () => false;
    /// <summary>CBlockManager::FindBlockedAction(2) with GetBlockActionMessage(.., 2): the block's message, or null when chat
    /// is not blocked. STILL-OPEN: the block manager is not ported, so null.</summary>
    public Func<string> BlockedMessage = () => null;
    /// <summary>The field data's HasLimitContent(0x100) (groupchatmanager.cpp:566); false until the HUD wires the current field's.</summary>
    public Func<bool> ChatLimited = () => false;
    /// <summary>FUN_14151f820: the box shown now, or null.</summary>
    public Func<MessageBoxData> ShownBox = MessageBoxQueue.Current;
    /// <summary>FUN_140d73c60 / ShowMessageBox: show a box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    string F(int id, params object[] args) => ClientFormat.Format(Common(id), args);
    void Box(string text, string tag = "") => ShowBox(MessageBoxQueue.Create(0, text, null, tag));

    /// <summary>The chats in the map's order (by the unsigned oid).</summary>
    public IEnumerable<GroupChat> Chats => chats.Values;
    /// <summary>GetChatCount @0x14137e820 (:479).</summary>
    public int ChatCount => chats.Count;
    /// <summary>FindChat @0x14137e630 (:453): Lookup(oid), else null.</summary>
    public GroupChat FindChat(int oid) => chats.TryGetValue((uint)oid, out var c) ? c : null;

    /// <summary>AddChat @0x14137e4a0 (:429): not for null or with 3 chats; an oid already there keeps its chat.</summary>
    public void AddChat(GroupChat chat)
    {
        if (chat == null || ChatCount >= MaxChats) return;
        if (FindChat(chat.Oid) == null) chats[(uint)chat.Oid] = chat;
    }

    /// <summary>RemoveChat @0x14137e5c0 (:444): RemoveKey(oid).</summary>
    public void RemoveChat(int oid) => chats.Remove((uint)oid);

    /// <summary>FUN_14137e160 (:385): every chat removed (the constructor and the destructor).</summary>
    public void Clear() => chats.Clear();

    /// <summary>FindChatsWithMember @0x14137e6f0 (:464): the oids of the chats with a member of the character id, in the
    /// map's order.</summary>
    public List<int> FindChatsWithMember(long characterId) =>
        chats.Values.Where(c => c.HasMemberById(characterId)).Select(c => c.Oid).ToList();

    // ---- sends ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Create @0x14137e8a0 (:485): with 3 chats a box FormatStringId(0x818, the local name) and no send; a name longer than
    /// 0x18 is not sent (the client asserts and logs); else u8 1 with the name.
    /// </summary>
    public void Create(string name)
    {
        name ??= "";
        if (ChatCount >= MaxChats) { Box(F(0x818, LocalName())); return; }
        if (name.Length > MaxNameLength) return;
        Send(new GroupChatCreateRequest(name));
    }

    /// <summary>
    /// Invite @0x14137eb00 (:504): nothing for an unknown chat; a member of the name gets box 0x816 and no send. With 20
    /// members box 0x814 (tagged with its text), unless the box shown has that tag, which returns; else the invite is
    /// still sent after the box. A name longer than 0x18 is not sent (assert and log).
    /// </summary>
    public void Invite(int oid, string name)
    {
        name ??= "";
        var chat = FindChat(oid);
        if (chat == null) return;
        if (chat.HasMemberByName(name)) { Box(Common(0x816)); return; }
        if (chat.MemberCount >= GroupChat.MaxMembers)
        {
            string text = Common(0x814);
            if (ShownBox() is { } shown && shown.Extra == text) return;
            Box(text, text);
        }
        if (name.Length > MaxNameLength) return;
        Send(new GroupChatInviteRequest(name, oid));
    }

    /// <summary>SendGroupChatMode4 @0x14137efa0 (:540): leave (0) or refuse (1) a known chat.</summary>
    public void SendLeave(int oid, int flag)
    {
        if (FindChat(oid) == null) return;
        Send(new GroupChatLeaveRequest(oid, flag));
    }

    /// <summary>
    /// SendGroupChatMode10 @0x14137f080 (:551): a line to a known chat, at most 0x800 long. A chat block puts its message in
    /// the window (AddNoticeLine with the local name) instead; a field limiting content 0x100 sends nothing.
    /// </summary>
    public void SendTalk(int oid, string text)
    {
        text ??= "";
        if (FindChat(oid) == null) return;
        if (text.Length > MaxTextLength) return;
        if (BlockedMessage() is { } blocked)
        {
            Messenger()?.AddNoticeLine(oid, LocalName(), blocked);
            return;
        }
        if (ChatLimited()) return;
        Send(new GroupChatTalkRequest(text, oid));
    }

    // ---- OnGroupChat ------------------------------------------------------------------------------------------------

    /// <summary>OnGroupChat @0x14137f2f0 (:577): the mode's handler.</summary>
    public void Apply(GroupChatMessage m)
    {
        var ui = Messenger();
        switch (m)
        {
            case GroupChatAdd a:                                    // OnPacketAddChat (:597): the UI is not told
                if (a.Chat == null || ChatCount >= MaxChats) return;
                AddChat(a.Chat);
                break;
            case GroupChatCreated c:                                // OnPacketChatCreated (:608)
                if (FindChat(c.Oid) != null) ui?.OnChatCreated(c.Oid);
                break;
            case GroupChatInviteSent s:                             // FUN_14137f640 (:618)
                if (FindChat(s.Oid) != null) ui?.OnInviteSent(s.Oid, s.First, s.Second);
                break;
            case GroupChatInvited i:                                // FUN_14137f790 (:630)
                if (RefuseInvite()) { SendLeave(i.Oid, 1); return; }
                if (FindChat(i.Oid) != null) ui?.OnInvited(i.Oid, i.Inviter, i.Second);
                break;
            case GroupChatRemove r:                                 // OnPacketRemoveChat (:646)
                if (FindChat(r.Oid) == null) return;
                RemoveChat(r.Oid);
                ui?.RemoveGroup(r.Oid);
                break;
            case GroupChatMemberAdd a:                              // OnPacketAddMember (:657)
                if (FindChat(a.Oid) is not { } chat || !chat.AddMember(a.Member)) return;
                ui?.OnMemberInvited(a.Oid, a.Name, a.Member.Name);
                break;
            case GroupChatMemberRemove r:                           // OnPacketRemoveMember (:672)
                if (FindChat(r.Oid) is not { } from || from.FindMemberByName(r.Name) == null) return;
                from.RemoveMember(r.Name);
                ui?.OnMemberRemoved(r.Oid, r.Name, r.Reason);
                break;
            case GroupChatMemberOnline on:                          // OnPacketMemberOnline (:688)
                if (FindChat(on.Oid)?.FindMemberByName(on.Name) is not { } online) return;
                online.State = 1;
                ui?.OnGroupMemberOnline(on.Oid, on.Name);
                break;
            case GroupChatMemberOffline off:                        // OnPacketMemberOffline (:703)
                if (FindChat(off.Oid)?.FindMemberByName(off.Name) is not { } offline) return;
                offline.State = 2;
                ui?.OnGroupMemberOffline(off.Oid, off.Name);
                break;
            case GroupChatTalk t:                                   // OnPacketChatMessage (:718)
                if (FindChat(t.Oid)?.FindMemberByName(t.Name) == null) return;
                ui?.QueueTalkLog(t.Oid, t.Name, t.Message);
                break;
            case GroupChatItemLinks l:                              // FUN_141380320 (:733)
                if (FindChat(l.Oid)?.FindMemberByName(l.Name) == null) return;
                ui?.ReadItemLinks(l.Oid, l.Name, l.Links);
                break;
            case GroupChatMemberPicture p:                          // OnPacketMemberPicture (:750)
                // SetImage; STILL-OPEN: the UGC picture cache (FUN_141028be0) and its dispatcher's vfunc_1 are not ported
                if (FindChat(p.Oid)?.FindMemberByName(p.Name) is { } pictured) pictured.User.Picture = p.Picture;
                break;
            case GroupChatResult res:                               // OnPacketResult (:768)
                if (res.Code == 3) Box(Common(0x815));              // FUN_140d73bc0(0x815)
                else if (res.Code == 10) Box(F(0x818, res.Second));
                else if (res.Code == 8) Box(F(0x819, res.Second));
                else if (res.Code == 0xd) Box(F(0x29, res.Second));
                break;
        }
    }
}
