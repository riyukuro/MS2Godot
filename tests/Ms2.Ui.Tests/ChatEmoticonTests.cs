using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.ChatEmoticons;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The chat emoticons (needs MS2_DATA): chatemoticontable.cpp, CChatEmoticonCli (DispatchPacket @0x140a3a700),
/// UIChatEmoticonDialog (uichatemoticondialog.cpp) and the messenger's emoticon functions (uimessenger.cpp:1265-1500).
/// Only ids, counts and structure are compared, never a language string.</summary>
public class ChatEmoticonTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static KeyTable keys;
    readonly List<IOutPacket> sent = new();
    readonly List<string> errors = new();

    static void Data()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        tables = new GameTables(packs, "en");
        keys = new KeyTable(packs, "en");
    }

    static ChatEmoticonTable Table() { Data(); return tables.ChatEmoticonData; }

    /// <summary>A pack with several emoticons.</summary>
    static uint BigGroup(ChatEmoticonTable t) =>
        t.Emoticons.GroupBy(e => e.GroupId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

    static ChatEmoticonState State(long now = 1000)
    {
        var s = new ChatEmoticonState(Table()) { Now = () => now };
        return s;
    }

    [SetUp]
    public void Reset() { sent.Clear(); errors.Clear(); }

    // ---- the table ----

    [Test]
    public void AnEmoticonUrlLoadsItsTablesImage()
    {
        // GFxImageLoader::Protocol::Parse (gfximageloader.cpp:78-122): "img://" removed, the host before ":" and for
        // "emoticon" the rest, type 5; FUN_140a59830 (asyncimageloader.cpp:356-366): ParseUInt of it, the emoticon's image_path
        var t = Table();
        var e = t.Emoticons.First(x => x.ImagePath.Length > 0);
        Assert.That(t.ImagePathOfUrl($"img://emoticon:{e.Id}"), Is.EqualTo(e.ImagePath));
        Assert.That(t.ImagePathOfUrl($"emoticon:{e.Id}"), Is.EqualTo(e.ImagePath), "the loader's url without the scheme");
        Assert.That(t.ImagePathOfUrl($"img://emoticon:{e.Id}x"), Is.EqualTo(e.ImagePath), "ParseUInt takes the leading digits");
        Assert.That(t.ImagePathOfUrl("img://emoticon:"), Is.Null, "no path: Parse fails");
        Assert.That(t.ImagePathOfUrl("img://emoticon:4294967295"), Is.Null, "an id the table does not have: no file");
        Assert.That(t.ImagePathOfUrl($"img://icon:{e.Id}"), Is.Null, "another host");
        Assert.That(t.ImagePathOfUrl(e.ImagePath), Is.Null, "a plain path");
    }

    /// <summary>The data sweep: every emoticon's chat IMG url finds its image file in the image archive, on each data set given
    /// (MS2_DATA, MS2_GMS2_DATA), with the path as the loader reads it.</summary>
    [Test]
    public void EveryEmoticonsChatImageIsInTheArchive()
    {
        Data();
        int sets = 0;
        foreach (var (label, dir) in new[] { ("MS2_DATA", Environment.GetEnvironmentVariable("MS2_DATA")), ("MS2_GMS2_DATA", Environment.GetEnvironmentVariable("MS2_GMS2_DATA")) })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { TestContext.WriteLine($"{label}: not set"); continue; }
            var p = label == "MS2_DATA" ? packs : new PackSet(dir);
            var t = label == "MS2_DATA" ? tables.ChatEmoticonData : new GameTables(p, "en").ChatEmoticonData;
            var all = t.Emoticons.ToList();
            Assert.That(all, Is.Not.Empty, label);
            var missing = all.Where(e => t.ImagePathOfUrl($"img://emoticon:{e.Id}") is not { } path
                || !p.Exists("Resource/Image", ChatEmoticonTable.ResourceImageEntry(path))).Select(e => $"{e.Id}:{e.ImagePath}").ToList();
            // a tab with emoticons must show its icon; a tab with none (the data's development category) is only listed
            var used = all.Select(e => e.CategoryId).ToHashSet();
            var noIcon = t.Categories.Where(c => !p.Exists("Resource/Image", ChatEmoticonTable.ResourceImageEntry(c.IconPath))).ToList();
            var cats = noIcon.Where(c => used.Contains(c.Id)).Select(c => $"{c.Id}:{c.IconPath}").ToList();
            TestContext.WriteLine($"{label}: {all.Count} emoticons in {all.Select(e => e.GroupId).Distinct().Count()} packs, {missing.Count} without their image: {string.Join(", ", missing.Take(10))}; " +
                $"{t.Categories.Count()} tabs, {cats.Count} with emoticons and without their icon: {string.Join(", ", cats.Take(10))}; " +
                $"empty tabs without an icon: {string.Join(", ", noIcon.Where(c => !used.Contains(c.Id)).Select(c => $"{c.Id}:{c.IconPath}"))}");
            Assert.That(missing, Is.Empty, label + ": emoticon images");
            Assert.That(cats, Is.Empty, label + ": tab icons");
            sets++;
        }
        Assert.That(sets, Is.GreaterThan(0));
    }

    [Test]
    public void TheTableHasEmoticonsInKnownCategories()
    {
        var t = Table();
        Assert.That(t.Emoticons, Is.Not.Empty);
        Assert.That(t.Categories, Is.Not.Empty);
        Assert.That(t.InvalidCategory(), Is.Empty);
        foreach (var e in t.Emoticons)
        {
            Assert.That(t.GetEmoticon(e.Id), Is.SameAs(e));
            Assert.That(t.GetEmoticonsByGroup(e.GroupId), Does.Contain(e));
            if (e.Command.Length > 0) Assert.That(e.Command, Does.StartWith(":").And.EndWith(":"));
        }
        Assert.That(t.GetEmoticon(0xffffffff), Is.Null);
        Assert.That(t.GetEmoticonsByGroup(0xffffffff), Is.Empty);
    }

    // ---- the state ----

    [Test]
    public void AnEmoticonIsUsableOnlyWithItsPackOwnedAndNotExpired()
    {
        var t = Table();
        uint g = BigGroup(t);
        var e = t.GetEmoticonsByGroup(g)[0];
        var s = State(1000);
        Assert.That(s.IsEmoticonUsable(e.Id), Is.False);          // not owned
        Assert.That(s.IsGroupExpired(g), Is.True);                // an unowned pack is expired
        s.AddGroup(g, 1000);
        Assert.That(s.IsEmoticonUsable(e.Id), Is.True);           // expiry == now: not passed
        Assert.That(s.IsGroupExpired(g), Is.False);
        s.AddGroup(g, 999);
        Assert.That(s.IsEmoticonUsable(e.Id), Is.False);
        Assert.That(s.Groups, Has.Count.EqualTo(1));              // AddGroup removes first
        Assert.That(s.IsEmoticonUsable(0xffffffff), Is.False);
        Assert.That(s.ListRequest().ExpiredGroups, Is.EqualTo(new[] { g }));     // now > expiry
        s.AddGroup(g, 1000);
        Assert.That(s.ListRequest().ExpiredGroups, Is.Empty);
    }

    [Test]
    public void TheListReplacesTheStateAndClearsThenRefreshesTheDialog()
    {
        var s = State();
        var log = new List<string>();
        s.Clear += () => log.Add("clear"); s.Refresh += () => log.Add("refresh");
        s.AddGroup(99, 5);
        var e = Table().Emoticons.First();
        s.Apply(new ChatStampMessage.List(new[] { e.Id }, new[] { new ChatEmoticonGroup(e.GroupId, 2000) }));
        Assert.That(s.Favorites, Is.EqualTo(new[] { e.Id }));
        Assert.That(s.Groups, Is.EqualTo(new[] { new ChatEmoticonGroup(e.GroupId, 2000) }));
        Assert.That(log, Is.EqualTo(new[] { "clear", "refresh" }));
    }

    [Test]
    public void ExpiredRemovesPacksAndFavoritesThenShowsTheBox()
    {
        var t = Table();
        var e = t.Emoticons.First();
        var s = State();
        s.Apply(new ChatStampMessage.List(new[] { e.Id }, new[] { new ChatEmoticonGroup(e.GroupId, 2000) }));
        var boxes = new List<string>();
        s.MessageBox += boxes.Add;
        s.Apply(new ChatStampMessage.Expired(new[] { e.GroupId }, new[] { e.Id }));
        Assert.That(s.Groups, Is.Empty);
        Assert.That(s.Favorites, Is.Empty);
        Assert.That(boxes, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddedAppendsPacksMarksThemNewAndTellsTheItem()
    {
        var s = State();
        var news = new List<uint>(); int item = 0, refreshes = 0;
        s.NewGroup += news.Add; s.Added += i => item = i; s.Refresh += () => refreshes++;
        s.Apply(new ChatStampMessage.Added(123, new[] { new ChatEmoticonGroup(7, 5000), new ChatEmoticonGroup(8, 6000) }));
        Assert.That(s.Groups.Select(g => g.Id), Is.EqualTo(new uint[] { 7, 8 }));
        Assert.That(news, Is.EqualTo(new uint[] { 7, 8 }));
        Assert.That(item, Is.EqualTo(123));
        Assert.That(refreshes, Is.EqualTo(1));
    }

    [Test]
    public void AddedTellsEachNewPackOnceInAscendingOrder()
    {
        // FUN_140a39360 collects the ids in a std::set first: ascending, no duplicates.
        var s = State();
        var news = new List<uint>();
        s.NewGroup += news.Add;
        s.Apply(new ChatStampMessage.Added(1, new[] { new ChatEmoticonGroup(9, 5000), new ChatEmoticonGroup(7, 5000), new ChatEmoticonGroup(9, 6000) }));
        Assert.That(news, Is.EqualTo(new uint[] { 7, 9 }));
    }

    [Test]
    public void LinkTakesTheSceneHandlersOffSoAReloadAddsNoSecondBox()
    {
        // The state outlives the HUD scene (a field change reloads it): hook, unhook, hook again = one box per packet.
        var s = State();
        int boxes = 0, typed = 0;
        for (int scene = 0; scene < 3; scene++)
        {
            var link = new Ms2.Ui.ChatEmoticons.ChatEmoticonLink(s, _ => boxes++, (w, f, id) => typed++);
            link.Hook();
            link.Hook();                      // a repeated Hook does not add twice either
            link.Unhook();
            link.Hook();
            if (scene < 2) link.Unhook();     // the last scene stays hooked
        }
        s.Apply(new ChatStampMessage.Result(0));
        s.Apply(new ChatStampMessage.Typed(9, "win", 3));
        Assert.That(boxes, Is.EqualTo(1));
        Assert.That(typed, Is.EqualTo(1));
    }

    [Test]
    public void FavoritesMoveToTheEndAndAreRemoved()
    {
        var s = State();
        s.Apply(new ChatStampMessage.List(new uint[] { 1, 2, 3 }, Array.Empty<ChatEmoticonGroup>()));
        s.Apply(new ChatStampMessage.MovedToEnd(1));
        Assert.That(s.Favorites, Is.EqualTo(new uint[] { 2, 3, 1 }));
        s.Apply(new ChatStampMessage.Removed(3));
        Assert.That(s.Favorites, Is.EqualTo(new uint[] { 2, 1 }));
    }

    [Test]
    public void TypedAndGroupSentReachTheNamedWindow()
    {
        var s = State();
        (string, byte, uint) typed = default; (string, uint) sentImg = default;
        s.TypeInWindow += (w, f, id) => typed = (w, f, id);
        s.SendImageInWindow += (w, id) => sentImg = (w, id);
        s.Apply(new ChatStampMessage.Typed(9, "win", 3));
        s.Apply(new ChatStampMessage.GroupSent(4, "grp"));
        Assert.That(typed, Is.EqualTo(("win", (byte)3, 9u)));
        Assert.That(sentImg, Is.EqualTo(("grp", 4u)));
    }

    [Test]
    public void EachResultCodeShowsItsBox()
    {
        var s = State();
        var boxes = new List<string>();
        s.MessageBox += boxes.Add;
        for (byte code = 0; code < 6; code++) s.Apply(new ChatStampMessage.Result(code));
        Assert.That(boxes, Has.Count.EqualTo(5));                 // codes 0-4 each show a box, 5 none
    }

    // ---- the dialog ----

    ChatEmoticonDialogController Dialog(ChatEmoticonState s, out List<(string, uint)> picks, out List<ChatStickerListRequest> lists)
    {
        Data();
        var p2 = new List<(string, uint)>(); var l2 = new List<ChatStickerListRequest>();
        var c = new ChatEmoticonDialogController(s)
        {
            CurrentMessenger = () => "win",
            Selected = (n, id) => p2.Add((n, id)),
            CommonText = id => $"c{id:x} {{0}}/{{1}}/{{2}} {{3}}:{{4}}",
            Send = l2.Add,
        };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        errors.AddRange(rt.Errors);
        picks = p2; lists = l2;
        return c;
    }

    [Test]
    public void TheDialogRequestsTheListAndBuildsTabsAndItems()
    {
        var t = Table();
        uint g = BigGroup(t);
        var s = State();
        s.AddGroup(g, 5000);
        var fav = t.GetEmoticonsByGroup(g)[0];
        s.Apply(new ChatStampMessage.List(new[] { fav.Id }, new[] { new ChatEmoticonGroup(g, 5000) }));
        var c = Dialog(s, out _, out var lists);
        Assert.That(lists, Has.Count.EqualTo(1));
        Assert.That(c.SelectedTab, Is.EqualTo(ChatEmoticonDialogController.FavoritesTab));
        var cats = t.GetEmoticonsByGroup(g).Select(e => (int)e.CategoryId).Distinct().ToList();
        Assert.That(c.Tabs, Is.EqualTo(cats));
        Assert.That(c.TabData, Has.Count.EqualTo(cats.Count + 1));             // the favorites tab first
        Assert.That(c.ItemData, Has.Count.EqualTo(1));                         // the favorites tab lists the favorite
        Assert.That(((Dictionary<string, object>)c.ItemData[0])["id"], Is.EqualTo(fav.Id));
        Assert.That(((Dictionary<string, object>)c.ItemData[0])["enable"], Is.EqualTo(true));
        Assert.That(c.NextExpire, Is.EqualTo(5000));
        c.SelectTab(cats[0]);
        Assert.That(c.SelectedTab, Is.EqualTo(cats[0]));
        Assert.That(c.ItemData, Has.Count.EqualTo(t.GetEmoticonsByGroup(g).Count(e => (int)e.CategoryId == cats[0])));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ANewPackMarksItsTabAndItemsUntilCleared()
    {
        var t = Table();
        uint g = BigGroup(t);
        var s = State();
        var c = Dialog(s, out _, out _);
        s.Apply(new ChatStampMessage.Added(1, new[] { new ChatEmoticonGroup(g, 5000) }));
        int cat = (int)t.GetEmoticonsByGroup(g)[0].CategoryId;
        Assert.That(c.IsNewTab(cat), Is.True);
        Assert.That(c.TabEmoticons(cat).All(x => x.New), Is.True);
        c.ClearNewGroups(); c.Rebuild();
        Assert.That(c.IsNewTab(cat), Is.False);
    }

    [Test]
    public void AnExpiredPackShowsDisabledAndTheShopTabKeepsTheTab()
    {
        var t = Table();
        uint g = BigGroup(t);
        var s = State(1000);
        s.AddGroup(g, 10);
        var c = Dialog(s, out var picks, out _);
        int cat = (int)t.GetEmoticonsByGroup(g)[0].CategoryId;
        c.SelectTab(cat);
        Assert.That(c.ItemData.Cast<Dictionary<string, object>>().All(d => (bool)d["enable"] == false), Is.True);
        c.SelectTab(ChatEmoticonDialogController.ShopTab);
        Assert.That(c.SelectedTab, Is.EqualTo(cat));
        c.OnCallback("onSelectEmoticon", new object[] { t.GetEmoticonsByGroup(g)[0].Id });
        Assert.That(picks, Is.EqualTo(new[] { ("win", t.GetEmoticonsByGroup(g)[0].Id) }));
        c.OnCallback("onSelectEmoticon", new object[] { 0u });
        Assert.That(picks, Has.Count.EqualTo(1));                              // id 0 posts nothing
    }

    // ---- the messenger ----

    MessengerController Messenger(ChatEmoticonState s, out MessengerBrowser log)
    {
        Data();
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.HasLocalPlayer = true;
        var l = log = new MessengerBrowser { Model = m, ChatEmoticons = s };
        l.SendSticker = sent.Add;
        l.IsEmoticonUsable = id => id > 0 && s.IsEmoticonUsable((uint)id);
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, l);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return c;
    }

    [Test]
    public void SendingAnEmoticonNeedsItUsableAndNamesTheWindow()
    {
        var t = Table();
        var e = t.Emoticons.First();
        var s = State();
        var c = Messenger(s, out _);
        Assert.That(c.SendEmoticon((int)e.Id), Is.False);                      // pack not owned: nothing goes out
        Assert.That(c.SendEmoticon(-1), Is.False);
        Assert.That(sent, Is.Empty);
        s.AddGroup(e.GroupId, 5000);
        Assert.That(c.SendEmoticon((int)e.Id), Is.True);
        var req = (ChatStickerRequest)sent.Single();
        Assert.That(req.EmoticonId, Is.EqualTo((int)e.Id));
        Assert.That(req.Window, Is.EqualTo(c.Name));
    }

    [Test]
    public void AFavoriteChangeIsSentOnlyForAUsableEmoticon()
    {
        var e = Table().Emoticons.First();
        var s = State();
        var c = Messenger(s, out _);
        Assert.That(c.SetEmoticonFavorite((int)e.Id, true), Is.False);
        Assert.That(sent, Is.Empty);
        s.AddGroup(e.GroupId, 5000);
        Assert.That(c.SetEmoticonFavorite((int)e.Id, false), Is.True);
        Assert.That(sent.Single(), Is.EqualTo(ChatStickerRequest.SetFavorite((int)e.Id, false)));
    }

    [Test]
    public void TheCallbacksReachTheMessengerFunctions()
    {
        var e = Table().Emoticons.First();
        var s = State();
        s.AddGroup(e.GroupId, 5000);
        var c = Messenger(s, out var log);
        int toggles = 0; log.ToggleEmoticonDialog = () => toggles++;
        Assert.That(c.OnCallback("eventChatEmoticonOpen", Array.Empty<object>()), Is.False);
        Assert.That(toggles, Is.EqualTo(1));
        c.OnCallback("eventChatEmoticonPush", new object[] { e.Id });
        Assert.That(sent.OfType<ChatStickerRequest>().Count(r => r.Mode == ChatStickerRequest.Use), Is.EqualTo(1));
        c.OnCallback("eventChatEmoticonFavorite", new object[] { e.Id, true });
        Assert.That(sent.OfType<ChatStickerRequest>().Count(r => r.Mode == ChatStickerRequest.Favorite), Is.EqualTo(1));
    }

    [Test]
    public void TypingAnEmoticonSubmitsItsImageLine()
    {
        var e = Table().Emoticons.First();
        var s = State();
        var c = Messenger(s, out var log);
        var lines = new List<UserChatRequest>();
        log.SendPacket = lines.Add;
        c.TypeEmoticon(0, e.Id);
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0].Message, Does.Contain($"img://emoticon:{e.Id}"));
        Assert.That(lines[0].Message, Does.Contain($"vspace='{e.VSpace}'"));
        Assert.That(lines[0].Message.Contains("width=") , Is.EqualTo(e.Width != 0 || e.Height != 0));
    }
}
