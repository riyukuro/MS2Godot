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

/// <summary>UICoupleActionDialog (uicoupleactiondialog.cpp) on its movie uicoupleaction (needs MS2_DATA): the list build
/// FUN_140becef0 / FUN_140bed2d0 / FUN_140bed8a0, the filters, the favorite toggle and the use on the selected character.</summary>
public class CoupleActionDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    const int Couple = CoupleActionDialogController.CoupleActionMainType;

    sealed class Rig
    {
        public readonly EmotionList List = new();
        public readonly Dictionary<uint, SkillData> Skills = new();
        public readonly Dictionary<uint, string> Names = new();
        public readonly Dictionary<uint, string[]> Reserved = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly HashSet<long> Field = new();
        public CoupleActionDialogController C;
        public Action Tick;

        /// <summary>Runs the close tween (startClose) to its end.</summary>
        public bool ClosedAfterTween() { for (int i = 0; i < 120 && !C.IsClosed; i++) Tick(); return C.IsClosed; }

        public void Skill(uint id, int mainType = Couple) => Skills[id] = new SkillData { Id = id, MainType = mainType, Icon = $"icon{id}.png" };
    }

    static void Data()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        src ??= new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        tables ??= new GameTables(packs, "en");
    }

    static Rig Load(Action<Rig> setup)
    {
        Data();
        var r = new Rig();
        setup(r);
        var c = r.C = new CoupleActionDialogController(r.List)
        {
            Skill = id => r.Skills.TryGetValue(id, out var s) ? s : null,
            ActionName = id => r.Names.TryGetValue(id, out var n) ? n : "",
            Reserved = id => r.Reserved.TryGetValue(id, out var k) ? k : null,
            HasCharacter = r.Field.Contains,
            Now = () => 1000,
            Send = r.Sent.Add,
        };
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        r.Tick = () => p.Tick();
        return r;
    }

    static EmoteEntry E(int id, bool perm = false, long sort = 0, bool fav = false, ushort lv = 1) => new(id, lv, perm, 0, sort, fav);

    static List<uint> Ids(CoupleActionDialogController c) =>
        c.Actions.OfType<Dictionary<string, object>>().Select(a => (uint)a["id"]).ToList();

    static double ListLength(CoupleActionDialogController c)
    {
        var h = c.Host;
        return Rt.ToNumber(h.Get(h.Get(h.Child("m_list"), "dataProvider"), "length"));
    }

    [Test]
    public void OnlyMainTypeFifteenIsListedWithoutKeyOrLock()
    {
        // FUN_140becef0: GetSkillData null or GetMainType() != 0xf is skipped; FUN_140bed2d0 pushes id, isNew, iconPath,
        // isFavorite (no key, no isLocked)
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2, mainType: 14);
            r.List.Apply(new EmoteMessage.List(new[] { E(1), E(2), E(3) }), 0);
        });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        var row = (Dictionary<string, object>)r.C.Actions[0];
        Assert.That(row.Keys, Is.EquivalentTo(new[] { "id", "isNew", "iconPath", "isFavorite" }));
        Assert.That(row["iconPath"], Is.EqualTo("icon1.png"));
        Assert.That(ListLength(r.C), Is.EqualTo(CoupleActionDialogController.GridMinimum));   // the movie got the padded array
    }

    [Test]
    public void PaddingFillsThirtyThenWholeRowsOfSix()
    {
        // PadEntriesToGrid (@0x140bee040): max(ceil(n / 6.0f) * 6, 0x1e)
        var l = new List<object>();
        CoupleActionDialogController.PadEntriesToGrid(l);
        Assert.That(l, Has.Count.EqualTo(30));
        l = Enumerable.Repeat<object>(1, 31).ToList();
        CoupleActionDialogController.PadEntriesToGrid(l);
        Assert.That(l, Has.Count.EqualTo(36));
        Assert.That(l.Skip(31), Is.All.Null);
    }

    [Test]
    public void TheDefaultOrderIsPermanentFirstAndOnSortReversesIt()
    {
        var r = Load(r =>
        {
            foreach (uint id in new uint[] { 10, 11, 12, 13 }) r.Skill(id);
            r.List.Apply(new EmoteMessage.List(new[] { E(12, sort: 5), E(11, perm: true), E(13, sort: 3), E(10, perm: true) }), 0);
        });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 10, 11, 13, 12 }));
        r.C.OnCallback("onSort", new object[] { true });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 12, 13, 11, 10 }));
        r.C.OnCallback("onFavoriteSort", new object[] { true });
        Assert.That(Ids(r.C), Is.Empty);
    }

    [Test]
    public void OnSelectEmotionUsesItOnTheSelectedCharacterThenCloses()
    {
        // vfunc_28 onSelectEmotion (:302-307) -> FUN_140bed1f0 (:405-420): the selected character must be in the field;
        // then vfunc_67 closes the dialog whether or not the emotion was used
        var r = Load(r =>
        {
            r.Skill(1);
            r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0);
        });
        var used = new List<uint>();
        r.C.UseSkill = used.Add;
        r.C.OnCallback("onSelectEmotion", new object[] { 0u });
        Assert.That(used, Is.Empty, "id 0: nothing");
        Assert.That(r.ClosedAfterTween(), Is.False, "id 0 returns before the close");
        r.C.SetSelectedCharacter(4002);
        r.C.OnCallback("onSelectEmotion", new object[] { 1u });
        Assert.That(used, Is.Empty, "the selected character is not in the field");
        Assert.That(r.ClosedAfterTween(), Is.True);
    }

    [Test]
    public void OnSelectEmotionUsesTheSkillWhenThePartnerIsThere()
    {
        var r = Load(r =>
        {
            r.Skill(1);
            r.Field.Add(4002);
            r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0);
        });
        var used = new List<uint>();
        bool player = true;
        r.C.UseSkill = used.Add;
        r.C.HasPlayer = () => player;
        r.C.SetSelectedCharacter(4002);
        Assert.That(r.C.SelectedCharacter, Is.EqualTo(4002));
        r.C.UseEmotion(9);
        Assert.That(used, Is.EqualTo(new uint[] { 9 }), "the list is not checked (the cast's own checks are)");
        player = false;
        r.C.UseEmotion(1);
        Assert.That(used, Is.EqualTo(new uint[] { 9 }));
        player = true;
        r.C.OnCallback("onSelectEmotion", new object[] { 1u });
        Assert.That(used, Is.EqualTo(new uint[] { 9, 1 }));
        Assert.That(r.ClosedAfterTween(), Is.True);
        Assert.That(r.Sent, Is.Empty, "the dialog itself sends nothing");
    }

    [Test]
    public void OnFavoriteSendsModeFour()
    {
        // vfunc_28 onFavorite (:321-338)
        var r = Load(r =>
        {
            r.Skill(1);
            r.List.Apply(new EmoteMessage.List(new[] { E(1, lv: 2) }), 0);
        });
        r.C.OnCallback("onFavorite", new object[] { 7u, true });
        Assert.That(r.Sent, Is.Empty);
        r.C.OnCallback("onFavorite", new object[] { 1u, true });
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new EmoteFavoriteRequest(1, 2, true) }));
        Assert.That(r.List.All.First(e => e.Id == 1).Entry.Favorite, Is.True);
    }

    [Test]
    public void SearchMatchesNameOrKeyword()
    {
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.Names[1] = "hug"; r.Names[2] = "bow";
            r.Reserved[2] = new[] { "/greet" };
            r.List.Apply(new EmoteMessage.List(new[] { E(1, sort: 1), E(2, sort: 2) }), 0);
        });
        r.C.OnCallback("onSearch", new object[] { "ug" });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1 }));
        r.C.OnCallback("onSearch", new object[] { "gre" });
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 2 }));
        r.C.OnCallback("onSearch", new object[] { "/" });
        Assert.That(Ids(r.C), Is.Empty);
        Assert.That(r.C.EmotionKeywords(2), Is.EqualTo("/greet"));
    }

    [Test]
    public void OpenedClearsTheSelectionAndFilters()
    {
        // vfunc_65 (@0x140bec0c0): m_178 = 0, m_180 cleared, m_1a8 emptied, m_1a0 = m_1a1 = false
        var r = Load(r => { r.Skill(1); r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0); });
        r.C.SetSelectedCharacter(4002);
        r.C.OnCallback("onSort", new object[] { true });
        r.C.OnCallback("onFavoriteSort", new object[] { true });
        r.C.SetSearchText("x");
        r.C.Opened();
        Assert.That(r.C.SelectedCharacter, Is.EqualTo(0));
        Assert.That(r.C.SortFlag || r.C.FavoriteOnly, Is.False);
        Assert.That(r.C.SearchText, Is.Empty);
        Assert.That(r.C.ListedCount, Is.EqualTo(0));
    }

    [Test]
    public void ALearnedCoupleEmotionRefreshesAndIsFocused()
    {
        var r = Load(r =>
        {
            r.Skill(1); r.Skill(2);
            r.List.Apply(new EmoteMessage.List(new[] { E(1) }), 0);
        });
        r.List.Apply(new EmoteMessage.Learn(E(2, sort: 9)), 0);
        Assert.That(Ids(r.C), Is.EqualTo(new uint[] { 1, 2 }));
        Assert.That(r.C.FocusId, Is.EqualTo(2u));
        r.C.Close();                                                        // vfunc_67: ClearNew
        Assert.That(r.List.All.All(e => !e.IsNew), Is.True);
    }
}
