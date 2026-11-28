using Ms2.Avm2;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game menu's pet buttons: UIGameMenuDialog::InitPetInfo, RefreshMenu's pet part and OnPetAttack (needs MS2_DATA).</summary>
public partial class GameMenuTests
{
    /// <summary>A model with the pet attack feature (0x97) forced <paramref name="on"/> or off, the rest as the data's.</summary>
    static UiModel PetFeatureModel(bool on)
    {
        string name = FeatureTable.Names[GameMenuController.PetAttackFeature - 1];
        return new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => f == name ? on : LobbyFeatures.IsEnabled(Packs(), f) };
    }

    static bool Shown(UiHost h, string child) => Rt.ToBoolean(h.Get(h.Child(child), "visible"));

    [Test]
    public void InitPetInfoShowsThePetButtonsByTheRowType()
    {
        var (h, c) = Load(PetFeatureModel(true));
        Assert.That(Shown(h, "m_petInfo") || Shown(h, "m_petAttack"), Is.False, "no pet: both hidden");
        c.InitPetInfo(true, true, 1);
        Assert.That(Shown(h, "m_petInfo"), Is.True, "initPetInfo(1, ...): the pet info button");
        Assert.That(Shown(h, "m_petAttack"), Is.True, "a row of type 1: the attack button");
        c.InitPetInfo(true, false, 1);
        Assert.That(Shown(h, "m_petAttack"), Is.False, "another type: no attack button");
        c.InitPetInfo(false, false, 0);
        Assert.That(Shown(h, "m_petInfo"), Is.False, "the unsummon's InitPetInfo(0, 0, 0)");
    }

    [Test]
    public void ThePetAttackFeatureOffHidesTheAttackButton()
    {
        var (h, c) = Load(PetFeatureModel(false));
        c.InitPetInfo(true, true, 1);
        Assert.That(Shown(h, "m_petInfo"), Is.True);
        Assert.That(Shown(h, "m_petAttack"), Is.False, "uigamemenudialog.cpp:1219: feature 0x97 off forces the type flag to 0");
    }

    [Test]
    public void RefreshMenuShowsTheOwnPetsButtons()
    {
        var (h, c) = Load(PetFeatureModel(true));
        c.OwnPetInfo = () => null;
        c.MarkDirty(); c.Update();
        Assert.That(Shown(h, "m_petInfo"), Is.False, "no pet object: RefreshMenu leaves the buttons");
        c.OwnPetInfo = () => (true, 1);
        c.MarkDirty(); c.Update();
        Assert.That(Shown(h, "m_petInfo") && Shown(h, "m_petAttack"), Is.True, "uigamemenudialog.cpp:1513-1518");
    }

    [Test]
    public void ThePetAttackButtonTogglesThroughThePetManager()
    {
        var (h, c) = Load(PetFeatureModel(true));
        c.InitPetInfo(true, true, 1);
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        var pets = new PetManager();
        string named = null;
        c.Button += n => named = n;
        c.TogglePetAuto = () => pets.ToggleAuto(sent.Add);
        Assert.That(c.OnCallback("onpetattack", Array.Empty<object>()), Is.True);
        Assert.That(named, Is.Null, "the button goes to the pet manager");
        Assert.That(pets.Auto, Is.EqualTo(0), "m_nAuto 1 at construction, flipped");
        Assert.That(sent, Is.EqualTo(new[] { new Ms2.Net.Packets.PetAttackRequest(false) }), "RequestPet 0xf with m_nAuto == 1");
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_petAttack"), "selected")), Is.False, "setPetAttackState(0)");
        c.OnCallback("onpetattack", Array.Empty<object>());
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_petAttack"), "selected")), Is.True, "setPetAttackState(1)");
        Assert.That(sent[^1], Is.EqualTo(new Ms2.Net.Packets.PetAttackRequest(true)));
    }
}
