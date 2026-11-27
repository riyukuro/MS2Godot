using Ms2.Quest;
using Ms2.Avm2;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the group chat invite, UIInviteGroupDialog (client/ms2/uiinventorydialog.cpp:1939, uiinvitegroupdialog.h;
/// UI id 0x4e90, a UIModalDialog), for its movie uiinvitegroupdialog (InviteGroupDialg). A group window's OnShowInviteDialog
/// sets the chat (m_180, <see cref="SetGroupId"/>) and opens it; it lists the party, guild and friend characters not in the
/// chat, and invites the typed name (Enter, OnInvite) or the checked rows (the movie's onInviteByComunity, one OnInvite each)
/// through CGroupChatManager::Invite (<see cref="GroupChatBook.Invite"/>).
/// STILL-OPEN: the guild's members (CollectGuildMembers, CGuildManager m_info.GetMembers) are not kept by the port's guild
/// book, so none are listed; the rows' image (FUN_141028be0, the UGC picture) is "".
/// </summary>
public sealed class InviteGroupDialogController : ModalDialogController
{
    public const int UiId = 0x4e90;
    public override int TypeId => UiId;
    public override string Movie => "uiinvitegroupdialog";
    protected override string EntityMethod => "updateUserCount";
    /// <summary>GetTypeName (@0x14199f820).</summary>
    public override string TypeName => "InviteGroupDialog";
    public override bool KeysWhileTextFocused => true;   // FUN_1422dc3b0(1) in the ctor (uiinventorydialog.cpp:1945)

    readonly GroupMessengerBrowserController browser;

    public InviteGroupDialogController(GroupMessengerBrowserController browser) => this.browser = browser;

    /// <summary>m_180: the chat the invites go to.</summary>
    public int GroupId { get; private set; }

    /// <summary>CPartyManager::GetMembers.</summary>
    public Func<IEnumerable<PartyMember>> PartyMembers = Array.Empty<PartyMember>;
    /// <summary>CBuddyManager::GetAllBuddies.</summary>
    public Func<IEnumerable<BuddyInfo>> Buddies = Array.Empty<BuddyInfo>;
    /// <summary>GetMessageBoxLayer()->FUN_1419f8270() == 0: a message box is shown.</summary>
    public Func<bool> BoxShown = () => !MessageBoxQueue.IsIdle;
    /// <summary>CUIService::IsTextFieldFocused (<see cref="InputBlock.TextFieldFocused"/>).</summary>
    public Func<bool> TextFieldFocused = () => InputBlock.TextFieldFocused();

    /// <summary>SetGroupId (@0x14199fcf0).</summary>
    public void SetGroupId(int groupId) => GroupId = groupId;

    /// <summary>vfunc_67 (@0x14199f870): UIModalDialog's close, then SetGroupId(0).</summary>
    public override void Close()
    {
        base.Close();
        SetGroupId(0);
    }

    /// <summary>
    /// OnLoadInit (@0x14199f8a0): UIObject's, m_userCount set to common 0x48a of 0 (BlockFormat), then RefreshUserList; run
    /// after UIDialog's OnLoadComplete, as the host has no separate load-init step.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Child("m_userCount") is { } count) Host.Set(count, "text", ClientFormat.Format(browser.Common(0x48a), 0));
        RefreshUserList();
    }

    /// <summary>
    /// vfunc_13 (@0x14199f9a0), after UIDialog's: when the dialog takes keys (FUN_1419a0b20, <see cref="TakesKeys"/>) Return
    /// (DIK 0x1c) invites m_textInput's text; true = used.
    /// </summary>
    public bool OnKey(bool enter)
    {
        if (!TakesKeys()) return false;
        if (!enter) return false;
        if (Child("m_textInput") is { } input) browser.Book.Invite(GroupId, Rt.ToString(Host.Get(input, "text")));
        return true;
    }

    /// <summary>
    /// FUN_1419a0b20 (@0x1419a0b20): m_textInput focused; else not while a message box is shown (FUN_1419f8270 0) or another
    /// text field has the focus (CUIService::IsTextFieldFocused).
    /// </summary>
    bool TakesKeys()
    {
        if (Child("m_textInput") is { } input && Rt.ToNumber(Host.Get(input, "focused")) != 0) return true;
        if (BoxShown()) return false;
        if (TextFieldFocused()) return false;
        return true;
    }

    /// <summary>
    /// vfunc_28 (@0x14199fae0), after UIDialog's: OnInvite(name) invites the name (used); OnChangeUserCount(uint) is
    /// FUN_14199fe70 and OnRefreshUserCount(uint) SetUserCount, both not used.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "OnInvite", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0) browser.Book.Invite(GroupId, Rt.ToString(args[0]));
            return true;
        }
        if (string.Equals(name, "OnChangeUserCount", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0) OnChangeUserCount(Rt.ToUint32(args[0]));
        }
        else if (string.Equals(name, "OnRefreshUserCount", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0) SetUserCount(Rt.ToUint32(args[0]));
        }
        return false;
    }

    /// <summary>
    /// FUN_14199fe70 (@0x14199fe70): SetUserCount, then m_inviteFromComunity: with the checked rows and the chat's members
    /// over 0x14 it is disabled and box 0 of common 0x814 shown (tagged with its text) unless the box shown has that tag;
    /// else it is enabled.
    /// </summary>
    void OnChangeUserCount(uint count)
    {
        SetUserCount(count);
        if (Child("m_inviteFromComunity") is not { } button) return;
        uint total = count;
        if (browser.Book.FindChat(GroupId) is { } chat) total += (uint)chat.MemberCount;
        if (total > 0x14)
        {
            Host.Set(button, "enabled", false);
            string text = browser.Common(0x814);
            if (browser.Book.ShownBox() is { } shown && shown.Extra == text) return;
            browser.Book.ShowBox(MessageBoxQueue.Create(0, text, null, text));
        }
        else Host.Set(button, "enabled", true);
    }

    /// <summary>SetUserCount (@0x1419a0160): m_userCount set to common 0x48a of the count (FUN_14028dee0).</summary>
    void SetUserCount(uint count)
    {
        if (Child("m_userCount") is { } text) Host.Set(text, "text", ClientFormat.Format(browser.Common(0x48a), count));
    }

    /// <summary>One list row (Unk_MdEntry, messengerdata.cpp:875): kind (online), character id, image, name and info.</summary>
    public readonly record struct Entry(int Kind, long Id, string Image, string Name, string Info);

    /// <summary>
    /// The rows of RefreshUserList (@0x1419a0240): CollectPartyMembers (@0x1419a06d0), CollectGuildMembers (@0x1419a07d0,
    /// STILL-OPEN: none) and AddBuddyEntries (@0x1419a08f0), each skipping IsAlreadyListedOrInGroup (@0x1419a0a10): a member
    /// of the chat (the window's IsGroupMember) or an id already listed. A party member (Unk_MdEntry SrcB @0x141483580): kind
    /// not offline, its level (+0x5c) and job (+0x68): the member's user record sits at +0x28, so these are its m_34 and m_40
    /// (<see cref="PartyUser.Level"/>, <see cref="PartyUser.Job"/>), the job taken as is. A friend (SrcA @0x141483300): kind
    /// online, its level and GetSubJob1 of CBuddy::GetJobId (buddy.cpp:92, m_a0, <see cref="BuddyInfo.JobGroup"/>). The info
    /// is common 0x20 of the level and string table 6 of the job.
    /// </summary>
    public List<Entry> CollectEntries()
    {
        var entries = new List<Entry>();
        foreach (var m in PartyMembers())
        {
            if (m == null || IsAlreadyListedOrInGroup(m.User.CharacterId, entries)) continue;
            entries.Add(new Entry(m.Offline ? 0 : 1, m.User.CharacterId, "", m.User.Name, Info(m.User.Level, m.User.Job)));
        }
        foreach (var b in Buddies())
        {
            if (b == null || IsAlreadyListedOrInGroup(b.CharacterId, entries)) continue;
            entries.Add(new Entry(b.Online ? 1 : 0, b.CharacterId, "", b.Name, Info(b.Level, Host?.Model?.Tables?.Skills.Jobs?.GetSubJob1(b.JobGroup) ?? 0)));
        }
        return entries;
    }

    string Info(int level, int job) => ClientFormat.Format(browser.Common(0x20), level, Host?.Model?.Tables?.JobName(job) ?? "");

    bool IsAlreadyListedOrInGroup(long characterId, List<Entry> entries)
    {
        if (browser.FindWindowByGroupId(GroupId) is { } w && w.IsGroupMember(characterId)) return true;
        return entries.Any(e => e.Id == characterId);
    }

    /// <summary>
    /// RefreshUserList (@0x1419a0240): m_userList cleared (FUN_1422dd1d0), then a row {online, iconPath, characterName,
    /// levelAndJobName} added for each entry of kind not 0; at most 8 rows shown, validated; m_textInput emptied and the
    /// movie's updateUserCount.
    /// </summary>
    public void RefreshUserList()
    {
        if (Host?.Entity == null) return;
        var entries = CollectEntries();
        if (Child("m_userList") is { } list)
        {
            if (Host.Get(list, "dataProvider") is AsObject dp) Host.Invoke(dp, "cleanUp");
            foreach (var e in entries)
                if (e.Kind != 0 && Host.Get(list, "dataProvider") is AsObject p)
                    Host.Invoke(p, "push", Host.Obj(("online", e.Kind), ("iconPath", e.Image), ("characterName", e.Name), ("levelAndJobName", e.Info)));
            int count = Host.Get(list, "dataProvider") is AsObject d ? (int)Rt.ToNumber(Host.Get(d, "length")) : 0;
            Host.Set(list, "rowCount", (uint)(count > 8 ? 8 : count));
            Host.Invoke(list, "invalidate");
            Host.Invoke(list, "validateNow");
        }
        if (Child("m_textInput") is { } input) Host.Set(input, "text", "");
        Host.Invoke("updateUserCount");
    }

    /// <summary>
    /// FUN_14199fd10 / FUN_14199fd60 / FUN_14199fdb0 / FUN_14199fdf0 / FUN_14199fe30: a chat member joined, was invited,
    /// left, came online or went offline: nothing while closed (vfunc_55), else RefreshUserList.
    /// </summary>
    public void OnMemberChanged()
    {
        if (IsClosed) return;
        RefreshUserList();
    }

    AsObject Child(string name) => Host?.Entity == null ? null : Host.Child(name) as AsObject;
}
