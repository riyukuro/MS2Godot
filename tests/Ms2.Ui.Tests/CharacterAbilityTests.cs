using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CCharacterAbilityTable over the client's tables (needs MS2_DATA) and CCharacterAbilityManager's data.</summary>
public class CharacterAbilityTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
        }
        return packs;
    }

    static XDocument Table(string name) => XDocument.Load(new MemoryStream(Packs().Read("Xml", "table/" + name + ".xml")));

    static CharacterAbilityTable Load() =>
        CharacterAbilityTable.Load(Table("characterabilitycategory"), Table("characterability"),
            e => FeatureElements.IsAvailable(Packs(), e), key => key);

    static XDocument Doc(string xml) => XDocument.Parse(xml);

    [Test]
    public void TableHasTheClientsCategoriesAndAbilities()
    {
        var t = Load();
        Assert.That(t.Categories.Select(c => c.Id), Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(t.Abilities, Has.Count.EqualTo(28));
        Assert.That(t.Categories.Sum(c => c.Abilities.Count), Is.EqualTo(28));
        var a = t.GetAbility(110001001);
        Assert.That(a, Is.Not.Null);
        Assert.That((a.CategoryId, a.RequireLevel, a.AdditionalEffectId, a.AdditionalEffectLevel), Is.EqualTo((1, (ushort)50, 110001001, (ushort)1)));
        Assert.That(t.GetCategory(1).Name, Is.EqualTo("s_character_ability_character_category_name"));
        Assert.That(t.GetAbility(1), Is.Null);
    }

    [Test]
    public void AnUnknownCategoryStopsTheAbilityLoad()
    {
        var t = CharacterAbilityTable.Load(Doc("<ms2><category id='1' name='a' tooltip='b'/></ms2>"),
            Doc("<ms2><ability id='10' categoryID='1' requireLevel='50'/><ability id='11' categoryID='9' requireLevel='51'/>" +
                "<ability id='12' categoryID='1' requireLevel='52'/></ms2>"), _ => true, k => k);
        Assert.That(t.Abilities.Select(a => a.Id), Is.EqualTo(new[] { 10 }));
        Assert.That(CharacterAbilityTable.Load(null, Doc("<ms2><ability id='10' categoryID='1'/></ms2>"), _ => true, k => k).Abilities, Is.Empty);
    }

    [Test]
    public void DataAppliesTheLoadAndKnownLearns()
    {
        var d = new CharacterAbilityData();
        Assert.That(d.Apply(new CharacterAbilityMessage.Loaded(new[] { 3, 1 }, 1000, 2), _ => true), Is.True);
        Assert.That(d.Ids, Is.EqualTo(new[] { 3, 1 }));
        Assert.That((d.ResetTime, d.Points), Is.EqualTo((1000L, 2)));
        Assert.That(d.Apply(new CharacterAbilityMessage.Learned(7), id => id != 7), Is.False);
        Assert.That(d.Apply(new CharacterAbilityMessage.Learned(8), _ => true), Is.True);
        Assert.That(d.IsLearned(8), Is.True);
        Assert.That(d.Apply(new CharacterAbilityMessage.Reset(), _ => true), Is.False);
        Assert.That(d.Ids, Has.Count.EqualTo(3));
    }

    [Test]
    public void PointsAreTheLevelsAboveFiftyPlusDefaultAndExtra()
    {
        var d = new CharacterAbilityData { Points = 1 };
        d.Ids.AddRange(new[] { 1, 2 });
        Assert.That(d.Total(null, 3), Is.EqualTo(0u));
        Assert.That(d.Total(55, 3), Is.EqualTo(9u));
        Assert.That(d.Remain(55, 3), Is.EqualTo(7));
        Assert.That(d.Remain(null, 3), Is.EqualTo(-2));
        Assert.That(CharacterAbilityData.IsOpen(true, 50), Is.True);
        Assert.That(CharacterAbilityData.IsOpen(true, 49), Is.False);
        Assert.That(CharacterAbilityData.IsOpen(false, 60), Is.False);
    }

    [Test]
    public void CooldownCountsWholeMinutes()
    {
        var d = new CharacterAbilityData();
        Assert.That(d.CooldownLeft(5000, 10), Is.EqualTo(0));
        d.ResetTime = 1000;
        Assert.That(d.CooldownLeft(1000 + 59, 1), Is.EqualTo(1));
        Assert.That(d.CooldownLeft(1000 + 60, 1), Is.EqualTo(0));
        Assert.That(d.CooldownLeft(1000 + 125, 10), Is.EqualTo(600 - 125));
    }

    [Test]
    public void ALevelChangeBeforeTheMovieLoadsReadsTheHudsModel()
    {
        // UICharacterAbilityDialog::OnChangeMyLevel (@0x140f2b000, uicharacterabilitydialog.cpp:515-530) runs from the
        // dialog's making (NotifyChangeMyLevel, :356); it reads the feature and the constants, and m_30 (the movie) only
        // before the rebuild. A level-up before the dialog was ever opened threw here, so the LevelUp packet's other
        // listeners (the game menu, the guide) were skipped.
        var table = CharacterAbilityTable.Load(Doc("<ms2><category id='1' name='a' tooltip='b'/></ms2>"),
            Doc("<ms2><ability id='10' categoryID='1' requireLevel='50'/><ability id='11' categoryID='1' requireLevel='51'/></ms2>"),
            _ => true, k => k);
        var model = new UiModel(null);
        int asked = 0;
        var counts = new List<int>();
        var c = new CharacterAbilityController
        {
            Table = table, World = () => model,
            IsQuestRewarded = id => { asked = id; return true; },
        };
        c.MenuNotifyCount += counts.Add;
        Assert.That(c.Host, Is.Null);

        c.OnChangeMyLevel(50);
        Assert.That(counts, Is.Empty, "feature 0x2d off: FUN_1401e0800 is 0");

        model.Features.Add(Feature.CharacterAbility);
        c.OnChangeMyLevel(49);
        Assert.That(counts, Is.Empty, "below CharacterAbilityData.StartLevel");
        c.OnChangeMyLevel(50);
        c.OnChangeMyLevel(52);
        Assert.That(counts, Is.EqualTo(new[] { 1, 0 }), "FUN_140f2bee0: an unlearned ability at exactly that level");
        Assert.That(asked, Is.EqualTo((int)model.Tables.CharacterAbilityOpenQuestID), "the constant's quest");

        Assert.DoesNotThrow(() => new CharacterAbilityController { Table = table }.OnChangeMyLevel(50), "no host and no world");
    }

    [Test]
    public void NewAbilityAtIsAnUnlearnedAbilityOfExactlyThatLevel()
    {
        var t = Load();
        var d = new CharacterAbilityData();
        Assert.That(CharacterAbilityController.NewAbilityAt(t, d, 50), Is.True);
        Assert.That(CharacterAbilityController.NewAbilityAt(t, d, 49), Is.False);
        d.Ids.AddRange(t.Abilities.Where(a => a.RequireLevel == 50).Select(a => a.Id));
        Assert.That(CharacterAbilityController.NewAbilityAt(t, d, 50), Is.False);
    }
}
