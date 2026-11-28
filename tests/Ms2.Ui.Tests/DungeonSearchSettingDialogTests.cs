using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The dungeon finder's room picker on its movie (needs MS2_DATA): the rows the groups list, their colours, the selection, the
/// group dropdown and the OK button (uidungeonsearchsettingdialog.cpp). No test asserts a language-specific string.</summary>
public class DungeonSearchSettingDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost Host, Player Player) Open(UiController c)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        src ??= TestData.Movies(packs);
        tables ??= new GameTables(packs, "en");
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
        return (h, p);
    }

    static void EnsureTables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        tables ??= new GameTables(packs, "en");
    }

    /// <summary>A user pick: the control's row, then its "listIndexChange" (the dropdown raises it on a pick, not on a script set).</summary>
    static void Pick(UiHost h, Player p, object target, int index)
    {
        h.Set(target, "selectedIndex", index);
        h.Invoke(target, "dispatchEvent", h.RT.Construct(h.RT.GetClass("flash.events.Event"), new object[] { "listIndexChange" }));
        for (int i = 0; i < 3; i++) p.Tick();
    }

    sealed class Env
    {
        public HashSet<uint> Enterable = new();
        public bool Player = true;
        public List<uint> Applied;
    }

    static (DungeonSearchSettingController C, UiHost H, Player P) Dialog(Env e, IEnumerable<uint> selected = null)
    {
        var c = new DungeonSearchSettingController
        {
            RoomTable = () => tables?.DungeonRooms,
            CanEnterRoom = id => e.Enterable.Contains(id),
            HasLocalPlayer = () => e.Player,
            ApplyBookmarks = ids => e.Applied = ids.ToList(),
        };
        EnsureTables();
        if (selected != null) c.SetSelected(selected);
        var (h, p) = Open(c);
        return (c, h, p);
    }

    static List<uint> GroupIds(params int[] groups)
    {
        EnsureTables();
        var ids = new List<uint>();
        foreach (int g in groups) ids.AddRange(tables.DungeonRooms.GetGroupRooms(g).Select(r => r.Id));
        return ids;
    }

    static object Row(UiHost h, int row) => h.Child("m_listItem" + (row + 1), h.Child("m_customPage"));
    static object Check(UiHost h, int row) => h.Child("m_checkBox", Row(h, row));
    static bool Shown(UiHost h, int row) => Rt.ToBoolean(h.Get(Row(h, row), "visible"));

    [Test]
    public void TheOpenListsTheRoomsOfBothGroupsInTheTablesOrder()
    {
        var (c, h, p) = Dialog(new Env());
        Assert.That(c.Group, Is.Zero);
        Assert.That(c.Rooms.Select(r => r.Id), Is.EqualTo(GroupIds(1, 2)), "SelectGroup(0): group 1 then group 2");
        int shown = Math.Min(c.Rooms.Count, DungeonSearchSettingController.ItemCount);
        for (int i = 0; i < shown; i++) Assert.That(Shown(h, i), Is.True, "row " + i);
    }

    [Test]
    public void ARowIsWhiteWhereTheRoomCanBeEnteredAndRedWhereItCannot()
    {
        var env = new Env();
        var ids = GroupIds(1, 2);
        if (ids.Count < 2) Assert.Ignore("fewer than two rooms in the groups");
        env.Enterable.Add(ids[0]);
        var (c, h, p) = Dialog(env);
        string a = Rt.ToString(h.Get(Check(h, 0), "label")), b = Rt.ToString(h.Get(Check(h, 1), "label"));
        Assert.That(a, Does.Contain("#ffffff"));
        Assert.That(b, Does.Contain("#ff0000"));
        Assert.That(Rt.ToBoolean(h.Get(Check(h, 0), "isHtml")), Is.True);
    }

    [Test]
    public void TheSelectionTheOwnerGaveIsTickedAndATickChangesTheSet()
    {
        var ids = GroupIds(1, 2);
        if (ids.Count < 2) Assert.Ignore("fewer than two rooms in the groups");
        var (c, h, p) = Dialog(new Env(), new[] { ids[1] });
        Assert.That(Rt.ToBoolean(h.Get(Check(h, 0), "selected")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(Check(h, 1), "selected")), Is.True);
        Assert.That(c.Selected, Is.EqualTo(new[] { ids[1] }), "filling the rows leaves the set as it was");
        Assert.That(c.Toggle(0), Is.True);
        Assert.That(c.Selected.OrderBy(x => x), Is.EqualTo(new[] { ids[0], ids[1] }.OrderBy(x => x)));
        c.Toggle(1);
        Assert.That(c.Selected, Is.EqualTo(new[] { ids[0] }));
    }

    [Test]
    public void TheDropdownSwitchesTheGroupAndTheListFollows()
    {
        var (c, h, p) = Dialog(new Env());
        var dd = h.Child("m_groupDropdown");
        Pick(h, p, dd, 1);
        Assert.That((c.Group, c.Rooms.Select(r => r.Id)), Is.EqualTo((1, (IEnumerable<uint>)GroupIds(1))));
        Pick(h, p, dd, 2);
        Assert.That((c.Group, c.Rooms.Select(r => r.Id)), Is.EqualTo((2, (IEnumerable<uint>)GroupIds(2))));
        int shown = Math.Min(c.Rooms.Count, DungeonSearchSettingController.ItemCount);
        for (int i = shown; i < DungeonSearchSettingController.ItemCount; i++) Assert.That(Shown(h, i), Is.False, "row " + i + " past the rooms");
    }

    [Test]
    public void OkGivesTheTickedIdsToTheControlCenterAndCloses()
    {
        var ids = GroupIds(1, 2);
        if (ids.Count < 1) Assert.Ignore("no rooms in the groups");
        var env = new Env();
        var (c, h, p) = Dialog(env, new[] { ids[0] });
        h.Invoke(h.Child("m_okButton"), "dispatchEvent", h.RT.Construct(h.RT.GetClass("flash.events.Event"), new object[] { "buttonClick" }));
        for (int i = 0; i < 3; i++) p.Tick();
        Assert.That(env.Applied, Is.EqualTo(new[] { ids[0] }));
        Assert.That(c.Selected, Is.Empty, "the close clears the dialog's state (vfunc_26)");
    }

    [Test]
    public void WithoutALocalPlayerTheRowsStayUnfilled()
    {
        var (c, h, p) = Dialog(new Env { Player = false });
        Assert.That(c.ItemData(0, 0), Is.False);
    }
}
