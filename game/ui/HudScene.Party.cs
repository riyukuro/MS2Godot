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
        var book = partyBook;
        book.Opened += opened; book.Closed += closed; book.MemberAdded += added; book.MemberRemoved += removed;
        book.MemberUpdated += updated; book.MembersRefreshed += refreshed; book.ChiefChanged += chief;
        unwatchParty = () =>
        {
            book.Opened -= opened; book.Closed -= closed; book.MemberAdded -= added; book.MemberRemoved -= removed;
            book.MemberUpdated -= updated; book.MembersRefreshed -= refreshed; book.ChiefChanged -= chief;
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
        c.MemberMenu += (x, y, id) => GD.Print($"party: member menu for {id} at ({x}, {y}): the popup menu (invite, leave, kick, leader) is not ported");
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
}
