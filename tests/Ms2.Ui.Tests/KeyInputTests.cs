using System.Reflection;
using System.Runtime.CompilerServices;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UICommon::CMyPCKeyInputController (uicommon.cpp:1540-1682), the move, skill and attack channels the modal dialogs
/// (UIModalDialog::vfunc_47 = FUN_141901470) hold while they are up, and what reads them (InputBlock.OwnKeysBlocked,
/// InputBlock.ActionKeys). Every controller of a UIModalDialog subclass holds them while shown.
/// </summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class KeyInputTests
{
    KeyInputLayers saved;
    Func<uint> savedFlags;
    uint flags;

    [SetUp]
    public void SetUp()
    {
        saved = KeyInput.Shared;
        savedFlags = InputBlock.OwnStateFlags;
        KeyInput.Shared = new KeyInputLayers();
        flags = 0;
        // the own pc: the move channel adds and removes its state flag 0x20, and PCBase m_unk_158 is what the key reads see
        KeyInput.Shared.LocalPcFlag = block => { if (block) flags |= KeyInputLayers.MoveStateFlag; else flags &= ~KeyInputLayers.MoveStateFlag; };
        InputBlock.OwnStateFlags = () => flags;
    }

    [TearDown]
    public void TearDown()
    {
        KeyInput.Shared = saved;
        InputBlock.OwnStateFlags = savedFlags;
    }

    [Test]
    public void TheFirstLayerHoldsAChannelAndTheLastReleaseGivesItBack()
    {
        var k = KeyInput.Shared;
        k.Move(false, 10);
        k.Move(false, 11);
        Assert.That((k.MoveHeld, flags), Is.EqualTo((true, 0x20u)));
        k.Move(true, 10);
        Assert.That((k.MoveHeld, flags), Is.EqualTo((true, 0x20u)), "the other layer still holds it");
        k.Move(true, 11);
        Assert.That((k.MoveHeld, flags), Is.EqualTo((false, 0u)));
    }

    [Test]
    public void ALayerHoldsOnceHoweverOftenItAsksAndAReleaseOfAFreeLayerChangesNothing()
    {
        var k = KeyInput.Shared;
        k.Move(false, 10);
        k.Move(false, 10);
        k.Move(true, 12);                          // not held
        k.Move(true, 10);
        Assert.That(k.MoveHeld, Is.False, "one release gives back what two asks took (FUN_141901170)");
        k.Move(true, 10);                          // not held again: the count stays at 0, not below
        k.Move(false, 10);
        Assert.That(k.MoveHeld, Is.True);
    }

    [Test]
    public void TheAttackChannelSetsTheActionKeyModeToZeroWhileHeld()
    {
        var k = KeyInput.Shared;
        Assert.That(k.ActionKeyMode, Is.EqualTo(1), "CKeyTable m_nMode is 1 by default");
        k.Attack(false, 5);
        Assert.That(k.ActionKeyMode, Is.EqualTo(0));
        k.Attack(true, 5);
        Assert.That(k.ActionKeyMode, Is.EqualTo(1));
    }

    [Test]
    public void TheEntryPointsTakeTheirChannels()
    {
        var k = KeyInput.Shared;
        k.MoveAndAttack(false, 1);                 // FUN_141901470
        Assert.That((k.MoveHeld, k.AttackHeld, k.SkillBlocked), Is.EqualTo((true, true, false)));
        k.MoveAndAttack(true, 1);
        k.MoveAndSkill(false, 2);                  // FUN_141901420
        Assert.That((k.MoveHeld, k.AttackHeld, k.SkillBlocked), Is.EqualTo((true, false, true)));
        k.MoveAndSkill(true, 2);
        k.Channels(false, 4 | 2, 3);               // FUN_141901310: 4 attack, 2 skill, 1 move
        Assert.That((k.MoveHeld, k.AttackHeld, k.SkillBlocked), Is.EqualTo((false, true, true)));
    }

    [Test]
    public void AHeldMoveChannelClosesTheOwnKeyReadsAndTheActionKeysFollowTheAttackChannel()
    {
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((false, true)));
        KeyInput.MoveAndAttack(false, 7);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((true, false)));
        KeyInput.MoveAndAttack(true, 7);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((false, true)));
        KeyInput.Move(false, 7);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((true, true)), "the move channel alone leaves the action keys on");
        KeyInput.Move(true, 7);
        KeyInput.Attack(false, 7);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((false, false)), "the attack channel alone leaves the own key reads on");
    }

    [Test]
    public void NoPcMeansNoFlagButTheCountsStillRun()
    {
        KeyInput.Shared.LocalPcFlag = _ => { };    // pPC null: nothing to flag
        KeyInput.Move(false, 1);
        Assert.That((KeyInput.Shared.MoveHeld, flags), Is.EqualTo((true, 0u)));
    }

    static IEnumerable<Type> ModalControllers() =>
        typeof(UiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ModalDialogController).IsAssignableFrom(t)).OrderBy(t => t.Name);

    /// <summary>The 17 classes the client derives from UIModalDialog; the ones with no controller here yet are STILL-OPEN
    /// and listed, so a port of one has to take the base class (the assertion below fails for a controller of that name that does not).</summary>
    static readonly string[] ClientModalDialogs =
    {
        "Bill", "CommunityQuery", "DungeonSearchSelect", "DungeonSearchSetting", "GemSetting", "HomeInfoEdit", "InviteGroup",
        "ItemGetOption", "ItemMultiOpenBox", "ItemSocketSystem", "MarketBill", "MDEdit", "MeratMarketRegister", "PartySearchRegister",
        "SkillBookAutoDistribute", "LapenshardSystem", "IGuideMovie",
    };

    [Test]
    public void EveryModalDialogControllerHoldsTheMoveAndAttackChannelsWhileShown()
    {
        var types = ModalControllers().ToList();
        Assert.That(types.Count, Is.GreaterThanOrEqualTo(9), "the ported ones: " + string.Join(", ", types.Select(t => t.Name)));
        var ids = new HashSet<int>();
        foreach (var t in types)
        {
            var c = (ModalDialogController)RuntimeHelpers.GetUninitializedObject(t);
            Assert.That(ids.Add(c.TypeId), Is.True, t.Name + ": its type id is its layer, none shared");
            KeyInput.Shared = new KeyInputLayers { LocalPcFlag = KeyInput.Shared.LocalPcFlag };
            flags = 0;
            c.SetEntityVisible(true);
            Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, flags), Is.EqualTo((true, true, 0x20u)), t.Name + " shown");
            Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys, ModalDialogController.ModalDialogId), Is.EqualTo((true, false, c.TypeId)), t.Name + " shown");
            c.SetEntityVisible(false);
            Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, flags, ModalDialogController.IsModalDialogShown), Is.EqualTo((false, false, 0u, false)), t.Name + " hidden");
        }
    }

    [Test]
    public void EveryClientModalDialogWithAControllerDerivesFromTheModalBase()
    {
        var all = typeof(UiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(UiController).IsAssignableFrom(t)).ToList();
        var ported = new List<string>();
        foreach (var name in ClientModalDialogs)
        {
            var found = all.Where(t => t.Name.StartsWith(name, StringComparison.Ordinal) && (t.Name.EndsWith("Controller") || t.Name.EndsWith("DialogController"))).ToList();
            foreach (var t in found)
                Assert.That(typeof(ModalDialogController).IsAssignableFrom(t), Is.True, $"{t.Name}: the client's UI{name}Dialog is a UIModalDialog");
            if (found.Count > 0) ported.Add(name);
        }
        TestContext.WriteLine($"ported {ported.Count} of {ClientModalDialogs.Length}: {string.Join(", ", ported)}; STILL-OPEN: {string.Join(", ", ClientModalDialogs.Except(ported))}");
        Assert.That(ported.Count, Is.GreaterThanOrEqualTo(9));
    }

    [Test]
    public void ALayerLeftByATornDownHudIsGivenBackAndTheNextModalReleasesThePc()
    {
        // a bill shown when the HUD leaves the tree: its hide never runs
        var gone = (ModalDialogController)RuntimeHelpers.GetUninitializedObject(typeof(BillDialogController));
        gone.SetEntityVisible(true);
        KeyInput.ReleaseAll();
        Assert.That((KeyInput.ActionKeyMode, KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, flags), Is.EqualTo((1, false, false, 0u)), "the next login reads the action keys");
        var next = (ModalDialogController)RuntimeHelpers.GetUninitializedObject(typeof(ItemGetOptionDialogController));
        next.SetEntityVisible(true);
        next.SetEntityVisible(false);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys), Is.EqualTo((false, true)), "the next modal's close gives the pc back");
    }

    [Test]
    public void ASecondModalDialogKeepsTheHoldWhenTheFirstCloses()
    {
        var a = (ModalDialogController)RuntimeHelpers.GetUninitializedObject(typeof(BillDialogController));
        var b = (ModalDialogController)RuntimeHelpers.GetUninitializedObject(typeof(ItemGetOptionDialogController));
        a.SetEntityVisible(true);
        b.SetEntityVisible(true);
        a.SetEntityVisible(false);
        Assert.That((InputBlock.OwnKeysBlocked, InputBlock.ActionKeys, ModalDialogController.ModalDialogId), Is.EqualTo((true, false, b.TypeId)), "ClearModalDialogId clears only its own id");
        b.SetEntityVisible(false);
        Assert.That(InputBlock.OwnKeysBlocked, Is.False);
    }

    [Test]
    public void TheRealDialogHoldsOnLoadAndGivesBackOnClose()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var src = TestData.Movies(packs);
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        var c = new ItemMultiOpenBoxController(3);
        c.State.Open(20300327, 1, "k", 5);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var host = new UiHost(p, c, model);
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld), Is.EqualTo((false, false)), "nothing before the load");
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, flags), Is.EqualTo((true, true, 0x20u)), "shown: vfunc_8(1)");
        Assert.That(ModalDialogController.ModalDialogId, Is.EqualTo(ItemMultiOpenBoxController.UiId));
        c.Close();                                  // vfunc_67
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, flags, ModalDialogController.IsModalDialogShown), Is.EqualTo((false, false, 0u, false)));
    }

    [Test]
    public void TheTaxiWorldMapHoldsMoveAndSkillButNotAttack()
    {
        // UIWorldMapDialog has no vfunc_47 override: UIObject's FUN_141901420 (move + skill), called by SetTaxiMode(n) as vfunc_47(m_194 == 0)
        var c = (WorldMapController)RuntimeHelpers.GetUninitializedObject(typeof(WorldMapController));
        c.SetTaxiMode(1);
        c.SetTaxiMode(2);   // the same layer again holds once
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.SkillBlocked, KeyInput.Shared.AttackHeld, flags), Is.EqualTo((true, true, false, 0x20u)));
        Assert.That(InputBlock.ActionKeys, Is.True, "the attack channel (CKeyTable mode) is not touched");
        c.SetTaxiMode(0);
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.SkillBlocked, flags), Is.EqualTo((false, false, 0u)));
    }
}
