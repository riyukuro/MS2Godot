using Ms2.Avm2;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using ReservedWords = Ms2.Formats.World.ReservedWords;

namespace Ms2.Ui.Hud;

/// <summary>A player shown on the community dialog's nearby tab (tab 0): what FUN_1418d8540 reads from the CProxyGameObject.</summary>
public sealed record NearbyPlayer(long CharacterId, string Name, string PortraitPath, int Level, int Job, string Address,
    string TodayMemo, (int Combat, int Adventure, int Life) Trophy);

/// <summary>
/// The C++ side of the friend list, UICommunityDialog (client/ms2/uibuddyeventlistener.cpp:298; UI id 0x4e4d), for its movie
/// uicommunitydialog. Five tabs (m_190): 0 the players nearby, 1 the friends (and our requests waiting), 2 the requests
/// from others, 3 the blocked, 4 the home blocks. The list is <see cref="BuddyBook"/> (CBuddyManager); the dialog follows it
/// through the UIBuddyEventDispatcher calls (<see cref="BuddyBook.BuddyEvent"/>). The sends (SendOp::Buddy 0x30) go through
/// <see cref="Send"/>.
/// STILL-OPEN: the home blocks (HomeBlockManager, tab 4 and vfunc_buddy_12..14) are not ported, so tab 4 lists nothing; the
/// popups (0x4e39: PopUpMenuForBuddy, the proxy popup, PopUpMenuForHomeBlock), the home visit (FUN_1418fd330 and its box
/// FUN_1418fde20), the VIP page (FindWebBrowser(0x4e6a)) and the query dialog's open come out as events; FUN_1422dc3b0 of
/// ChangeTab is left out.
/// </summary>
public sealed class CommunityController : UiController, IMessageBoxListener
{
    public override string Movie => "uicommunitydialog";
    protected override string EntityMethod => "buildBuddyList";
    /// <summary>GetTypeName (@0x1418d3bf0).</summary>
    public override string TypeName => "CommunityDialog";

    /// <summary>IsFeatureEnabled(0x82): the VIP row on the nearby tab and OnVIPGMClick.</summary>
    public const int VipFeature = 0x82;
    /// <summary>The message box tag of OnEditBanMemo's failed check.</summary>
    public const string BanMemoFailTag = "communityBanMemoFail";

    public BuddyBook Book { get; }

    int tab;            // m_190
    int online;         // m_194
    bool recount;       // m_198
    bool dirty;         // m_19c
    long editingId;     // m_1a0

    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>CStringTable FUN_141811980(3, id): a map's name.</summary>
    public Func<int, string> MapName = _ => "";
    /// <summary>CStringTable FUN_141811980(6, code): a job's name.</summary>
    public Func<int, string> JobName = _ => "";
    /// <summary>FUN_141028be0(character id, path, 1, ""): the profile image's path.</summary>
    public Func<long, string, string> ProfileImagePath = (_, p) => p ?? "";
    /// <summary>CAddressTable::FormatChecked(map, plot, apartment, home name): the friend's home address. STILL-OPEN: the
    /// address table is not ported, so "".</summary>
    public Func<BuddyInfo, string> Address = _ => "";
    /// <summary>The local player's character id (IsOtherCharacter: 0 when there is none), null when there is no local player.</summary>
    public Func<long?> SelfCharacterId = () => null;
    /// <summary>CPartyManager::FindMemberByCharId != null.</summary>
    public Func<long, bool> IsPartyMember = _ => false;
    /// <summary>WorldService GetFieldId: the map of the nearby tab's count text.</summary>
    public Func<int> FieldId = () => 0;
    /// <summary>The other players and the local one in the field (FUN_141bc9fb0's map, IsOtherPlayer || IsSelf).</summary>
    public Func<IEnumerable<NearbyPlayer>> NearbyPlayers = () => Array.Empty<NearbyPlayer>();
    /// <summary>CommonString GetChecker: the block memo's check; null skips it.</summary>
    public BanTextChecker Checker;
    /// <summary>CFeatureManager::IsFeatureEnabled.</summary>
    public Func<int, bool> FeatureEnabled = _ => false;
    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>ShowMessageBox.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>FUN_1418d6ed0(a, b): the query dialog (0x4e4e) gets a message of the two ints (OnAddBuddy 0, 1; OnAddBan 0, 2).</summary>
    public event Action<int, int> QueryRequested;
    /// <summary>OnBuddyPopupMenu: the popup 0x4e39 at (x, y) for the id on the tab (PopUpMenuForBuddy flags 0x63fe; the
    /// proxy popup 0x3f3fe on tab 0; PopUpMenuForHomeBlock 0x20046 on tab 4). STILL-OPEN.</summary>
    public event Action<float, float, long, int> PopupRequested;
    /// <summary>onHomeVisit: FUN_1418fd330(id) and the box FUN_1418fde20(0, id, kind, name) with OnHomeVisitConfirm
    /// (kind 7 a friend, 8 a player nearby). STILL-OPEN.</summary>
    public event Action<long, int, string> HomeVisitRequested;
    /// <summary>OnVIPGMClick: FindWebBrowser(0x4e6a)->Navigate(the constant at +0x8838, true). STILL-OPEN.</summary>
    public event Action VipPageRequested;
    /// <summary>OnEditBanMemo on tab 4: HomeBlockManager FUN_140a47c80(id, memo). STILL-OPEN.</summary>
    public event Action<long, string> HomeBlockMemoEdited;

    public CommunityController(BuddyBook book) => Book = book;

    /// <summary>GetTab (@0x1418d6960).</summary>
    public int Tab => tab;
    /// <summary>m_194: the online friend count the tab-1 text shows.</summary>
    public int Online => online;

    /// <summary>SetTab (@0x1418d6930): before the movie loads (the game menu's open path).</summary>
    public void SetTab(int t)
    {
        if (t < 0 || t >= 5) return;
        tab = t;
    }

    /// <summary>ResetTab (@0x1418d6410).</summary>
    public void ResetTab() => tab = 0;

    bool NoMovie => Host?.Entity == null;

    /// <summary>OnLoadComplete (@0x1418d3650): UIDialog's, InitData, then the UGC, buddy and entity listeners.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        InitData();
        Book.BuddyEvent -= OnBuddyEvent;
        Book.BuddyEvent += OnBuddyEvent;
    }

    /// <summary>vfunc_67 (@0x1418d33a0): UIDialog's close, the listeners removed, the counters cleared.</summary>
    public override void Close()
    {
        base.Close();
        Book.BuddyEvent -= OnBuddyEvent;
        dirty = false;
        recount = false;
        online = 0;
        editingId = 0;
    }

    /// <summary>InitData (@0x1418d6980): initData(tab).</summary>
    void InitData()
    {
        if (NoMovie) return;
        Host.Invoke(Host.Entity, "initData", tab);
    }

    /// <summary>vfunc_72 (@0x1418d34a0), each frame: the online friend count again when asked on tab 1, then the count
    /// text when the list changed.</summary>
    public void Process()
    {
        if (IsClosed) return;
        if (recount && tab == 1) online = FriendOnlineCount();
        recount = false;
        if (dirty && Host?.Child("m_list") is { } list)
            SetCountText(online, Rt.ToInt32(Host.Get(Host.Get(list, "dataProvider"), "length")));
        dirty = false;
    }

    /// <summary>vfunc_79 (@0x1418d3a70): a Buddy packet (0x61) the book has read. Protocol 1 rebuilds the tab (not tab 0);
    /// 14 on tab 1 recounts.</summary>
    public void OnPacket(BuddyMessage m)
    {
        if (NoMovie) return;
        switch (m.Protocol)
        {
            case 1:
                if (tab != 0) BuildBuddyList(tab);
                break;
            case 0xe:
                if (tab == 1 && m.Flag is 0 or 1) { recount = true; dirty = true; }
                break;
        }
    }

    /// <summary>vfunc_13 (@0x1418d3c40): Enter (DIK 0x1c) while a block memo is edited ends the edit (endEditBanMemo).
    /// True = the key is taken.</summary>
    public bool OnKey(bool enter)
    {
        if (editingId == 0) return false;
        if (!enter) return false;
        if (!NoMovie) Host.Invoke(Host.Entity, "endEditBanMemo", MailTexts.IdText(editingId));
        return true;
    }

    /// <summary>OnMessage (@0x1418d3720) from the popup: "editMemo" and the id runs the movie's editMemo (the id as a
    /// number, FUN_140c1c8c0). 1 when handled.</summary>
    public bool PopupCommand(string command, string id)
    {
        if (IsClosed || NoMovie) return false;
        if (!string.Equals(command, "editMemo", StringComparison.OrdinalIgnoreCase)) return false;
        Host.Invoke(Host.Entity, "editMemo", ReservedWords.ParseUInt64(id ?? ""));
        return true;
    }

    /// <summary>OnMessage (@0x1418d3720) from the message box: a tag holding "communityBanMemoFail" split on "," gives the
    /// id; a non-zero one runs the movie's editMemoFail. 1 once there are two parts.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (IsClosed || NoMovie) return false;
        tag ??= "";
        if (!tag.Contains(BanMemoFailTag, StringComparison.Ordinal)) return false;
        var parts = tag.Split(',');   // FUN_14025a680(text, parts, L",")
        if (parts.Length <= 1) return false;
        ulong id = ReservedWords.ParseUInt64(parts[1]);
        if (id > 0) Host.Invoke(Host.Entity, "editMemoFail", id);
        return true;
    }

    /// <summary>vfunc_28 (@0x1418d3d70).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        bool any = args is { Length: > 0 };   // ASData::SetArgsIfAny
        string Str(int i) => args.Length > i ? Rt.ToString(args[i]) ?? "" : "";
        long U64Arg(int i) => unchecked((long)ReservedWords.ParseUInt64(Str(i)));
        switch (name.ToLowerInvariant())
        {
            case "onchangetab":
                if (!any) return false;
                ChangeTab(Rt.ToInt32(args[0]));
                return true;
            case "onaddban":
                QueryRequested?.Invoke(0, 2);
                return true;
            case "onaddbuddy":
                QueryRequested?.Invoke(0, 1);
                return true;
            case "onacceptbuddy":
                if (!any) return false;
                Send(BuddyRequest.Accept(U64Arg(0)));
                return true;
            case "ondeclinebuddy":
                if (!any) return false;
                Send(BuddyRequest.Decline(U64Arg(0)));
                return true;
            case "ondeletebuddy":
                if (!any) return false;
                Send(BuddyRequest.Remove(U64Arg(0)));
                return true;
            case "oncancelbuddy":
                if (!any) return false;
                Send(BuddyRequest.Cancel(U64Arg(0)));
                return true;
            case "onhomevisit":
            {
                if (!any) return false;
                long target = U64Arg(0);
                long id; int kind; string who = "";
                if (Book.FindById(target) is { } b) { id = b.CharacterId; kind = 7; who = b.Name; }
                else
                {
                    id = target; kind = 8;
                    if (NearbyPlayers().FirstOrDefault(p => p.CharacterId == id) is { } p) who = p.Name;
                }
                // FUN_1418fd330(id) != 0, then the box: both are the home visit's (STILL-OPEN), answered 0 here as the
                // client does when the test fails
                HomeVisitRequested?.Invoke(id, kind, who);
                return false;
            }
            case "onbuddypopupmenu":
            {
                if (!any) return false;
                float x = (float)Rt.ToNumber(args[0]);
                float y = args.Length > 1 ? (float)Rt.ToNumber(args[1]) : 0;
                long target = U64Arg(2);
                if (tab == 0) { if (NearbyPlayers().Any(p => p.CharacterId == target)) PopupRequested?.Invoke(x, y, target, tab); }
                else if (tab == 4) PopupRequested?.Invoke(x, y, target, tab);   // HomeBlockManager FindById (STILL-OPEN)
                else if (Book.FindById(target) != null) PopupRequested?.Invoke(x, y, target, tab);
                return true;
            }
            case "oneditbanmemo":
            {
                if (!any) return true;
                editingId = 0;
                string who = Str(0), memo = Str(1);
                long id = U64Arg(2);
                if (Checker != null && Checker.CheckBuddyMemo(memo, out string message) != 0)
                {
                    ShowBox(MessageBoxQueue.Create(0, message, this, $"{BanMemoFailTag},{unchecked((ulong)id)}"));
                    return true;
                }
                if (tab == 3) Send(BuddyRequest.EditMemo(id, who, memo));
                else if (tab == 4) HomeBlockMemoEdited?.Invoke(id, memo);
                return true;
            }
            case "oneditbanmemostart":
                if (any) editingId = U64Arg(0);
                return true;
            case "oneditbanmemoend":
                editingId = 0;
                return true;
            case "onvipgmclick":
                if (!FeatureEnabled(VipFeature)) return true;
                VipPageRequested?.Invoke();
                Send(new EventRewardRequest(5, 1));
                return true;
        }
        return false;
    }

    /// <summary>vfunc_45 (@0x1418d5150): getTrophyTooltip(id) = {total, combat, life, adventure} of the player nearby (tab 0)
    /// or of the entry (FUN_1418d7af0); nothing when there is none.</summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (!string.Equals(name, "getTrophyTooltip", StringComparison.OrdinalIgnoreCase)) return null;
        long id = unchecked((long)ReservedWords.ParseUInt64(args is { Length: > 0 } ? Rt.ToString(args[0]) ?? "" : ""));
        (int Combat, int Adventure, int Life)? t = null;
        if (tab == 0) { if (NearbyPlayers().FirstOrDefault(p => p.CharacterId == id) is { } p) t = p.Trophy; }
        else if (Book.FindById(id) is { } b) t = b.Trophy;
        if (t is not { } v) return null;
        return new Dictionary<string, object>
        {
            ["total"] = v.Combat + v.Adventure + v.Life, ["combat"] = v.Combat, ["life"] = v.Life, ["adventure"] = v.Adventure,
        };
    }

    /// <summary>vfunc_ugc_1 (@0x1418d5340): a profile image arrived for a character: the player nearby's id on tab 0, else
    /// the entry's id of that character; updateBuddyProfile(id, image path).</summary>
    public void OnProfile(long characterId, string path)
    {
        if (IsClosed || characterId == 0 || NoMovie) return;
        string id = "";
        if (tab == 0) id = MailTexts.IdText(characterId);
        else if (Book.FindByAccount(characterId) is { } b) id = MailTexts.IdText(b.Id);
        if (id.Length > 0) Host.Invoke(Host.Entity, "updateBuddyProfile", id, ProfileImagePath(characterId, path));
    }

    /// <summary>The UIBuddyEventListener calls, vfunc_buddy_1..11 (@0x1418d55e0..; 7 and 10 are not overridden).</summary>
    void OnBuddyEvent(BuddyEventKind kind, long id, int arg)
    {
        if (NoMovie) return;
        switch (kind)
        {
            case BuddyEventKind.RequestReceived:    // vfunc_buddy_1
                if (tab == 2) { dirty = true; AddRow(tab, id); }
                else SetNew(2, 1, 1);
                break;
            case BuddyEventKind.Declined:           // vfunc_buddy_2
                if (tab == 2) { dirty = true; RemoveBuddyRow(id); }
                else SetNew(2, 1, -1);
                break;
            case BuddyEventKind.Accepted:           // vfunc_buddy_3
                if (tab == 1) AddRow(tab, id);
                else if (tab == 2) { dirty = true; RemoveBuddyRow(id); SetNew(1, 1, 1); }
                else SetNew(1, 1, 1);
                break;
            case BuddyEventKind.Blocked:            // vfunc_buddy_4
                if (tab == 3) AddRow(tab, id);
                else
                {
                    SetNew(3, 1, 1);
                    if (arg != 0)
                    {
                        if (tab == 1) { recount = true; RemoveBuddyRow(id); }
                        else SetNew(1, 1, -1);
                    }
                }
                break;
            case BuddyEventKind.Removed:            // vfunc_buddy_5
                if (tab is 1 or 2) { recount = true; RemoveBuddyRow(id); }
                else SetNew(1, 1, -1);
                break;
            case BuddyEventKind.Unblocked:          // vfunc_buddy_6
                if (tab == 3) RemoveBuddyRow(id);
                else SetNew(3, 1, -1);
                break;
            case BuddyEventKind.Cancelled:          // vfunc_buddy_8
                if (tab == 1) RemoveBuddyRow(id);
                else SetNew(1, 1, -1);
                break;
            case BuddyEventKind.Updated:            // vfunc_buddy_9
                if (Book.FindById(id) is { } b
                    && ((b.Received && tab == 2) || (b.IsFriend && tab == 1) || (b.Blocked && tab == 3)))
                    UpdateBuddyRow(tab, b);
                break;
            case BuddyEventKind.MemoChanged:        // vfunc_buddy_11
                if (tab == 3 && Book.FindById(id) is { Blocked: true } m)
                    Host.Invoke(Host.Entity, "updateBanMemo", MailTexts.IdText(id), m.Memo);
                break;
        }
    }

    /// <summary>The entity listener's vfunc_3 (@0x1418d5e90): another player came into the field. On tab 0 the row is added
    /// (isNew 1), else the tab's new mark.</summary>
    public void OnPlayerAdded(NearbyPlayer p, bool otherPlayer)
    {
        if (p == null || IsClosed || NoMovie || !otherPlayer) return;
        if (tab == 0)
        {
            dirty = true;
            Host.Invoke(Host.Entity, "addBuddy", NearbyRow(p, true));
        }
        else SetNew(0, 1, 1);
    }

    /// <summary>vfunc_4 (@0x1418d6060): another player left the field; removeBuddy on tab 0.</summary>
    public void OnPlayerRemoved(long characterId, bool otherPlayer)
    {
        if (IsClosed || NoMovie || !otherPlayer || tab != 0) return;
        dirty = true;
        Host.Invoke(Host.Entity, "removeBuddy", MailTexts.IdText(characterId));
    }

    /// <summary>vfunc_5 (@0x1418d61d0): a player changed; on tab 0 the row is updated when a flag past the two low bits is
    /// set.</summary>
    public void OnPlayerUpdated(NearbyPlayer p, int flags)
    {
        if (p == null || IsClosed || NoMovie) return;
        if (tab != 0 || flags == 0) return;
        flags &= ~3;
        if (flags > 0) Host.Invoke(Host.Entity, "updateBuddy", NearbyRow(p, false));
    }

    /// <summary>ChangeTab (@0x1418d69f0).</summary>
    public void ChangeTab(int t)
    {
        if (NoMovie) return;
        if (t < 0 || t >= 5) return;
        tab = t;
        BuildBuddyList(t);
    }

    /// <summary>BuildBuddyList (@0x1418d6430): tab 0 the players nearby, tab 4 the home blocks, else the book's entries of
    /// the tab; then the new marks of all five tabs (the friends' and the blocked's new entries, every request).</summary>
    public void BuildBuddyList(int t)
    {
        if (NoMovie) return;
        bool skip = false;
        dirty = true;
        if (t == 0) { BuildNearbyPlayerList(); skip = true; }
        if (t == 4) { Host.Invoke(Host.Entity, "buildBuddyList", new List<object>()); skip = true; }   // BuildHomeBlockList (STILL-OPEN)
        var rows = new List<object>();
        int n = 0;
        var counts = new int[5];
        foreach (var b in Book.Buddies.ToList())
        {
            bool include = false;
            if (b.Waiting) include = t == 1;
            else if (b.IsFriend)
            {
                if (b.Online) n++;
                if (b.IsNew) counts[1]++;
                include = t == 1;
            }
            else if (b.Received) { counts[2]++; include = t == 2; }
            else if (b.Blocked) { if (b.IsNew) counts[3]++; include = t == 3; }
            if (!include) continue;
            rows.Add(BuddyRow(b, t));
        }
        online = n;
        if (!skip) Host.Invoke(Host.Entity, "buildBuddyList", rows);
        for (int i = 0; i < 5; i++) SetNew(i, counts[i] > 0 ? 1 : 0, counts[i]);
    }

    /// <summary>BuildNearbyPlayerList (@0x1418d6a90): the VIP row first, then a row of each player.</summary>
    void BuildNearbyPlayerList()
    {
        var rows = new List<object>();
        if (FeatureEnabled(VipFeature)) rows.Add(new Dictionary<string, object> { ["isVIPGM"] = true });   // FUN_1418d8ac0
        foreach (var p in NearbyPlayers()) rows.Add(NearbyRow(p, false));
        Host.Invoke(Host.Entity, "buildBuddyList", rows);
    }

    /// <summary>SetNew (@0x1418d6f80).</summary>
    void SetNew(int t, int isNew, int count)
    {
        if (NoMovie) return;
        Host.Invoke(Host.Entity, "setNew", t, isNew, count);
    }

    /// <summary>FUN_1418d71d0 and AddBuddyRow (@0x1418d7000): an online friend on tab 1 counts.</summary>
    void AddRow(int t, long id)
    {
        if (Book.FindById(id) is not { } b) return;
        var row = BuddyRow(b, t);
        if (tab == 1 && b.IsFriend && b.Online) { online++; dirty = true; }
        Host.Invoke(Host.Entity, "addBuddy", row);
    }

    /// <summary>RemoveBuddyRow (@0x1418d7240): an online friend on tab 1 leaves the count.</summary>
    void RemoveBuddyRow(long id)
    {
        if (tab == 1 && Book.FindById(id) is { IsFriend: true, Online: true }) { online--; dirty = true; }
        Host.Invoke(Host.Entity, "removeBuddy", MailTexts.IdText(id));
    }

    /// <summary>UpdateBuddyRow (@0x1418d73b0).</summary>
    void UpdateBuddyRow(int t, BuddyInfo b) => Host.Invoke(Host.Entity, "updateBuddy", BuddyRow(b, t));

    /// <summary>FUN_1418d76c0: the friends online.</summary>
    int FriendOnlineCount() => Book.Buddies.Count(b => b.IsFriend && b.Online);

    /// <summary>FUN_1418d77f0: setOnline(the tab's count text): 0x60e the map and the rows, 0x60f the requests, 0x610 the
    /// blocked, 0x612 online of all friends, 0x611 the home blocks.</summary>
    void SetCountText(int onlineCount, int total)
    {
        string text = tab switch
        {
            0 => ClientFormat.Format(Common(0x60e), MapName(FieldId()) ?? "", total),
            2 => ClientFormat.Format(Common(0x60f), total),
            3 => ClientFormat.Format(Common(0x610), total),
            1 => ClientFormat.Format(Common(0x612), onlineCount, total),
            4 => ClientFormat.Format(Common(0x611), total),
            _ => "",
        };
        Host.Invoke(Host.Entity, "setOnline", text);
    }

    /// <summary>IsOtherCharacter: 0 when there is no local player or the id is the local character.</summary>
    int IsOtherCharacter(long characterId) => SelfCharacterId() is { } self && self != characterId ? 1 : 0;

    string CharInfo(int level, int jobCode) => ClientFormat.Format(Common(0x20), level, JobName(jobCode) ?? "");

    /// <summary>BuildBuddyRow (@0x1418d7b90). Building a row clears the entry's new mark (ConsumeIsNew).</summary>
    public Dictionary<string, object> BuddyRow(BuddyInfo b, int t)
    {
        bool waiting = b.Waiting;
        bool on = b.Online && !waiting;
        string state;
        if (on) state = b.Channel == 0 ? MapName(b.MapId) ?? "" : ClientFormat.Format(Common(0x60c), b.Channel, MapName(b.MapId) ?? "");
        else state = Common(waiting ? 0x60b : 0x60d);
        var row = new Dictionary<string, object>
        {
            ["charname"] = b.Name,
            ["buddysn"] = MailTexts.IdText(b.Id),
            ["imgpath"] = ProfileImagePath(b.CharacterId, b.ProfilePath),
            ["isWaiting"] = waiting ? 1 : 0,
            ["offline"] = !on,
            ["trophyTotal"] = b.TrophyTotal,
            ["homeVisitEnable"] = IsOtherCharacter(b.CharacterId),
            ["isParty"] = IsPartyMember(b.CharacterId) ? 1 : 0,
            ["state"] = state,
            ["charinfo"] = CharInfo(b.Level, b.Job),
            ["isNew"] = b.ConsumeIsNew() ? 1 : 0,
            ["recv"] = t == 2 ? 1 : 0,
        };
        if (t == 2) row["msg"] = b.Message;
        else if (t == 1) { row["address"] = Address(b) ?? ""; row["todaymemo"] = b.TodayMemo; }
        else row["memo"] = b.Memo;
        return row;
    }

    /// <summary>FUN_1418d8540: a nearby player's row; isBuddy when the book holds the character as a friend.</summary>
    public Dictionary<string, object> NearbyRow(NearbyPlayer p, bool isNew) => new()
    {
        ["isPlayerInfo"] = true,
        ["charname"] = p.Name,
        ["buddysn"] = MailTexts.IdText(p.CharacterId),
        ["isBuddy"] = Book.FindByAccount(p.CharacterId) is { IsFriend: true } ? 1 : 0,
        ["isParty"] = IsPartyMember(p.CharacterId) ? 1 : 0,
        ["imgpath"] = ProfileImagePath(p.CharacterId, p.PortraitPath),
        ["charinfo"] = ClientFormat.Format(Common(0x20), (ushort)(short)p.Level, JobName(p.Job) ?? ""),
        ["offline"] = 0,
        ["address"] = p.Address ?? "",
        ["todaymemo"] = p.TodayMemo ?? "",
        ["isNew"] = isNew ? 1 : 0,
        ["trophyTotal"] = p.Trophy.Combat + p.Trophy.Adventure + p.Trophy.Life,
        ["homeVisitEnable"] = IsOtherCharacter(p.CharacterId),
        ["isVIPGM"] = false,
    };
}
