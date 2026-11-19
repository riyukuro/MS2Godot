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
        Action<PartyMessage> applied = msg =>
        {
            popupMenu?.OnPartyMessage(msg);                     // UIPopUpMenu's 0x53 handler
            if (PartyWindowOpen) party.OnPartyMessage(msg);     // UIPartyDialog::OnPartyPacket
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
        Action<PartyVote> voteStarted = v => votes.OnStarted(book, v);
        Action<PartyVote> voteEnded = v => votes.OnEnded(book, v);
        book.Opened += opened; book.Closed += closed; book.MemberAdded += added; book.MemberRemoved += removed;
        book.MemberUpdated += updated; book.MembersRefreshed += refreshed; book.ChiefChanged += chief; book.Applied += applied;
        book.VoteStarted += voteStarted; book.VoteEnded += voteEnded;
        unwatchParty = () =>
        {
            book.Opened -= opened; book.Closed -= closed; book.MemberAdded -= added; book.MemberRemoved -= removed;
            book.MemberUpdated -= updated; book.MembersRefreshed -= refreshed; book.ChiefChanged -= chief; book.Applied -= applied;
            book.VoteStarted -= voteStarted; book.VoteEnded -= voteEnded;
            if (live != null) live.PartyWindowOpen = () => false;
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
        c.Recall += () => GD.Print("party: recall: not ported");
        c.Send = PartySend;
        c.SystemNotice += text => GD.Print($"party: system notice: {text}");    // ShowSystemNotice: the notices are not ported
        c.ClubNameError = name =>
        {
            partyNameChecker ??= Ms2.Formats.Text.BanTextChecker.FromPacks(assets.Packs, assets.Language);
            return partyNameChecker.CheckClubName(name, out var message) == 0 ? null : message;
        };
        if (partyLayer == null) AddChild(partyLayer = new CanvasLayer { Layer = 10 });
        var m = new MovieDialog(assets, c, menuModel, GetWindow().Size);
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

    /// <summary>A party slot's right click (UIPartyDialog::FUN_141a77760): the popup menu, its movie loaded on first use.</summary>
    void OpenPartySlotMenu(float x, float y, long characterId)
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
                // PCBase::IsDead; read per open, the menu outlives a session. Offline (null): the fixture's health stands in
                LocalPlayerDead = () => Ms2Godot.Net.LiveSession.Instance?.Me.Dead,
            };
            c.Send = PartySend;
            // FUN_141a83ed0: the buddy and block items' UIMsg to UICommunityQueryDialog (0x4e4e)'s OnMessage
            c.BuddyQuery = (query, kind, name, buddyId, charId) => CommunityQuery().OnQuery(query, kind, name, buddyId, charId);
            c.Messenger = ChatLog;                                  // Unk_MdState m_48: the chat windows' browser
            c.PlaySound = key => FieldAudio.Instance?.PlayUi(key);
            c.OpenMyInfo += () => { if (myInfo == null || myInfo.IsClosed) OpenMyInfo(); };     // OpenDialog(0x2711)
            c.CharacterInfo = pcInfoRequests;                       // UIPCInfoDialog (0x4ec8)'s request state
            c.CharacterInfoOpen = () => PcInfoOpen;                 // FUN_141924630: vfunc_55 == 0
            c.CloseCharacterInfo += () => { if (PcInfoOpen) pcInfo.Close(); };   // RequestCloseDialog(0x4ec8)
            c.ItemClicked += (id, t) => GD.Print($"popup menu: item {id} ({menuModel.Tables.PopupMenuName(id)}) for {t?.Name}: its handler is not ported");
            if (popupLayer == null) AddChild(popupLayer = new CanvasLayer { Layer = 11 });
            var m = new MovieDialog(assets, c, menuModel ??= NewModel(), GetWindow().Size);
            popupMenu = c; popupMovie = m;
            popupLayer.AddChild(m);
        }
        popupMenu.Field = SlotFieldData();
        popupMenu.TradeBook = Trades;                               // Trade (500): UIPopUpMenu::SendTradeRequest; live only
        popupMenu.Buddies = CommunityBook();                        // CBuddyManager: live, or the offline book
        popupMenu.OpenForPartySlot(x, y, characterId, hudModel.Player);
    }
}
