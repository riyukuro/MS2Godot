using Godot;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The party window: UIPartyDialog (0x4e45, movie uipartydialog) over CPartyManager's party. Live, the party is the session's
/// (the 0x53 packets); offline, --party-fixture loads <see cref="PartyFixture"/>. CPartyManager opens the window when its party
/// has more than one member (OnPartyInfo) or a member joins while it is closed, and closes it on a disband or the own leave
/// or kick; the dialog's calls (AddMember, UpdateMember, RemoveMemberEntry, ChangeChief) go to it while it is loaded.
/// </summary>
public partial class HudScene
{
    bool partyFixture;                  // --party-fixture: the offline fixture party
    PartyBook partyBook;
    PartyController party;              // its C++ side while the movie is loaded
    MovieDialog partyMovie;             // the movie, from open until OnEndClose
    CanvasLayer partyLayer;
    Action unwatchParty;
    PopupMenuController popupMenu;      // UIPopUpMenu (0x4e39), loaded on the first slot menu and kept, as the client keeps it
    MovieDialog popupMovie;
    CanvasLayer popupLayer;

    Ms2.Formats.Text.BanTextChecker partyNameChecker;

    bool PartyWindowOpen => party != null && !party.IsClosed;

    // ---- scripted live runs (MS2_LIVE_PARTY) ---------------------------------------------------------------------------

    /// <summary>Scripted runs: the party window is up.</summary>
    public bool PartyWindowShown => PartyWindowOpen;

    /// <summary>Scripted runs: a click on a chat party link, as the messenger's hyperLinkLeft runs it (for example "inviteParty,Name").</summary>
    public void PartyLinkClick(string text) => ChatLog.PartyLinks?.Handle(text);

    /// <summary>Scripted runs: the leader and every member of the party book with the offline and dead marks.</summary>
    public string PartyText()
    {
        if (partyBook == null || !partyBook.IsInParty) return "not in a party";
        return $"party {partyBook.PartyId} leader {partyBook.LeaderId}: " + string.Join(", ",
            partyBook.Members.Select(m => $"{m.User.Name}({m.User.CharacterId}) hp {m.User.Hp}/{m.User.MaxHp}{(m.Offline ? " offline" : "")}{(m.User.IsSleeping ? " sleeping" : "")}{(m.User.TombType != 0 ? $" tomb{m.User.TombType}" : "")}"));
    }

    /// <summary>Scripted runs: the vote page's yes / no button.</summary>
    public bool PartyVoteAnswer(bool yes)
    {
        if (!PartyWindowOpen) return false;
        party.AnswerVote(yes);
        return true;
    }

    /// <summary>Scripted runs: the party slot menu of a member, opened as a right click on its slot opens it.</summary>
    public bool PartySlotMenuOpen(long characterId)
    {
        if (!PartyWindowOpen || partyBook?.Find(characterId) == null) return false;
        OpenPartySlotMenu(0, 0, characterId);
        return popupMenu != null && !popupMenu.IsClosed;
    }

    /// <summary>Scripted runs: an item of the open menu picked, as the movie's MenuItemClick does.</summary>
    public bool PartySlotMenuPick(int itemId)
    {
        if (popupMenu == null || popupMenu.IsClosed) return false;
        popupMenu.ItemClick(itemId);
        return true;
    }

    /// <summary>FUN_140710590 for the party window and the popup menu: the session's send; offline the packet is logged.</summary>
    void PartySend(Ms2.Net.Wire.IOutPacket packet)
    {
        if (Ms2Godot.Net.LiveSession.Instance is { } live) live.SendParty(packet);
        else GD.Print($"party: offline, not sent: {packet}");
    }

    /// <summary>Takes the session's party (or the fixture) and follows its window calls; opens the window for a party already
    /// formed, as OnPartyInfo did before this scene was built.</summary>
    void WatchParty()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null) partyBook = live.Party;
        else if (partyFixture) partyBook = PartyFixture.Book(SlotFieldData()?.Id ?? 0, hudModel.Player);
        if (partyBook == null) return;
        if (live != null) live.PartyWindowOpen = () => PartyWindowOpen;
        Action opened = OpenParty;
        Action closed = () => { if (PartyWindowOpen) party.Close(); };
        Action<PartyMember> added = m => { if (PartyWindowOpen) party.AddMember(m); };
        Action<PartyMember> removed = m => { if (PartyWindowOpen) party.RemoveMember(m); };
        Action<PartyMember, int> updated = (m, f) => { if (PartyWindowOpen) party.UpdateMember(m, f); };
        Action refreshed = () => { if (PartyWindowOpen) party.RefreshAllMembers(); };
        Action<PartyMember, PartyMember> chief = (o, n) => { if (PartyWindowOpen) party.ChangeChief(o, n); };
        // CPartyManager's chat lines, notices, boxes and sounds (partymanager.cpp): the handlers' lines after the book's state
        var tables = (menuModel ??= NewModel()).Tables;
        if (live != null) live.DungeonRecords.Rooms = tables.DungeonRooms;     // DungeonRoomTable::GetInstance, which the record manager resolves its rooms from
        var notices = new PartyNotices
        {
            Tables = tables,
            SelfCharacterId = live?.CharacterId ?? PartyFixture.SelfCharacterId,
            Buddies = CommunityBook(),
            StringIdText = t => Notices?.Text(t) ?? t.Text ?? "",
            ChatLine = (text, kind) => ChatLog.AddSystemLine(text, kind),
            Notice = SystemNoticeText,
            PlaySound = key => { if (!string.IsNullOrEmpty(key)) FieldAudio.Instance?.PlayUi(key); },
            InviteLine = name => { ChatLog.AddInvitePartyLine(name); return true; },    // FindMessengerBrowser(0x4e61) is always up here
            DungeonName = id => tables.FieldEnteranceName(id),
        };
        Action<Ms2.Net.Packets.DungeonMatchError> matchError = notices.OnDungeonMatchError;
        if (live != null) live.DungeonMatchErrorReceived += matchError;    // OnDungeonMatchError (uidungeonhelper.cpp:333)
        if (live != null) live.DungeonWaitingReceived += OnDungeonWaiting;                  // UIDungeonLobby::OnRoomPacket (uidungeonlobby.cpp:68)
        // UIMessenger::HandlePartyLink (:2302): the chat lines' party links
        ChatLog.PartyLinks = new PartyLinks
        {
            SelfName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
            IsInParty = () => partyBook.IsInParty,
            EnterState = id => live?.DungeonEnterStateOf(id) ?? 0,
            RoomOf = id => live?.DungeonRoomOf?.Invoke(id),
            RecordOf = id => live?.DungeonRecordOf((uint)id),
            DungeonName = id => tables.FieldEnteranceName(id),
            Key = k => tables.Common(k) ?? "",
            Common = id => tables.Common(id) ?? "",
            Notice = SystemNoticeText,                           // ShowSystemNotice(text, 1, 1)
            Send = PartySend,
            OpenRoom = OpenDungeonRoomOf,                        // UIDungeonRoomDialog::OpenRoom (FUN_141a16c50)
        };
        Action<PartyMessage> applied = msg =>
        {
            popupMenu?.OnPartyMessage(msg);                     // UIPopUpMenu's 0x53 handler
            if (PartyWindowOpen) party.OnPartyMessage(msg);     // UIPartyDialog::OnPartyPacket
            notices.OnApplied(partyBook, msg);                  // the manager handler's own lines, after the state
        };
        var book = partyBook;
        // PartyVoteCli vfunc_2 / vfunc_3: the votes' chat lines, sound, notice and the ready check's answer box
        var votes = new PartyVoteUi
        {
            Tables = (menuModel ??= NewModel()).Tables,
            SelfCharacterId = live?.CharacterId ?? PartyFixture.SelfCharacterId,
            Dialog = () => PartyWindowOpen ? party : null,
            ChatLine = (text, kind) => ChatLog.AddSystemLine(text, kind),
            Notice = SystemNoticeText,                          // ShowSystemNotice(text, 1, 1)
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            Send = PartySend,
        };
        Action<PartyNote> noted = notices.OnNoted;
        Action<PartyVote> voteStarted = v => votes.OnStarted(book, v);
        Action<PartyVote> voteEnded = v => votes.OnEnded(book, v);
        book.Opened += opened; book.Closed += closed; book.MemberAdded += added; book.MemberRemoved += removed;
        book.MemberUpdated += updated; book.MembersRefreshed += refreshed; book.ChiefChanged += chief; book.Applied += applied;
        book.VoteStarted += voteStarted; book.VoteEnded += voteEnded; book.Noted += noted;
        unwatchParty = () =>
        {
            book.Opened -= opened; book.Closed -= closed; book.MemberAdded -= added; book.MemberRemoved -= removed;
            book.MemberUpdated -= updated; book.MembersRefreshed -= refreshed; book.ChiefChanged -= chief; book.Applied -= applied;
            book.VoteStarted -= voteStarted; book.VoteEnded -= voteEnded; book.Noted -= noted;
            if (live != null) { live.PartyWindowOpen = () => false; live.DungeonMatchErrorReceived -= matchError; live.DungeonWaitingReceived -= OnDungeonWaiting; }
            ChatLog.PartyLinks = null;
        };
        if (book.IsInParty) OpenParty();
    }

    FieldData SlotFieldData()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        try { return live?.FieldId is { } id ? FieldData.FindById(assets.Packs, id) : FieldData.FindByXBlock(assets.Packs, xblock); }
        catch (Exception e) { GD.PushWarning("party field data: " + e.Message); return null; }
    }

    /// <summary>OpenDialog(0x4e45) = FUN_141924680: opens only a closed dialog; the movie loads and OnLoadComplete fills it.</summary>
    void OpenParty()
    {
        if (GuideRefusesOpen("PartyDialog")) return;
        if (PartyWindowOpen || partyBook == null) return;
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the party window runs interpreted");
        menuModel ??= NewModel();
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = new PartyController
        {
            Party = partyBook,
            SelfCharacterId = live?.CharacterId ?? PartyFixture.SelfCharacterId,
            Field = SlotFieldData(),
            MapName = id => menuModel.Tables.MapName(id),
            LocalPlayerDead = () => live?.Me.Dead ?? false,   // PCBase::IsDead; offline: never dead
        };
        if (live?.GameSession is { } gs) c.ServerNow = () => gs.ServerTime;     // GetServerTime
        c.SlotMenuOpen = () => popupMenu != null && !popupMenu.IsClosed && popupMenu.InfoString == PopupMenuController.PartySlotInfo;
        c.MemberMenu += (x, y, id) => OpenPartySlotMenu(x, y, id);
        c.ObserveMember += id => GD.Print($"party: observe {id}: StartObserving is not ported");
        c.Recall += () => live?.RecallFromPartyWindow();
        c.Send = PartySend;
        c.SystemNotice += SystemNoticeText;                                      // ShowSystemNotice(text, 1, 1)
        c.DungeonName = id => menuModel.Tables.FieldEnteranceName(id);          // CStringTable::FUN_141811980(0x14, id)
        c.ClubNameError = name =>
        {
            partyNameChecker ??= Ms2.Formats.Text.BanTextChecker.FromPacks(assets.Packs, assets.Language);
            return partyNameChecker.CheckClubName(name, out var message) == 0 ? null : message;
        };
        if (partyLayer == null) AddChild(partyLayer = new CanvasLayer { Layer = 10 });
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (partyMovie == m) { partyMovie = null; party = null; }
        };
        party = c; partyMovie = m;
        partyLayer.AddChild(m);
    }

    Vector2 partyMenuPoint;             // the shot's window point on the slot

    /// <summary>The shot's slot menu (--uigen-party-menu ID), one step a frame through the window's own input: the pointer onto
    /// the member's slot, the right button down, then up. The player's right button dispatch (Main's stage listeners) turns the
    /// click into rightClick, and PartyDialog.OnRightClickParty sends onPopupMenu(x, y, charSN) to the controller.</summary>
    void ShotPartyMenu(long characterId, int step)
    {
        if (step == 0)
        {
            partyMenuPoint = default;
            if (!PartyWindowOpen || party.Host.Invoke(party.Host.Entity, "getMemberAni", characterId.ToString()) is not Ms2.Avm2.Flash.AsDisplayObject slot
                || WindowPoint(partyMovie, slot) is not { } at)
            {
                GD.PushWarning("party menu shot: no slot " + characterId);
                partyMenuShot = 0;
                return;
            }
            partyMenuPoint = at;
            SendMouse(at, null);
        }
        else if (step == 2) SendMouse(partyMenuPoint, true, MouseButton.Right);
        else if (step == 4) SendMouse(partyMenuPoint, false, MouseButton.Right);
    }

    /// <summary>FIXTURE: the offline party's field users are its online members on the own map and channel.</summary>
    bool FixtureUserInField(long id)
    {
        var self = partyBook?.Find(PartyFixture.SelfCharacterId)?.User;
        return self != null && partyBook.Find(id) is { Offline: false } m && m.User.MapId == self.MapId && m.User.Channel == self.Channel;
    }

    /// <summary>FIXTURE: the field user block of an online party member in the field (FixtureUserInField), from its party record.</summary>
    Ms2.Net.Packets.UserBlock FixtureFieldUser(long id)
    {
        if (!FixtureUserInField(id)) return null;
        var u = partyBook.Find(id).User;
        return new Ms2.Net.Packets.UserBlock
        {
            CharacterId = u.CharacterId, Name = u.Name, Level = (short)u.Level, Picture = u.Picture, JobCode = u.JobGroup, Job = u.Job,
            AchievementCombat = u.Trophy.Item1, AchievementAdventure = u.Trophy.Item2, AchievementLifestyle = u.Trophy.Item3,
        };
    }

    /// <summary>A party slot's right click (UIPartyDialog::FUN_141a77760): the popup menu, its movie loaded on first use.</summary>
    void OpenPartySlotMenu(float x, float y, long characterId)
    {
        EnsurePopupMenu();
        popupMenu.OpenForPartySlot(x, y, characterId, hudModel.Player);
    }

    /// <summary>The popup menu (UIPopUpMenu, 0x4e39), its movie loaded on first use and kept, with the books it reads set
    /// for this open (the field, the trade and buddy managers, the own guild).</summary>
    PopupMenuController EnsurePopupMenu()
    {
        if (popupMenu == null)
        {
            var live = Ms2Godot.Net.LiveSession.Instance;
            var c = new PopupMenuController
            {
                Party = partyBook,
                SelfCharacterId = live?.CharacterId ?? PartyFixture.SelfCharacterId,
                // FUN_141028be0(id, picture, 1): a picture not downloaded yet, or none, gives the default image
                ProfileImagePath = (id, picture) => Ms2Godot.Net.ProfileImageService.Images.Path(id, picture, true),
                // FUN_141bd1530: the field's users. FIXTURE (offline): an online fixture member on the own map and channel
                PlayerInField = live != null ? live.UserInField : FixtureUserInField,
                FieldUser = live != null ? live.OtherUserInField : FixtureFieldUser,
                // PCBase::IsDead; read per open, the menu outlives a session. Offline (null): the fixture's health stands in
                LocalPlayerDead = () => Ms2Godot.Net.LiveSession.Instance?.Me.Dead,
            };
            c.Send = PartySend;
            // FUN_141a83ed0: the buddy and block items' UIMsg to UICommunityQueryDialog (0x4e4e)'s OnMessage
            c.BuddyQuery = (query, kind, name, buddyId, charId) => CommunityQuery().OnQuery(query, kind, name, buddyId, charId);
            c.Messenger = ChatLog;                                  // Unk_MdState m_48: the chat windows' browser
            c.PlaySound = key => FieldAudio.Instance?.PlayUi(key);
            c.OpenMyInfo += () => { if (myInfo == null || myInfo.IsClosed) OpenMyInfo(); };     // OpenDialog(0x2711)
            c.OpenCoupleAction += OpenCoupleActionDialog;           // 0x1fc: UICoupleActionDialog (0x4f4e)
            c.CharacterInfo = pcInfoRequests;                       // UIPCInfoDialog (0x4ec8)'s request state
            c.CharacterInfoOpen = () => PcInfoOpen;                 // FUN_141924630: vfunc_55 == 0
            c.CloseCharacterInfo += () => { if (PcInfoOpen) pcInfo.Close(); };   // RequestCloseDialog(0x4ec8)
            c.ItemClicked += (id, t) => GD.Print($"popup menu: item {id} ({menuModel.Tables.PopupMenuName(id)}) for {t?.Name}: its handler is not ported");
            if (popupLayer == null) AddChild(popupLayer = new CanvasLayer { Layer = 11 });
            var m = new MovieDialog(assets, c, menuModel ??= NewModel(), ScreenMode.PixelSize(GetWindow()));
            popupMenu = c; popupMovie = m;
            popupLayer.AddChild(m);
        }
        popupMenu.Field = SlotFieldData();
        popupMenu.TradeBook = Trades;                               // Trade (500): UIPopUpMenu::SendTradeRequest; live only
        popupMenu.Buddies = CommunityBook();                        // CBuddyManager: live, or the offline book
        popupMenu.Guild = GuildState();                             // CGuildManager: the guild invite item and its send
        return popupMenu;
    }
}
