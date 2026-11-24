using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game menu (uigamemenudialog) run by its own ActionScript with UIGameMenuDialog's C++ side (needs MS2_DATA).</summary>
public class GameMenuTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
    static bool FeatureOn(int id) => NewModel().IsFeatureEnabled(id);

    static (UiHost, GameMenuController) Load(UiModel model = null)
    {
        var c = new GameMenuController { Table = GameMenuTable.Load(Packs(), FeatureOn) };
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
        // setMenuTag: the caption in the tag, bottom-aligned (TextFieldEx.setVerticalAlign)
        var tag = (Ms2.Avm2.Flash.AsTextField)h.Get(group, "tag");
        Assert.That(tag.Text, Is.EqualTo("Character"));
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

    /// <summary>Entry 0x14's (characterability) m_notifyUp on its top entry's button.</summary>
    static bool AbilityUp(UiHost h, GameMenuController c)
    {
        var t = c.Table;
        string top = t.Find(t.FindTopId(0x14)).Name;
        var button = h.Invoke(h.Invoke(h.Child("m_menus"), "getChildByName", top), "getChildByName", "m_button");
        return Rt.ToBoolean(h.Get(h.Get(button, "m_notifyUp"), "visible"));
    }

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
        Assert.That(AbilityUp(h, c), Is.True);
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
        Assert.That(AbilityUp(h, c), Is.True);
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
}
