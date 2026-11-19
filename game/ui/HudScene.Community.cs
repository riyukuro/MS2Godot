using Godot;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The friend list: UICommunityDialog (0x4e4d, movie uicommunitydialog) and its query, UICommunityQueryDialog (0x4e4e, movie
/// uicommunityquerydialog: ask a friend, block, edit a memo), over CBuddyManager's entries. Live, the book is the session's (the
/// Buddy packets, 0x61) and the sends go out (Buddy 0x30, EventReward 0x97). Offline, an empty book, and the sends are logged.
/// Both C++ sides are kept between opens, as the client keeps its dialogs. STILL-OPEN: the popup (0x4e39), the home visit,
/// the home blocks (HomeBlockManager, the HomeBlock packet 0x138) and the VIP page are not in this HUD: their requests are
/// logged.
/// </summary>
public partial class HudScene
{
    CommunityController community;          // UICommunityDialog's C++ side, kept between opens
    MovieDialog communityMovie;             // its movie, from open until OnEndClose
    CommunityQueryController communityQuery;
    MovieDialog communityQueryMovie;
    static BuddyBook offlineBuddies;
    BanTextChecker communityChecker;
    bool buddiesWatched;
    int? communityFieldId;                  // this scene's field id, read once
    Action unwatchBuddies;

    bool CommunityOpen => community != null && !community.IsClosed;
    bool CommunityQueryOpen => communityQuery != null && !communityQuery.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e4d): open when closed, else vfunc_67.</summary>
    void ToggleCommunity()
    {
        if (!CommunityOpen) OpenCommunity();
        else community.Close();
    }

    BuddyBook CommunityBook() => Ms2Godot.Net.LiveSession.Instance is { } live ? live.Buddies : (offlineBuddies ??= new BuddyBook());

    BanTextChecker CommunityChecker() => communityChecker ??= BanTextChecker.FromPacks(assets.Packs, assets.Language);

    /// <summary>
    /// The book's UI calls (CBuddyManager::OnBuddy): the notices (Unk_AkoMessage with bLog as a chat line), the chat lines,
    /// the message boxes, the sounds, protocol 18's rebuild (the query closes, the list runs BuildBuddyList(GetTab())), the
    /// game menu's friend count (FUN_141938ae0(0x4e4d, 0)), and the open dialogs' vfunc_79 after the book (CNetworkService
    /// runs the scene's OnBuddy before CUIService::FUN_141adbfb0). Subscribed once per scene.
    /// </summary>
    void WatchBuddies()
    {
        if (buddiesWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        buddiesWatched = true;
        var book = live.Buddies;
        book.Common = hudModel.Tables.Common;
        void Notice(string text, int type, bool log)
        {
            if (string.IsNullOrEmpty(text) || SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        void Chat(string text, int kind) => ChatLog.AddSystemLine(text, kind);       // OnBuddy passes 12, TalkKind.Friend
        void Box(string text) => MessageBoxQueue.ShowMessageBox(text, 0, null, "");
        void Sound(string key) => FieldAudio.Instance?.PlayUi(key);
        void Rebuild()
        {
            if (CommunityQueryOpen) communityQuery.Close();
            if (CommunityOpen) community.BuildBuddyList(community.Tab);
        }
        void Count() => gameMenu?.UpdateNotifyCount(0x4e4d);
        void Packet(Ms2.Net.Packets.BuddyMessage m)
        {
            if (CommunityOpen) community.OnPacket(m);
            if (CommunityQueryOpen) communityQuery.OnPacket(m);
        }
        book.Notice += Notice;
        book.ChatLine += Chat;
        book.Box += Box;
        book.Sound += Sound;
        book.ListRebuild += Rebuild;
        book.MenuCount += Count;
        live.BuddyPacket += Packet;
        unwatchBuddies = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Box -= Box; book.Sound -= Sound; book.ListRebuild -= Rebuild;
            book.MenuCount -= Count; live.BuddyPacket -= Packet;
            buddiesWatched = false;
        };
    }

    /// <summary>The friend dialogs' sends: out live, logged offline.</summary>
    void SendBuddy(Ms2.Net.Wire.IOutPacket packet)
    {
        if (Ms2Godot.Net.LiveSession.Instance is { } s) s.SendBuddy(packet);
        else GD.Print($"community: offline, not sent: {packet}");
    }

    /// <summary>The field's users as the nearby tab reads them (FUN_1418d8540); the own character is one of them. Offline,
    /// the HUD's player alone. STILL-OPEN: the address (CAddressTable) is not ported, so "".</summary>
    IEnumerable<NearbyPlayer> NearbyPlayers()
    {
        if (Ms2Godot.Net.LiveSession.Instance is { } live)
            return live.FieldUsers().Select(u => new NearbyPlayer(u.CharacterId, u.Name ?? "", u.Picture ?? "",
                u.Level, u.Job, "", u.Motto ?? "", (u.AchievementCombat, u.AchievementAdventure, u.AchievementLifestyle)));
        if (!hudModel.HasLocalPlayer) return Enumerable.Empty<NearbyPlayer>();
        var p = hudModel.Player;
        return new[] { new NearbyPlayer(0, p.Name ?? "", "", p.Level, p.SubJob, "", "", (0, 0, 0)) };
    }

    /// <summary>
    /// Opens the friend list (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with UICommunityDialog's C++ side
    /// (CommunityController). A dialog that is open is left; the movie is dropped when its close tween ends.
    /// </summary>
    void OpenCommunity()
    {
        if (GuideRefusesOpen("CommunityDialog")) return;
        if (CommunityOpen) return;
        if (communityMovie != null) { communityMovie.Host.Unload(); communityMovie.QueueFree(); communityMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var book = CommunityBook();
        book.Common = hudModel.Tables.Common;
        if (community == null || community.Book != book)
        {
            var t = hudModel.Tables;
            var c = new CommunityController(book)
            {
                Common = t.Common,
                MapName = id => t.MapName(id) ?? "",
                JobName = job => t.JobName(job),
                ProfileImagePath = (id, picture) => Ms2Godot.Net.ProfileImageService.Images.Path(id, picture, true),
                SelfCharacterId = () => hudModel.HasLocalPlayer ? Ms2Godot.Net.LiveSession.Instance?.CharacterId : null,
                IsPartyMember = id => Ms2Godot.Net.LiveSession.Instance?.Party.Find(id) != null,
                FieldId = () => communityFieldId ??= FieldData.FindByXBlock(assets.Packs, xblock)?.Id ?? 0,   // GetFieldId
                NearbyPlayers = NearbyPlayers,
                Checker = CommunityChecker(),
                FeatureEnabled = hudModel.IsFeatureEnabled,
                Send = SendBuddy,
            };
            c.QueryRequested += (a, b) => CommunityQuery().OnQuery(a, b);
            c.PopupRequested += (x, y, id, tab) => GD.Print($"community: popup for {id} on tab {tab} (0x4e39 is not in this HUD)");
            c.HomeVisitRequested += (id, kind, name) => GD.Print($"community: home visit {name} (not in this HUD)");
            c.VipPageRequested += () => GD.Print("community: the VIP page (0x4e6a) is not in this HUD");
            c.HomeBlockMemoEdited += (id, memo) => GD.Print($"community: home block memo {id} (HomeBlockManager is not ported)");
            c.Unloaded += () => { communityMovie?.Host.Unload(); communityMovie?.QueueFree(); communityMovie = null; };
            community = c;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        communityMovie = new MovieDialog(assets, community, hudModel, GetWindow().Size);
        GD.Print($"community: load={communityMovie.Loaded} in {sw.ElapsedMilliseconds} ms ({book.Count} entries)");
        uiRoot.AddChild(communityMovie);
    }

    /// <summary>The query dialog's C++ side (made once; it opens itself through OpenRequested).</summary>
    CommunityQueryController CommunityQuery()
    {
        var book = CommunityBook();
        if (communityQuery != null && communityQuery.Book == book) return communityQuery;
        var q = new CommunityQueryController(book)
        {
            Common = hudModel.Tables.Common,
            Checker = CommunityChecker(),
            SelfName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
            Send = SendBuddy,
        };
        q.SystemNotice += text => SystemNoticeText(text);
        q.Sound += key => FieldAudio.Instance?.PlayUi(key);
        q.OpenRequested += OpenCommunityQuery;
        q.HomeBlockRequested += (id, memo) => GD.Print($"community: home block {id} (HomeBlockManager is not ported)");
        q.HomeBlockMemoEdited += (id, memo) => GD.Print($"community: home block memo {id} (HomeBlockManager is not ported)");
        q.HomeBlockRemoveRequested += id => GD.Print($"community: home block remove {id} (HomeBlockManager is not ported)");
        q.Unloaded += () => { communityQueryMovie?.Host.Unload(); communityQueryMovie?.QueueFree(); communityQueryMovie = null; };
        return communityQuery = q;
    }

    /// <summary>OpenDialog(0x4e4e) from the query's OnMessage: its movie with UICommunityQueryDialog's C++ side.</summary>
    void OpenCommunityQuery()
    {
        if (GuideRefusesOpen("CommunityQueryDialog")) return;
        if (CommunityQueryOpen) return;
        if (communityQueryMovie != null) { communityQueryMovie.Host.Unload(); communityQueryMovie.QueueFree(); communityQueryMovie = null; }
        communityQueryMovie = new MovieDialog(assets, communityQuery, hudModel, GetWindow().Size);
        GD.Print($"community: query load={communityQueryMovie.Loaded} (mode {communityQuery.Mode})");
        uiRoot.AddChild(communityQueryMovie);
    }

    /// <summary>vfunc_72 of the friend list each frame.</summary>
    void CommunityTick()
    {
        if (CommunityOpen) community.Process();
    }

    /// <summary>
    /// The friend dialogs' vfunc_13: UIDialog's first (Escape closes), then the query's Enter (onEnterKey) and Tab
    /// (onTabKey), and the list's Enter while a block memo is edited (endEditBanMemo). The query is modal, so it is asked first.
    /// </summary>
    bool CommunityKeys(InputEventKey k)
    {
        bool enter = k.Keycode is Key.Enter or Key.KpEnter;
        if (CommunityQueryOpen)
        {
            if (k.Keycode == Key.Escape) { communityQuery.Close(); return true; }
            if (communityQuery.OnKey(enter, k.Keycode == Key.Tab)) return true;
        }
        if (CommunityOpen)
        {
            if (k.Keycode == Key.Escape) { community.Close(); return true; }
            if (enter && community.OnKey(true)) return true;
        }
        return false;
    }
}
