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

    bool PartyWindowOpen => party != null && !party.IsClosed;

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
        Action<PartyMessage> applied = msg => popupMenu?.OnPartyMessage(msg);      // UIPopUpMenu's 0x53 handler
        var book = partyBook;
        book.Opened += opened; book.Closed += closed; book.MemberAdded += added; book.MemberRemoved += removed;
        book.MemberUpdated += updated; book.MembersRefreshed += refreshed; book.ChiefChanged += chief; book.Applied += applied;
        unwatchParty = () =>
        {
            book.Opened -= opened; book.Closed -= closed; book.MemberAdded -= added; book.MemberRemoved -= removed;
            book.MemberUpdated -= updated; book.MembersRefreshed -= refreshed; book.ChiefChanged -= chief; book.Applied -= applied;
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
        };
        c.SlotMenuOpen = () => popupMenu != null && !popupMenu.IsClosed && popupMenu.InfoString == PopupMenuController.PartySlotInfo;
        c.MemberMenu += (x, y, id) => OpenPartySlotMenu(x, y, id);
        c.ObserveMember += id => GD.Print($"party: observe {id}: StartObserving is not ported");
        c.Recall += () => GD.Print("party: recall: not ported");
        c.EnterDungeon += () => GD.Print("party: enter dungeon (SendRoomDungeon 0): not ported");
        c.CreateClub += () => GD.Print("party: create club: not ported");
        c.GiveUpDungeon += () => GD.Print("party: give up dungeon: not ported");
        c.Vote += yes => GD.Print($"party: vote {(yes ? "yes" : "no")}: not ported");
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

    /// <summary>The shot's slot menu (--uigen-party-menu ID): what PartyDialog.OnRightClickParty sends for a click at the slot's
    /// middle, onPopupMenu(x, y, charSN). The player has no right button dispatch, so the call goes to the controller.</summary>
    void ShotPartyMenu(long characterId)
    {
        if (!PartyWindowOpen || party.Host.Invoke(party.Host.Entity, "getMemberAni", characterId.ToString()) is not Ms2.Avm2.Flash.AsDisplayObject slot)
        {
            GD.PushWarning("party menu shot: no slot " + characterId);
            return;
        }
        var b = slot.GetBounds(null);
        party.OnCallback("onPopupMenu", new object[] { b.X + b.W / 2, b.Y + b.H / 2, characterId.ToString() });
    }

    /// <summary>A party slot's right click (UIPartyDialog::FUN_141a77760): the popup menu, its movie loaded on first use.</summary>
    void OpenPartySlotMenu(float x, float y, long characterId)
    {
        if (popupMenu == null)
        {
            var live = Ms2Godot.Net.LiveSession.Instance;
            // FUN_141028be0 with no download: a picture not in the download cache, or none, gives the default profile image
            string noProfile = (string)assets.Table("table/commonresource.xml")?.Root?.Element("DEFAULT_PROFILE_IMAGE_PATH")?.Attribute("value") ?? "";
            var c = new PopupMenuController
            {
                Party = partyBook,
                SelfCharacterId = live?.CharacterId ?? PartyFixture.SelfCharacterId,
                ProfileImagePath = (_, _) => noProfile,
                // STILL-OPEN: the field's other users are not kept (FUN_141bd1530), so no trade item
            };
            c.ItemClicked += (id, t) => GD.Print($"popup menu: item {id} ({menuModel.Tables.PopupMenuName(id)}) for {t?.Name}: its handler is not ported");
            if (popupLayer == null) AddChild(popupLayer = new CanvasLayer { Layer = 11 });
            var m = new MovieDialog(assets, c, menuModel ??= NewModel(), GetWindow().Size);
            popupMenu = c; popupMovie = m;
            popupLayer.AddChild(m);
        }
        popupMenu.Field = SlotFieldData();
        popupMenu.OpenForPartySlot(x, y, characterId, hudModel.Player);
    }
}
