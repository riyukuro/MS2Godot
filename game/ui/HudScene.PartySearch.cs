using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The party search listing: UIPartySearchDialog (0x4eda, movie uipartysearchdialog, game menu entry 25 "partysearch") and its
/// register window UIPartySearchRegisterDialog (0x4edb), over the party book's listing state (the PartySearch packets, 0xBB in
/// and 0x6E out). The book's notices and boxes are the common strings shown by CPartyManager (ShowSystemNotice and
/// ShowMessageBox); the register controller is kept, as its m_190 (the last title and check) outlives the window.
/// </summary>
public partial class HudScene
{
    PartySearchDialogController partySearch;
    MovieDialog partySearchMovie;
    PartySearchRegisterDialogController partySearchRegister;
    MovieDialog partySearchRegisterMovie;
    Action unwatchPartySearch;

    bool PartySearchOpen => partySearch != null && !partySearch.IsClosed;
    bool PartySearchRegisterOpen => partySearchRegister != null && !partySearchRegister.IsClosed;

    /// <summary>The party book's sends and UI calls for the listing; once per scene, after <see cref="WatchParty"/>.</summary>
    void WatchPartySearch()
    {
        if (partyBook == null || unwatchPartySearch != null) return;
        var book = partyBook;
        var t = (menuModel ??= NewModel()).Tables;
        book.Send = PartySend;
        Action<int, object[]> notice = (id, args) =>
            SystemNoticeText(args is { Length: > 0 } ? Ms2.Quest.ClientFormat.Format(t.Common(id), args) : t.Common(id));
        Action<int> box = id => MessageBoxQueue.Show(MessageBoxQueue.Create(0, t.Common(id), null, ""));
        Action<string> boxByKey = key => MessageBoxQueue.Show(MessageBoxQueue.Create(0, t.Common(key) ?? "", null, ""));
        book.Notice += notice; book.Box += box; book.BoxByKey += boxByKey;
        unwatchPartySearch = () =>
        {
            book.Notice -= notice; book.Box -= box; book.BoxByKey -= boxByKey;
            partySearch?.Detach();
            partySearchRegister?.Detach();
        };
    }

    void TogglePartySearch()
    {
        if (PartySearchOpen) partySearch.Close();
        else OpenPartySearch();
    }

    /// <summary>OpenDialog(0x4eda): the search window, one search sent after its delay (Tick).</summary>
    void OpenPartySearch()
    {
        if (GuideRefusesOpen("PartySearchDialog")) return;
        if (PartySearchOpen || partyBook == null) return;
        WatchPartySearch();
        if (partySearchMovie != null) { partySearchMovie.Host.Unload(); partySearchMovie.QueueFree(); partySearchMovie = null; }
        var t = (menuModel ??= NewModel()).Tables;
        var live = Ms2Godot.Net.LiveSession.Instance;
        partySearch?.Detach();
        var c = new PartySearchDialogController(partyBook)
        {
            Common = t.Common,
            PlayerCharId = () => live?.CharacterId ?? PartyFixture.SelfCharacterId,
            PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
            CheckSearchText = text => PartyTextChecker().CheckPartySearchText(text, out var msg) == 0 ? null : msg ?? "",
            Notice = id => SystemNoticeText(t.Common(id)),
            Whisper = ChatLog.Whisper,
            OpenRegister = OpenPartySearchRegister,
            ChiefMenu = (x, y, account, charId, name) => { EnsurePopupMenu().OpenForPartySearch(x, y, account, charId, name, hudModel.HasLocalPlayer ? hudModel.Player : null); },
            CloseRegister = () => { if (PartySearchRegisterOpen) partySearchRegister.Close(); },
        };
        var m = new MovieDialog(assets, c, menuModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (partySearchMovie == m) partySearchMovie = null;
        };
        partySearch = c; partySearchMovie = m;
        GD.Print($"party search: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>OpenDialog(0x4edb) from the search window's register button; the controller is made once and kept.</summary>
    void OpenPartySearchRegister()
    {
        if (PartySearchRegisterOpen || partyBook == null) return;
        var t = (menuModel ??= NewModel()).Tables;
        if (partySearchRegister == null)
        {
            var c = new PartySearchRegisterDialogController(partyBook)
            {
                ComboValues = t.PartySearchRegisterComboValues,
                Common = t.Common,
                CheckTitle = title => PartyTextChecker().CheckPartyRecruitTitle(title, out var msg) == 0 ? null : msg ?? "",
                SearchDialogOpen = () => PartySearchOpen,
            };
            c.Unloaded += () => { partySearchRegisterMovie?.Host.Unload(); partySearchRegisterMovie?.QueueFree(); partySearchRegisterMovie = null; };
            partySearchRegister = c;
        }
        if (partySearchRegisterMovie != null) { partySearchRegisterMovie.Host.Unload(); partySearchRegisterMovie.QueueFree(); }
        partySearchRegisterMovie = new MovieDialog(assets, partySearchRegister, menuModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(partySearchRegisterMovie);
    }

    /// <summary>--uigen-open partysearch[register]: the search window offline on a local book (sends logged), its delayed
    /// search answered with a fixture page of listings, for the gallery (no server).</summary>
    void OpenPartySearchShot()
    {
        partyBook ??= new PartyBook();
        OpenPartySearch();
        var page = new PartyRecruit[5];
        for (int i = 0; i < page.Length; i++)
            page[i] = new PartyRecruit
            {
                Id = 100 + i, PartyId = 10 + i, Title = "Fixture listing " + (i + 1), AutoJoin = i % 2 == 0,
                MemberCount = 1 + i % 4, MaxMembers = 4, LeaderAccountId = 200 + i, LeaderCharacterId = 300 + i,
                LeaderName = "Leader" + (i + 1), Created = 1_700_000_000 + i * 60,
            };
        partyBook.Apply(new PartySearchResults(page));
    }

    Ms2.Formats.Text.BanTextChecker PartyTextChecker() =>
        partyNameChecker ??= Ms2.Formats.Text.BanTextChecker.FromPacks(assets.Packs, assets.Language);

    /// <summary>The search window's vfunc_15 (the delayed search) each frame while it is open.</summary>
    void TickPartySearch()
    {
        if (PartySearchOpen) partySearch.Tick();
    }

    /// <summary>Escape closes the register window first, then the search window (UIDialog's vfunc_13); Return goes to the
    /// open one's onProcessKey.</summary>
    bool PartySearchKeys(InputEventKey k)
    {
        if (PartySearchRegisterOpen)
        {
            if (k.Keycode == Key.Escape) { partySearchRegister.Close(); return true; }
            if (k.Keycode is Key.Enter or Key.KpEnter or Key.Tab) { partySearchRegister.Key(k.Keycode != Key.Tab); return true; }
        }
        if (PartySearchOpen)
        {
            if (k.Keycode == Key.Escape) { partySearch.Close(); return true; }
            if (k.Keycode is Key.Enter or Key.KpEnter) return partySearch.EnterKey();
        }
        return false;
    }
}
