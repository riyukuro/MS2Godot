using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIDynamicActionDialog (uidynamicactiondialog.cpp) on its movie uidynamicaction (needs MS2_DATA): the list
/// build FUN_14192c6b0 / FUN_14192ca70 / FUN_14192d100, the filters and the favorite toggle.</summary>
public class DynamicActionDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    const int Emotion = DynamicActionDialogController.DynamicActionMainType;

    sealed class Rig
    {
        public readonly EmotionList List = new();
        public readonly Dictionary<uint, SkillData> Skills = new();
        public readonly Dictionary<uint, string> Names = new();
        public readonly Dictionary<uint, string[]> Reserved = new();
        public readonly List<IOutPacket> Sent = new();
        public DynamicActionDialogController C;
        public int Builds;

        public void Skill(uint id, int mainType = Emotion) => Skills[id] = new SkillData { Id = id, MainType = mainType, Icon = $"icon{id}.png" };
    }

    static void Data()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        src ??= TestData.Movies(packs);
        tables ??= new GameTables(packs, "en");
    }

    static Rig Load(Action<Rig> setup)
    {
        Data();
        var r = new Rig();
        setup(r);
        var c = r.C = new DynamicActionDialogController(r.List)
        {
            Skill = id => r.Skills.TryGetValue(id, out var s) ? s : null,
            ActionName = id => r.Names.TryGetValue(id, out var n) ? n : "",
            Reserved = id => r.Reserved.TryGetValue(id, out var k) ? k : null,
            ActionKeyName = _ => "",
            Now = () => 1000,
            Send = r.Sent.Add,
        };
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return r;
    }

    static EmoteEntry E(int id, bool perm = false, long sort = 0, bool fav = false, ushort lv = 1) => new(id, lv, perm, 0, sort, fav);

    static List<uint> Ids(DynamicActionDialogController c) =>
        c.Actions.OfType<Dictionary<string, object>>().Select(a => (uint)a["id"]).ToList();

    static int Cells(DynamicActionDialogController c) => c.Actions.Count;

    static double ListLength(DynamicActionDialogController c)
    {
        var h = c.Host;
        return Rt.ToNumber(h.Get(h.Get(h.Child("m_list"), "dataProvider"), "length"));
    }

    [Test]
    public void TheDefaultOrderIsPermanentFirstAndOnSortReversesIt()
    {
        // FUN_14192ca70 (Unk_EmotionGreater = the 0x141764410 body): permanent by id, then the others by sort time;
        // FUN_14192d100 (Unk_EmotionLess = the 0x141764570 body): the others by sort time descending, then permanent by id
        // descending
        var r = Load(r =>
        {
            foreach (uint id in new uint[] { 10, 11, 12, 13 }) r.Skill(id);
            r.List.Apply(new EmoteMessage.List(new[] { E(12, sort: 5), E(11, perm: true), E(13, sort: 3), E(10, perm: true) }), 0);
        });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 10, 11, 13, 12 }));
        Assert.That(ListLength(r.C), Is.EqualTo(DynamicActionDialogController.GridMinimum));     // the movie got the padded array
        r.C.OnCallback("onSort", new object[] { true });
        Assert.That(r.C.SortFlag, Is.True);
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 12, 13, 11, 10 }));
    }

    [Test]
    public void OnlyMainTypeFourteenWithSkillDataIsListed()
    {
        // FUN_14192c6b0: GetSkillData null or GetMainType() != 0xe is skipped
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2, mainType: 15);
            r.List.Apply(new EmoteMessage.List(new[] { E(1), E(2), E(3) }), 0);
        });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        Assert.That(r.C.ListedCount, Is.EqualTo(1));
        var row = (Dictionary<string, object>)r.C.Actions[0];
        Assert.That(row["iconPath"], Is.EqualTo("icon1.png"));
        Assert.That(row["isLocked"], Is.EqualTo(0));
        Assert.That(row["isNew"], Is.EqualTo(0));
    }

    [Test]
    public void PaddingFillsTwentyFourThenWholeRowsOfSix()
    {
        // PadEntriesToGrid (@0x14192d960): max(ceil(n / 6.0f) * 6, 0x18)
        var r = Load(r =>
        {
            var entries = new List<EmoteEntry>();
            for (uint id = 1; id <= 25; id++) { r.Skill(id); entries.Add(E((int)id, sort: id)); }
            r.List.Apply(new EmoteMessage.List(entries), 0);
        });
        Assert.That(Ids(r.C), Has.Count.EqualTo(25));
        Assert.That(Cells(r.C), Is.EqualTo(30));
        Assert.That(r.C.Actions.Skip(25), Is.All.Null);
        var l = new List<object>();
        DynamicActionDialogController.PadEntriesToGrid(l);
        Assert.That(l, Has.Count.EqualTo(24));
    }

    [Test]
    public void FavoriteSortKeepsOnlyFavorites()
    {
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1, fav: true), E(2) }), 0);
        });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1, 2 }));
        r.C.OnCallback("onFavoriteSort", new object[] { true });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        Assert.That(Cells(r.C), Is.EqualTo(24));
    }

    [Test]
    public void OnSelectEmotionUsesTheSkillUnlessInCubeModeOrWithoutThePlayer()
    {
        // vfunc_28 onSelectEmotion (uidynamicactiondialog.cpp:311-318) -> UseAction (:449-457) -> MyPC::FUN_141525480(id, 0)
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1), E(2) }), 0);
        });
        var used = new List<uint>();
        bool cube = false, player = true;
        r.C.UseSkill = used.Add;
        r.C.InCubeMode = () => cube;
        r.C.HasPlayer = () => player;
        r.C.OnCallback("onSelectEmotion", new object[] { 0u });
        Assert.That(used, Is.Empty, "id 0: nothing");
        r.C.OnCallback("onSelectEmotion", new object[] { 2u });
        Assert.That(used, Is.EqualTo(new uint[] { 2 }));
        r.C.OnCallback("onSelectEmotion", new object[] { 9u });
        Assert.That(used, Is.EqualTo(new uint[] { 2, 9 }), "UseAction does not check the list (the cast's own checks do)");
        cube = true;
        r.C.OnCallback("onSelectEmotion", new object[] { 1u });
        cube = false; player = false;
        r.C.OnCallback("onSelectEmotion", new object[] { 1u });
        Assert.That(used, Is.EqualTo(new uint[] { 2, 9 }));
        Assert.That(r.Sent, Is.Empty, "the dialog itself sends nothing");
    }

    [Test]
    public void OnShiftRClickPreviewsTheActionNotUsesIt()
    {
        // vfunc_28 onShiftRClick (uidynamicactiondialog.cpp:336-343) -> PreviewAction (:473-481): PopUInt, 0 ignored, no
        // list or player check here (the preview dialog's FUN_140e93e70 has its own)
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1), E(2) }), 0);
        });
        var previewed = new List<uint>();
        var used = new List<uint>();
        r.C.PreviewAction = previewed.Add;
        r.C.UseSkill = used.Add;
        r.C.OnCallback("onShiftRClick", new object[] { 0u });
        r.C.OnCallback("onShiftRClick", System.Array.Empty<object>());
        Assert.That(previewed, Is.Empty, "id 0 or no argument: nothing");
        r.C.OnCallback("onShiftRClick", new object[] { 2u });
        Assert.That(previewed, Is.EqualTo(new uint[] { 2 }));
        Assert.That(used, Is.Empty, "a preview casts nothing");
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void AShiftRightClickOnACellPreviewsItThroughTheMovie()
    {
        // the movie's OnRightClick sends onShiftRClick when the right click's shiftKey is set; the event carries the
        // keyboard state's Shift (GenerateMouseEvents, KeyboardState::GetKeyModifiers @0x14261ae30) through main.gfx's
        // right-button relay. The player used to leave shiftKey false, so no right click on a cell opened the preview.
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1), E(2) }), 0);
        });
        var previewed = new List<uint>();
        r.C.PreviewAction = previewed.Add;
        var h = r.C.Host;
        var p = h.Player;
        var cell = h.Invoke(h.Child("m_list"), "getRendererAt", 0) as AsDisplayObject;
        Assert.That(cell, Is.Not.Null, "the list's first renderer");
        // the cell has doubleClickEnabled, so a second release at the same pixel within DoubleClickMs is a doubleClick
        // (sfr_as3_17.cpp:7140), not a click: the test clock moves past that window between the two clicks
        double now = 1000;
        p.Clock = () => now;
        var (x, y) = cell.LocalToGlobal(10, 10);
        p.MouseMove(x, y);
        p.MouseButton(true, Player.RightButton);
        p.MouseButton(false, Player.RightButton);
        Assert.That(previewed, Is.Empty, "a right click without Shift");
        now += Player.DoubleClickMs + 1;
        p.Key(16, 0, true, shift: true);
        p.MouseButton(true, Player.RightButton);
        p.MouseButton(false, Player.RightButton);
        p.Key(16, 0, false);
        Assert.That(previewed, Has.Count.EqualTo(1), "shift + right click: " + string.Join("; ", p.RT.Errors));
        Assert.That(previewed[0], Is.EqualTo(((Dictionary<string, object>)r.C.Actions[0])["id"]));
    }

    [Test]
    public void OnFavoriteSetsTheEntrySendsModeFourAndRefreshesOnlyWhenFavoriteOnly()
    {
        // vfunc_28 onFavorite (uidynamicactiondialog.cpp:356-374)
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1, lv: 3), E(2) }), 0);
        });
        r.C.OnCallback("onFavorite", new object[] { 9u, true });          // not listed: nothing
        r.C.OnCallback("onFavorite", new object[] { 0u, true });          // id 0: nothing
        Assert.That(r.Sent, Is.Empty);
        r.C.OnCallback("onFavorite", new object[] { 1u, true });
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new EmoteFavoriteRequest(1, 3, true) }));
        Assert.That(r.List.All.First(e => e.Id == 1).Entry.Favorite, Is.True);       // set before any answer
        Assert.That(((Dictionary<string, object>)r.C.Actions[0])["isFavorite"], Is.False);   // m_1b1 off: no rebuild
        r.C.OnCallback("onFavoriteSort", new object[] { true });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        r.C.OnCallback("onFavorite", new object[] { 1u, false });
        Assert.That(r.Sent[^1], Is.EqualTo(new EmoteFavoriteRequest(1, 3, false)));
        Assert.That(Ids(r.C), Is.Empty);                                    // m_1b1 on: rebuilt
    }

    [Test]
    public void SearchMatchesNameOrKeywordAndSlashMatchesNothing()
    {
        // FUN_14192ca70's search: "/" skips all; else the name (CStringW::Find) or a word of GetActionKeywords
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2); r.Skill(3);
            r.Names[1] = "abc"; r.Names[2] = "xyz"; r.Names[3] = "qqq";
            r.Reserved[2] = new[] { "/wave", "/hi" };
            r.List.Apply(new EmoteMessage.List(new[] { E(1, sort: 1), E(2, sort: 2), E(3, sort: 3) }), 0);
        });
        r.C.OnCallback("onSearch", new object[] { "b" });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        r.C.OnCallback("onSearch", new object[] { "av" });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 2 }));
        r.C.OnCallback("onSearch", new object[] { "/h" });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 2 }));
        r.C.OnCallback("onSearch", new object[] { "/" });
        Assert.That(Ids(r.C), Is.Empty);
        Assert.That(Cells(r.C), Is.EqualTo(24));
        r.C.OnCallback("onSearch", new object[] { "B" });                  // case-sensitive Find
        Assert.That(Ids(r.C), Is.Empty);
        r.C.OnCallback("onSearch", new object[] { "" });
        Assert.That(Ids(r.C), Has.Count.EqualTo(3));
        Assert.That(r.C.ActionKeywords(2), Is.EqualTo("/wave /hi"));
        Assert.That(r.C.ActionKeywords(1), Is.EqualTo(""));
    }

    [Test]
    public void ASearchTextEqualWithoutCaseDoesNotRebuild()
    {
        // SetSearchText (@0x14192d790): CompareNoCase equal returns
        var r = Load(r =>
        {
            r.Skill(1); r.Names[1] = "abc";
            r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0);
        });
        r.C.SetSearchText("a");
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        r.C.SetSearchText("A");
        Assert.That(r.C.SearchText, Is.EqualTo("a"));
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
    }

    [Test]
    public void ALearnedEmotionRefreshesAndIsFocused()
    {
        // NotifyLearnEmotion -> FUN_14192c630 -> FUN_14192c6b0; a new one is focused (setFocusByEmotion)
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0);
        });
        Assert.That(r.C.FocusId, Is.EqualTo(0u));
        r.List.Apply(new EmoteMessage.Learn(E(2, sort: 9)), 0);
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(r.C.FocusId, Is.EqualTo(2u));
        Assert.That(((Dictionary<string, object>)r.C.Actions[1])["isNew"], Is.EqualTo(1));
        r.C.Close();                                                        // vfunc_67: ClearNew
        Assert.That(r.List.All.All(e => !e.IsNew), Is.True);
        Assert.That(r.C.ListedCount, Is.EqualTo(0));
    }

    [Test]
    public void OpenedResetsTheFilters()
    {
        // vfunc_65 (@0x14192ad70): m_1b8 emptied, m_1b0 = m_1b1 = false
        var r = Load(r => { r.Skill(1); r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0); });
        r.C.OnCallback("onSort", new object[] { true });
        r.C.OnCallback("onFavoriteSort", new object[] { true });
        r.C.SetSearchText("x");
        r.C.Opened();
        Assert.That(r.C.SortFlag || r.C.FavoriteOnly, Is.False);
        Assert.That(r.C.SearchText, Is.Empty);
    }

    [Test]
    public void DynamicActionRowsHaveSlashPrefixedReservedPaths()
    {
        // CDynamicActionData::Load (@0x140347180): each "|" part of "reserved" as "/part"; Find gives null for no row
        Data();
        string entry = packs.List("Xml").FirstOrDefault(n => n.StartsWith("string/", StringComparison.OrdinalIgnoreCase)
            && n.EndsWith("/kordynamicaction.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) Assert.Ignore("no kordynamicaction.xml in this data");
        var ids = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)))
            .Root!.Elements("key").Select(e => (string)e.Attribute("skillID")).Where(v => uint.TryParse(v, out _))
            .Select(uint.Parse).Distinct().ToList();
        Assert.That(ids, Is.Not.Empty);
        int rows = 0, paths = 0;
        foreach (uint id in ids)
        {
            var reserved = tables.DynamicActionReserved(id);
            if (reserved == null) continue;
            rows++;
            paths += reserved.Count;
            Assert.That(reserved, Is.All.StartsWith("/"));
            Assert.That(reserved, Is.All.Not.EqualTo("/"));
            Assert.That(reserved, Is.All.Not.Contains("|"));
        }
        Assert.That(rows, Is.GreaterThan(0));
        Assert.That(paths, Is.GreaterThan(0));
        Assert.That(tables.DynamicActionReserved(0), Is.Null);
    }
}
