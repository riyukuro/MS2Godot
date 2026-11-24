using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIItemGetOptionDialog (uiitemgetoptiondialog.cpp) over its movie (needs MS2_DATA): the load from the character's
/// auto-loot options, the check boxes and the grade row, the features' hidden boxes, the OK's copy and save.</summary>
public class ItemGetOptionDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static (ItemGetOptionDialogController c, UiHost host, GameOption.AutoLoot saved, List<string> saves) Open(GameOption.AutoLoot saved, Func<int, bool> feature = null, bool cn = false)
    {
        Packs();
        var saves = new List<string>();
        var c = new ItemGetOptionDialogController { Saved = () => saved, SaveCharacterSettings = () => saves.Add("save"), Feature = feature ?? (_ => true), RegionCn = () => cn };
        if (packs.FindByFileName("Resource/Gfx", c.Movie + ".gfx") == null) Assert.Ignore("no " + c.Movie + " in this data");
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true });
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return (c, host, saved, saves);
    }

    static bool Selected(UiHost h, string child) => Rt.ToBoolean(h.Get(h.Child(child), "selected"));

    [Test]
    public void TheBoxesAndTheGradeRowShowTheSavedOptions()
    {
        // OnLoadInit -> LoadOptions (mode 1: the character's), OnLoadComplete: SetSelected per box, the grade's row
        var saved = new GameOption.AutoLoot { Equip = true, Consume = false, Etc = true, Meso = true, Merat = false, Token = true, Other = false, GemStone = true, Pet = false, EquipGrade = 4 };
        var (c, h, _, saves) = Open(saved);
        foreach (var (child, want) in new[] { ("m_equip", true), ("m_consume", false), ("m_etc", true), ("m_meso", true), ("m_merat", false),
                     ("m_token", true), ("m_other", false), ("m_gemstone", true), ("m_pet", false) })
            Assert.That(Selected(h, child), Is.EqualTo(want), child);
        Assert.That(Rt.ToInt32(h.Get(h.Child("m_equipRank"), "selectedIndex")), Is.EqualTo(3), "grade 4: row 3");
        Assert.That(saves, Is.Empty);
    }

    [Test]
    public void TheChecksAndTheRankEditACopyThatOkSavesIntoTheCharactersOptions()
    {
        var saved = new GameOption.AutoLoot();
        var (c, h, _, saves) = Open(saved);
        Assert.That(c.OnCallback("OnCheckEquip", new object[] { true }), Is.True);
        c.OnCallback("OnCheckMeso", new object[] { true });
        c.OnCallback("onCheckPet", new object[] { false });
        c.OnCallback("OnCheckOther", Array.Empty<object>());
        Assert.That(c.OnCallback("onChangeEquipRank", new object[] { 5 }), Is.True);
        Assert.That((saved.Equip, saved.Meso, saved.Pet, saved.EquipGrade), Is.EqualTo((false, false, true, 1)), "nothing changes before OK");
        Assert.That(c.OnCallback("OnClickOk", Array.Empty<object>()), Is.True);
        Assert.That((saved.Equip, saved.Meso, saved.Pet, saved.Other, saved.EquipGrade), Is.EqualTo((true, true, false, false, 6)));
        Assert.That(saves, Is.EqualTo(new[] { "save" }));
        c.OnCallback("onChangeEquipRank", new object[] { 9 });
        Assert.That(c.Options.EquipGrade, Is.EqualTo(1), "a row past the six grades is grade 1");
        Assert.That(c.OnCallback("OnClickSomething", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void TheFeaturesHideThePetAndGemBoxesAndCnHidesMerat()
    {
        // vfunc_46: no 0x8d -> m_pet hidden and disabled; no 0x2e -> m_gemstone hidden; IsRegionCN -> m_merat hidden
        var (_, h, _, _) = Open(new GameOption.AutoLoot(), id => id != 0x8d && id != 0x2e, cn: true);
        bool Visible(string n) => Rt.ToBoolean(h.Get(h.Child(n), "visible"));
        Assert.That((Visible("m_pet"), Rt.ToBoolean(h.Get(h.Child("m_pet"), "enabled")), Visible("m_gemstone"), Visible("m_merat"), Visible("m_equip")),
            Is.EqualTo((false, false, false, false, true)));
    }

    [Test]
    public void PetModeLoadsTheDefaultsAndRaisesItsSend()
    {
        // mode 0: the pet info's flags (not in the port: the defaults), OK -> RequestPet 6 (raised)
        Packs();
        var saved = new GameOption.AutoLoot { Equip = true };
        var c = new ItemGetOptionDialogController { Mode = 0, Saved = () => saved };
        var raised = new List<string>(); c.Button += raised.Add;
        c.LoadOptions();
        Assert.That((c.Options.Equip, c.Options.Pet, c.Options.EquipGrade), Is.EqualTo((false, true, 1)));
        c.ApplyOptions();
        Assert.That((raised.Single(), saved.Equip), Is.EqualTo(("RequestPet6", true)));
    }
}
