using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game menu (uigamemenudialog) run by its own ActionScript with UIGameMenuDialog's C++ side (needs MS2_DATA).</summary>
public partial class GameMenuTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
    static bool FeatureOn(int id) => NewModel().IsFeatureEnabled(id);

    static (UiHost, GameMenuController) Load(UiModel model = null, Func<GameMenuDef, bool> enabledBy = null)
    {
        var c = new GameMenuController { Table = GameMenuTable.Load(Packs(), FeatureOn) };
        if (enabledBy != null) c.EnabledBy = enabledBy;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var strs = new Dictionary<string, string>();
        using (var xr = System.Xml.XmlReader.Create(new MemoryStream(Packs().Read("Xml", "string/en/korflash.xml"))))
            while (xr.Read())
                if (xr.NodeType == System.Xml.XmlNodeType.Element && xr.Name == "key" && xr.GetAttribute("id") is { } id && xr.GetAttribute("value") is { } v)
                    strs[id] = v.Replace("\\n", "\n");
        p.Translate = k => strs.TryGetValue(k, out var t) ? t : k;
        var h = new UiHost(p, c, model ?? NewModel());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [Test]
    public void AMarkedMenuRefreshesOnceOnItsNextFrame()
    {
        var c = new GameMenuController();
        Assert.That(c.Update(), Is.False, "vfunc_15: not dirty");
        c.MarkDirty();
        c.MarkDirty();
        Assert.That(c.Update(), Is.True, "m_nDirty: RefreshMenu");
        Assert.That(c.Update(), Is.False, "m_nDirty cleared");
    }

    [Test]
    public void TableHasTheKrGameSet()
    {
        var t = GameMenuTable.Load(Packs(), FeatureOn);
        var top = t.TopItems().Select(i => (t.Find(i.Id)?.Name, i.Col, i.Row)).ToList();
        TestContext.WriteLine(string.Join(", ", top));
        Assert.That(top.Take(6), Is.EqualTo(new[]
        {
            ("charactergroup", 0, 0), ("contentsgroup", 1, 0), ("gamegroup", 2, 0), ("communitygroup", 3, 0), ("home", 4, 0), ("option", 5, 0),
        }));
        Assert.That(t.FindTopId(9), Is.EqualTo(1001), "the inventory is in the character group");
        Assert.That(t.IsChild(9), Is.True);
        Assert.That(t.IsChild(22), Is.False);
    }

    [Test]
    public void EntriesAreBuiltByTheMovie()
    {
        var (h, c) = Load();
        var menus = h.Child("m_menus");
        var names = Enumerable.Range(0, (int)Rt.ToNumber(h.Get(menus, "numChildren")))
            .Select(i => Rt.ToString(h.Get(h.Invoke(menus, "getChildAt", i), "name"))).ToList();
        TestContext.WriteLine(string.Join(", ", names));
        Assert.That(names, Does.Contain("charactergroup").And.Contain("home").And.Contain("option"));
        // addChildAt(entry, 0): the first entry ends up last
        Assert.That(names.Last(), Is.EqualTo("charactergroup"));
        var group = h.Invoke(menus, "getChildByName", "charactergroup");
        Assert.That(Rt.ToNumber(h.Get(group, "x")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(group, "y")), Is.EqualTo(120));
        var button = h.Invoke(group, "getChildByName", "m_button");
        var dp = h.Get(button, "dataProvider");
        int n = (int)Rt.ToNumber(h.Get(dp, "length"));
        var kids = Enumerable.Range(0, n).Select(i => Rt.ToString(h.Get(h.Invoke(dp, "requestItemAt", i), "name"))).ToList();
        TestContext.WriteLine("character group: " + string.Join(", ", kids));
        Assert.That(kids, Does.Contain("inven").And.Contain("skillbook").And.Contain("characterinfo"));
        var home = h.Invoke(menus, "getChildByName", "home");
        Assert.That(Rt.ToNumber(h.Get(home, "x")), Is.EqualTo(4 * 56));
        Assert.That(Rt.ToString(h.Get(h.Invoke(home, "getChildByName", "m_button"), "m_layerName")), Is.EqualTo("home"));
        // setMenuTag: the caption in the tag, bottom-aligned (TextFieldEx.setVerticalAlign). FUN_14193a7b0 gives a top entry
        // its caption only with feature 0xc4 (MainMenuText) on: KR 1; GMS2 (NA 99) has icons only
        var tag = (Ms2.Avm2.Flash.AsTextField)h.Get(group, "tag");
        string caption = h.Player.Translate(c.Table.Find(1001).Label);
        Assert.That(caption, Is.Not.Empty.And.Not.StartWith("$"), "the character group's caption is in the data's strings");
        Assert.That(tag.Text, Is.EqualTo(FeatureOn(0xc4) ? caption : ""));
        Assert.That(tag.VerticalAlign, Is.EqualTo("bottom"));
    }

    [Test]
    public void ClickingAnEntryTogglesItsDialog()
    {
        var (h, c) = Load();
        int toggled = 0; GameMenuDef action = null;
        c.ToggleDialog += id => toggled = id;
        c.MenuAction += d => action = d;
        Assert.That(c.OnCallback("inven", Array.Empty<object>()), Is.True);
        Assert.That(toggled, Is.EqualTo(0x4e2d));
        Assert.That(c.OnCallback("home", Array.Empty<object>()), Is.True);
        Assert.That(action?.Action, Is.EqualTo("FUN_14193b750"));
        Assert.That(c.OnCallback("ugcshop_event", Array.Empty<object>()), Is.True);
        Assert.That(action?.Id, Is.EqualTo(15));
        Assert.That(c.OnCallback("nosuchentry", Array.Empty<object>()), Is.False);
    }

    /// <summary>Entry 0x14's (characterability) m_notifyUp on its top entry's button. Where the menu has no entry 0x14 (its
    /// child needs feature CharacterAbility: KR 15, GMS2's NA 99), whether any top entry's m_notifyUp shows.</summary>
    static bool AbilityUp(UiHost h, GameMenuController c)
    {
        var t = c.Table;
        var menus = h.Child("m_menus");
        if (t.FindTopId(0x14) == 0)
        {
            Assert.That(LobbyFeatures.IsEnabledByName(Packs(), "CharacterAbility"), Is.False, "no entry 0x14: the feature is off");
            return t.TopItems().Select(i => t.Find(i.Id)?.Name).Where(n => n != null).Select(n => h.Invoke(menus, "getChildByName", n))
                .Where(e => !Rt.IsNullish(e)).Any(e => Rt.ToBoolean(h.Get(h.Get(h.Invoke(e, "getChildByName", "m_button"), "m_notifyUp"), "visible")));
        }
        string top = t.Find(t.FindTopId(0x14)).Name;
        var button = h.Invoke(h.Invoke(menus, "getChildByName", top), "getChildByName", "m_button");
        return Rt.ToBoolean(h.Get(h.Get(button, "m_notifyUp"), "visible"));
    }

    /// <summary>The menu has the character ability entry (0x14).</summary>
    static bool HasAbilityEntry(GameMenuController c) => c.Table.FindTopId(0x14) != 0;

    /// <summary>Entry 7's (option) m_notifyRedCircle on its top entry's button.</summary>
    static bool OptionRedCircle(UiHost h, GameMenuController c)
    {
        string top = c.Table.Find(c.Table.FindTopId(7)).Name;
        var button = h.Invoke(h.Invoke(h.Child("m_menus"), "getChildByName", top), "getChildByName", "m_button");
        return Rt.ToBoolean(h.Get(h.Get(button, "m_notifyRedCircle"), "visible"));
    }

    [Test]
    public void TheAgreementReminderRaisesTheOptionEntrysRedCircle()
    {
        var (h, c) = Load();
        string top = c.Table.Find(c.Table.FindTopId(7)).Name;
        var button = h.Invoke(h.Invoke(h.Child("m_menus"), "getChildByName", top), "getChildByName", "m_button");
        if (!h.Has(button, "setNotifyRedCircle"))
        {
            // the GMS2 movie's SystemDropdown has no red circle (no setNotifyRedCircle, no m_notifyRedCircle): the flag is
            // kept and sent to nothing
            Assert.That(Rt.IsNullish(h.Get(button, "m_notifyRedCircle")), Is.True);
            c.SetNotifyRedCircle7Flag(true);
            c.RefreshMenu();
            for (int i = 0; i < 5; i++) h.Player.Tick();
            Assert.That(h.RT.Errors, Is.Empty);
            return;
        }
        Assert.That(OptionRedCircle(h, c), Is.False, "built without the reminder");
        c.SetNotifyRedCircle7Flag(true);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        Assert.That(OptionRedCircle(h, c), Is.True);
        c.RefreshMenu();
        for (int i = 0; i < 5; i++) h.Player.Tick();
        Assert.That(OptionRedCircle(h, c), Is.True, "RefreshMenu sends the kept flag again");
        c.SetNotifyRedCircle7Flag(false);
        Assert.That(OptionRedCircle(h, c), Is.False);
    }

    [Test]
    public void HasSeesTheMoviesOwnMethodsOnly()
    {
        var (h, c) = Load();
        string top = c.Table.Find(c.Table.FindTopId(7)).Name;
        var button = h.Invoke(h.Invoke(h.Child("m_menus"), "getChildByName", top), "getChildByName", "m_button");
        Assert.That(h.Has(button, "setNotifyUp"), Is.True, "a method every movie has");
        Assert.That(h.Has(button, "setNoSuchMethod"), Is.False);
    }

    static UiModel LevelSixty()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        m.Player.Level = 60;
        return m;
    }

    static CharacterAbilityTable OneAbility() => CharacterAbilityTable.Load(
        System.Xml.Linq.XDocument.Parse("<ms2><category id='1' name='a' tooltip='b'/></ms2>"),
        System.Xml.Linq.XDocument.Parse("<ms2><ability id='10' categoryID='1' requireLevel='50'/></ms2>"), _ => true, k => k);

    [Test]
    public void AbilityStateSetAfterTheBuildRaisesTheMark()
    {
        var model = LevelSixty();
        var (h, c) = Load(model);
        Assert.That(AbilityUp(h, c), Is.False, "built without the ability state");
        int quest = (int)model.Tables.CharacterAbilityOpenQuestID;
        c.SetCharacterAbility(OneAbility(), new CharacterAbilityData(), id => id == quest);
        Assert.That(AbilityUp(h, c), Is.EqualTo(HasAbilityEntry(c)), "the mark, where the menu has the entry");
        var learned = new CharacterAbilityData();
        learned.Ids.Add(10);
        c.SetCharacterAbility(OneAbility(), learned, id => id == quest);
        Assert.That(AbilityUp(h, c), Is.False, "every ability learned");
    }

    [Test]
    public void TheOpenQuestsRewardRaisesTheMark()
    {
        var model = LevelSixty();
        var (h, c) = Load(model);
        int quest = (int)model.Tables.CharacterAbilityOpenQuestID;
        var rewarded = new HashSet<int>();
        c.SetCharacterAbility(OneAbility(), new CharacterAbilityData(), rewarded.Contains);
        Assert.That(AbilityUp(h, c), Is.False);
        rewarded.Add(quest);
        c.OnQuestRewarded(quest + 1);
        Assert.That(AbilityUp(h, c), Is.False, "another quest's reward leaves the mark");
        c.OnQuestRewarded(quest);
        Assert.That(AbilityUp(h, c), Is.EqualTo(HasAbilityEntry(c)), "the mark, where the menu has the entry");
    }

    [Test]
    public void TheRewardRecountsTheLearningQuestOnlyWithALocalPlayer()
    {
        var model = NewModel();
        var (h, c) = Load(model);
        var asked = new List<int>();
        c.NotifyCount = id => { asked.Add(id); return 0; };
        c.OnQuestRewarded(1);
        Assert.That(asked, Is.Empty, "no local player");
        model.HasLocalPlayer = true;
        c.OnQuestRewarded(1);
        Assert.That(asked, Is.EqualTo(new[] { 0x4f18 }));
    }

    /// <summary>
    /// The game scene's HUD set waits for the local player (CUIService::FUN_141ad9e50, uiservice.cpp:722-738): a character
    /// already at the job change level at login gets the jobchange entry (54) from the menu's first RefreshMenu, because the
    /// menu opens at the MyPC's OnObjectCreated, after its level and job are read. Opened before the MyPC (the HUD's own
    /// load, the old order) the first RefreshMenu has no player, lists no entry 54, and nothing later refreshes it.
    /// </summary>
    [Test]
    public void TheMenuOpensWithTheLocalPlayerSoItsFirstBuildListsTheJobChange()
    {
        bool player = false;
        bool canChangeJob(GameMenuDef d) => d.EnabledBy == "UIChangeJobDialog::CanChangeJob" && player;   // a level 60 first job

        var open = new GameSceneHudOpen(false);
        GameMenuController menu = null;
        open.Open(() => menu = Load(enabledBy: canChangeJob).Item2);
        Assert.That(menu, Is.Null, "no local player: the set waits");
        player = true;
        open.OnLocalPlayerCreated();
        Assert.That(menu, Is.Not.Null);
        Assert.That(menu.Listed, Does.Contain(54), "the first RefreshMenu sees the MyPC");
        bool late = false;
        open.Open(() => late = true);
        Assert.That(late, Is.True, "with a local player a dialog of the set opens at once");

        player = false;
        var early = Load(enabledBy: canChangeJob).Item2;   // the old order: built at the HUD's load
        player = true;
        Assert.That(early.Update(), Is.False, "no dirty mark: the menu is not built again");
        Assert.That(early.Listed, Does.Not.Contain(54));
    }
}
