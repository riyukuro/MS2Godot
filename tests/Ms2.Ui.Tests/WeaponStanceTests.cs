using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Unit and state tests for weapon attachment nodes, per-gender dummy offsets and battle stance transitions
/// (<see cref="WeaponStance"/>, <see cref="WeaponActionTable"/>, <see cref="ItemModelTable"/>).
/// </summary>
public class WeaponStanceTests
{
    static PackSet packs;
    static ItemDataTable items;
    static ItemModelTable models;
    static WeaponActionTable actions;

    static void LoadPacks()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        items = new ItemDataTable(packs);
        models = new ItemModelTable(packs);
        actions = WeaponActionTable.Load(packs);
    }

    static void AssertVec(Vector3 actual, float x, float y, float z, string label = "")
    {
        Assert.That(actual.X, Is.EqualTo(x).Within(1e-4f), label + " X");
        Assert.That(actual.Y, Is.EqualTo(y).Within(1e-4f), label + " Y");
        Assert.That(actual.Z, Is.EqualTo(z).Within(1e-4f), label + " Z");
    }

    [Test]
    public void TheStartItemsAreReadFromEitherJobFileShape()
    {
        static List<XElement> All(XElement parent, string child, string key) => parent.Elements(child).ToList();
        // KMS: <job jobGroupID startItems="a,b">
        var kms = XElement.Parse(@"<ms2><job jobGroupID=""10"" startItems=""13200220,14100190"" /><job jobGroupID=""70"" startItems=""13100225,13100225"" /></ms2>");
        var k = WeaponStance.StartItemsByGroup(kms, All);
        Assert.That(k[10], Is.EqualTo(new[] { 13200220, 14100190 }));
        Assert.That(k[70], Is.EqualTo(new[] { 13100225, 13100225 }), "a dual wield lists its weapon twice");
        // GMS2: <job code="010"><startInvenItem><item itemID/>; its <reward> items are not start items
        var gms2 = XElement.Parse(@"<ms2><job code=""010""><startInvenItem><item itemID=""13200220"" grade=""1"" count=""1"" />
            <item itemID=""14100190"" grade=""1"" count=""1"" /></startInvenItem><reward><item itemID=""13200029"" grade=""1"" /></reward></job>
            <job feature=""JobChange_01"" code=""010""><startInvenItem><item itemID=""1"" /></startInvenItem></job>
            <job code=""020""><startInvenItem><item itemID=""15000224"" /></startInvenItem></job></ms2>");
        Assert.That(JobTable.IsCodeShape(gms2), Is.True);
        Assert.That(JobTable.IsCodeShape(kms), Is.False);
        var g = WeaponStance.StartItemsByGroup(gms2, All);
        Assert.That(g[10], Is.EqualTo(new[] { 13200220, 14100190 }), "the first available row of the group");
        Assert.That(g[20], Is.EqualTo(new[] { 15000224 }));
    }

    [TestCase(10, 0, "Sword_", 200001,
        new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, 0, 10, 0 },
        new float[] { 0, 0, 0, -10, -5, 0 })]
    [TestCase(10, 1, "Sword_", 200001,
        new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, -2, 10, 0 },
        new float[] { 0, 0, 0, -10, -5, 0 })]
    [TestCase(20, 0, "LargeSword_", 300001,
        new[] { "Weapon_Back_R_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -6, 8, 9 },
        new float[] { -5, -5, 0 })]
    [TestCase(20, 1, "LargeSword_", 300001,
        new[] { "Weapon_Back_R_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -8, 8, 9 },
        new float[] { -5, -5, 0 })]
    [TestCase(30, 0, "Staff_", 400001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -6, 0, 0 },
        new float[] { 0, -10, 5 })]
    [TestCase(30, 1, "Staff_", 400001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -10, 0, 0 },
        new float[] { 0, -10, 5 })]
    [TestCase(40, 0, "Sceptre_", 500001,
        new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, -5, 5, 0 },
        new float[] { 0, -90, 0, 0, 0, 0 })]
    [TestCase(40, 1, "Sceptre_", 500001,
        new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, -7, 5, 0 },
        new float[] { 0, -90, 0, 0, 0, 0 })]
    [TestCase(50, 0, "Bow_", 600001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_L_Point" },
        new float[] { -4, 0, 0 },
        new float[] { 15, -10, 0 })]
    [TestCase(50, 1, "Bow_", 600001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_L_Point" },
        new float[] { -6, 0, 0 },
        new float[] { 15, -10, 0 })]
    [TestCase(60, 0, "Cannon_", 700001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_L_Point" },
        new float[] { -8, -5, 17 },
        new float[] { 0, 0, -12 })]
    [TestCase(60, 1, "Cannon_", 700001,
        new[] { "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_L_Point" },
        new float[] { -16, -5, 21 },
        new float[] { 4, 13, -2 })]
    [TestCase(70, 0, "Dagger_", 800021,
        new[] { "Weapon_Side_L_Point", "Weapon_Side_R_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 0, 0, 0 })]
    [TestCase(70, 1, "Dagger_", 800021,
        new[] { "Weapon_Side_L_Point", "Weapon_Side_R_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 0, 0, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 0, 0, 0 })]
    [TestCase(80, 0, "Star_", 900021,
        new[] { "Weapon_Back_B_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { -4, 6, 0, -4, 6, 0 },
        new float[] { 0, 0, 0, 0, 0, 0 })]
    [TestCase(80, 1, "Star_", 900021,
        new[] { "Weapon_Back_B_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { -6, 6, 0, -6, 6, 0 },
        new float[] { 0, 0, 0, 0, 0, 0 })]
    [TestCase(90, 0, "Blade_", 1000001,
        new[] { "Weapon_Back_R_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -5, 5, 20 },
        new float[] { -15, 0, 0 })]
    [TestCase(90, 1, "Blade_", 1000001,
        new[] { "Weapon_Back_R_Point" },
        new[] { "Weapon_Hand_R_Point" },
        new float[] { -5, 1, 18 },
        new float[] { -15, 0, 0 })]
    [TestCase(100, 0, "Knuckle_", 1100001,
        new[] { "Weapon_Back_B_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 7, 2, -4, 7, 2, -4 },
        new float[] { 77, 90, 127, 77, 90, 127 })]
    [TestCase(100, 1, "Knuckle_", 1100001,
        new[] { "Weapon_Back_B_Point", "Weapon_Back_B_Point" },
        new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" },
        new float[] { 7, 2, -4, 7, 2, -4 },
        new float[] { 77, 90, 127, 77, 90, 127 })]
    [TestCase(110, 0, "Orb_", 1200001,
        new[] { "Scene Root" },
        new[] { "Scene Root" },
        new float[] { 0, 0, 0 },
        new float[] { 0, 0, 0 })]
    [TestCase(110, 1, "Orb_", 1200001,
        new[] { "Scene Root" },
        new[] { "Scene Root" },
        new float[] { 0, 0, 0 },
        new float[] { 0, 0, 0 })]
    public void StarterWeaponNodesAndOffsetsPerJobAndGender(
        int jobGroup, int gender, string expectedPrefix, int expectedAttack,
        string[] sheathedNodes, string[] heldNodes, float[] sheathedTranslations, float[] sheathedRotations)
    {
        LoadPacks();
        var stance = WeaponStance.ForJob(packs, jobGroup, gender, items, models, actions);
        Assert.That(stance.Action, Is.Not.Null, $"job {jobGroup} weapon action");
        Assert.That(stance.Action.Prefix, Is.EqualTo(expectedPrefix));
        Assert.That(stance.Action.NormalAttack, Is.EqualTo(expectedAttack));
        Assert.That(stance.Parts.Count, Is.EqualTo(sheathedNodes.Length), $"job {jobGroup} gender {gender} part count");

        // Initial sheathed state (InBattle = false, WeaponsHeld = false)
        Assert.That(stance.InBattle, Is.False);
        Assert.That(stance.WeaponsHeld, Is.False);
        Assert.That(stance.ActionPrefix, Is.EqualTo(""));
        Assert.That(stance.StandSequence, Is.EqualTo("Idle_A"));
        var sheathed = stance.Placements();
        Assert.That(sheathed.Select(p => p.TargetNode).ToArray(), Is.EqualTo(sheathedNodes));
        for (int i = 0; i < sheathed.Count; i++)
        {
            AssertVec(sheathed[i].Translation, sheathedTranslations[i * 3], sheathedTranslations[i * 3 + 1], sheathedTranslations[i * 3 + 2], $"job {jobGroup} g{gender} part {i} sheathed T");
            AssertVec(sheathed[i].Rotation, sheathedRotations[i * 3], sheathedRotations[i * 3 + 1], sheathedRotations[i * 3 + 2], $"job {jobGroup} g{gender} part {i} sheathed R");
        }

        // Battle stance on -> held in hands, dummy offsets reset to zero/identity on hand nodes
        Assert.That(stance.SetBattle(true), Is.True);
        Assert.That(stance.InBattle, Is.True);
        Assert.That(stance.WeaponsHeld, Is.True);
        Assert.That(stance.ActionPrefix, Is.EqualTo(expectedPrefix));
        Assert.That(stance.StandSequence, Is.EqualTo("Attack_Idle_A"));
        Assert.That(stance.ResolveSequence(stance.StandSequence), Is.EqualTo(expectedPrefix + "Attack_Idle_A"));
        var held = stance.Placements();
        Assert.That(held.Select(p => p.TargetNode).ToArray(), Is.EqualTo(heldNodes));
        for (int i = 0; i < held.Count; i++)
        {
            AssertVec(held[i].Translation, 0, 0, 0, $"job {jobGroup} g{gender} part {i} held T");
            AssertVec(held[i].Rotation, 0, 0, 0, $"job {jobGroup} g{gender} part {i} held R");
        }
    }

    [Test]
    public void StanceTransitionsMoveWeaponsBetweenSheathedAndHands()
    {
        LoadPacks();
        // Knight (10, female): 1H sword 13200220 in RH + shield 14100190 in LH
        var stance = WeaponStance.ForJob(packs, 10, 1, items, models, actions);
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" }));

        // 1. Stance on (OnUserBattle emotion=1) -> both sword and shield move to hand nodes
        Assert.That(stance.SetBattle(true), Is.True);
        Assert.That(stance.WeaponsHeld, Is.True);
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" }));
        AssertVec(stance.Placements()[1].Translation, 0, 0, 0, "shield in hand T");
        AssertVec(stance.Placements()[1].Rotation, 0, 0, 0, "shield in hand R");

        // 2. State gating: slow-walk, ladder, water idle or riding answers 0 in the state weapon-hold slot -> sheathes weapons while keeping InBattle = true
        Assert.That(stance.SetState(WeaponStance.StateHoldsWeapons(WeaponStance.StateWalk, 0x18, running: false)), Is.True);
        Assert.That(stance.InBattle, Is.True, "battle stance emotion stays on during slow-walk");
        Assert.That(stance.WeaponsHeld, Is.False, "the slow-walk gait clears the held flag");
        Assert.That(stance.ActionPrefix, Is.EqualTo(""));
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" }));
        AssertVec(stance.Placements()[1].Translation, -2, 10, 0, "shield back on back T");
        AssertVec(stance.Placements()[1].Rotation, -10, -5, 0, "shield back on back R");

        // 3. Resume running / idle -> returns to hands while InBattle is still true
        Assert.That(stance.SetState(WeaponStance.StateHoldsWeapons(WeaponStance.StateWalk, 0x18, running: true)), Is.True);
        Assert.That(stance.WeaponsHeld, Is.True);
        Assert.That(stance.ActionPrefix, Is.EqualTo("Sword_"));
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" }));

        // 4. Stance timeout (OnUserBattle emotion=0) -> returns to sheathed nodes with per-gender dummy offsets
        Assert.That(stance.SetBattle(false), Is.True);
        Assert.That(stance.InBattle, Is.False);
        Assert.That(stance.WeaponsHeld, Is.False);
        Assert.That(stance.ActionPrefix, Is.EqualTo(""));
        Assert.That(stance.StandSequence, Is.EqualTo("Idle_A"));
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" }));
        AssertVec(stance.Placements()[1].Translation, -2, 10, 0, "shield sheathed after timeout T");
        AssertVec(stance.Placements()[1].Rotation, -10, -5, 0, "shield sheathed after timeout R");

        // 5. Casting a battle skill (StateSkill::vfunc_4 with m_bBattle) turns battle stance on and holds weapons
        Assert.That(stance.OnSkill(isBattleSkill: true, motionHoldsWeapon: true), Is.True);
        Assert.That(stance.InBattle, Is.True);
        Assert.That(stance.WeaponsHeld, Is.True);
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Hand_R_Point", "Weapon_Hand_L_Point" }));

        // A skill motion with m_nHoldWeapon == 0 sheathes during the motion while keeping InBattle = true
        Assert.That(stance.OnSkill(isBattleSkill: true, motionHoldsWeapon: false), Is.True);
        Assert.That(stance.InBattle, Is.True);
        Assert.That(stance.WeaponsHeld, Is.False);
        Assert.That(stance.Placements().Select(p => p.TargetNode), Is.EqualTo(new[] { "Weapon_Side_L_Point", "Weapon_Back_B_Point" }));
    }

    [Test]
    public void LeftHandDisallowedOrWithoutRightHandOrWorldDummyStaysSheathed()
    {
        var table = WeaponActionTable.Load(XDocument.Parse(@"
<ms2>
    <allowWeapon rh=""30"" lh=""0"" />
    <allowWeapon rh=""32"" lh=""41"" />
    <weapon rh=""0"" lh=""0"" normalAttack=""1"" actionprefix="""" />
    <weapon rh=""30"" lh=""0"" normalAttack=""100001"" actionprefix=""Blunt_"" />
    <weapon rh=""32"" lh=""0"" normalAttack=""200001"" actionprefix=""Sword_"" />
    <weapon rh=""32"" lh=""41"" normalAttack=""200001"" actionprefix=""Sword_"" />
</ms2>"));
        Assert.That(table.IsAllowed(30, 0), Is.True);
        Assert.That(table.IsAllowed(30, 41), Is.False, "blunt (30) disallows shield (41) in LH");
        Assert.That(table.IsAllowed(32, 41), Is.True, "sword (32) allows shield (41) in LH");
        Assert.That(table.IsAllowed(32, 40), Is.False, "sword (32) disallows codex (40) in LH");
        Assert.That(table.IsAllowed(50, 0), Is.True, "two-handed sword (50) has no allowWeapon restriction");

        // World-mode dummy on LH keeps the LH part on its sheathed node even when RH is held
        var assetRh = ItemModelTable.ParseAsset(5, "RH", XElement.Parse(@"<asset name=""rh.nif"" selfnode=""Point01"" targetnode=""Weapon_Side_L_Point"" />"));
        var assetLhWorld = ItemModelTable.ParseAsset(4, "LH", XElement.Parse(@"
<asset name=""lh.nif"" selfnode=""Point01"" targetnode=""Weapon_Back_B_Point"">
    <dummy gender=""2"" rotation=""0,0,0"" translation=""1,2,3"" worldmode=""true"" />
</asset>"));
        Assert.That(assetLhWorld.HasWorldDummy(0), Is.True);
        Assert.That(assetLhWorld.HasWorldDummy(1), Is.True);
        Assert.That(assetRh.HasWorldDummy(0), Is.False);
    }

    [Test]
    public void WaitingSelectHoldsOnlyAtSub55AndHoldForcesUnlessRiding()
    {
        // StateWaitingSelect::vfunc_15 (statewaitingselect.cpp:225-229): 1 only for m_n10 == 0x55
        Assert.That(WeaponStance.StateHoldsWeapons(0xe, 0x55), Is.True);
        Assert.That(WeaponStance.StateHoldsWeapons(0xe, 0x54), Is.False);
        Assert.That(WeaponStance.StateHoldsWeapons(0xe, 0), Is.False);
        // StateHold vfunc_15 and vfunc_16 (stateholddata.cpp:383-396): both 1 unless riding
        Assert.That(WeaponStance.StateHoldsWeapons(0x20, 0), Is.True);
        Assert.That(WeaponStance.StateHoldsWeapons(0x20, 0, riding: true), Is.False);
        Assert.That(WeaponStance.StateForces(0x20), Is.True);
        Assert.That(WeaponStance.StateForces(0x20, riding: true), Is.False);
        foreach (int id in new[] { 0, 1, 2, 4, 5, 6, 0xe, 0x1c }) Assert.That(WeaponStance.StateForces(id), Is.False, "state " + id);
        // forced hands outside battle (PC::FUN_141659000, pc.cpp:2491-2511), still needing a right-hand weapon
        Assert.That(WeaponStance.HoldsWeapons(false, true, 1, false, WeaponStance.StateForces(0x20)), Is.True);
        Assert.That(WeaponStance.HoldsWeapons(false, true, 0, false, WeaponStance.StateForces(0x20)), Is.False);
        Assert.That(WeaponStance.HoldsWeapons(false, true, 1, false, WeaponStance.StateForces(1)), Is.False);
    }

    [Test]
    public void StateGateIsOneTableForEveryCharacter()
    {
        // State::vfunc_15 by (state id, sub state): idle (1) except sub 6 and 7; walk (2) only running and not sub 0x19;
        // land, fall, jump (4, 5, 6) always; hit, spawn, revival, swim dash, climb, glide and pvp win/lose (0xd 0x11 0x18 0x1c 0x1d 0x1e 0x28) always (StateMoveSkill and the constant-1 states); swim 27, ladder 8, rope 9, dash 20 and unknown ids never
        Assert.That(WeaponStance.StateHoldsWeapons(1, 1), Is.True);
        Assert.That(WeaponStance.StateHoldsWeapons(1, 7), Is.False, "water idle");
        Assert.That(WeaponStance.StateHoldsWeapons(1, 6), Is.False, "idle leave timer");
        Assert.That(WeaponStance.StateHoldsWeapons(2, 0x18, running: true), Is.True);
        Assert.That(WeaponStance.StateHoldsWeapons(2, 0x18, running: false), Is.False, "slow walk");
        Assert.That(WeaponStance.StateHoldsWeapons(2, 0x19, running: true), Is.False);
        foreach (int id in new[] { 4, 5, 6 }) Assert.That(WeaponStance.StateHoldsWeapons(id, 0), Is.True, "state " + id);
        foreach (int id in new[] { 0xd, 0x11, 0x18, 0x1c, 0x1d, 0x1e, 0x28 }) Assert.That(WeaponStance.StateHoldsWeapons(id, 0), Is.True, "state " + id);
        foreach (int id in new[] { 8, 9, 20, 27, 0, 99 }) Assert.That(WeaponStance.StateHoldsWeapons(id, 0), Is.False, "state " + id);
    }

    [Test]
    public void HeldRuleAndHandNodeRuleAreShared()
    {
        // pc.cpp:2491-2511: battle and state, cube clears, vfunc_16 forces, no right-hand weapon clears
        Assert.That(WeaponStance.HoldsWeapons(true, true, 32), Is.True);
        Assert.That(WeaponStance.HoldsWeapons(false, true, 32), Is.False);
        Assert.That(WeaponStance.HoldsWeapons(true, false, 32), Is.False);
        Assert.That(WeaponStance.HoldsWeapons(true, true, 0), Is.False);
        Assert.That(WeaponStance.HoldsWeapons(true, true, 32, inCubeMode: true), Is.False);
        Assert.That(WeaponStance.HoldsWeapons(false, false, 32, inCubeMode: true, stateForcesWeapons: true), Is.True);
        // pc.cpp:3996-4019: right hand follows bPrimary; left hand also needs no world dummy and an allowed pair
        Assert.That(WeaponStance.UsesHandNode(true, false, true, false), Is.True);
        Assert.That(WeaponStance.UsesHandNode(false, false, false, true), Is.False);
        Assert.That(WeaponStance.UsesHandNode(true, true, false, true), Is.True);
        Assert.That(WeaponStance.UsesHandNode(true, true, true, true), Is.False);
        Assert.That(WeaponStance.UsesHandNode(true, true, false, false), Is.False);
    }

    [Test]
    public void DummyLastMatchWinsAndSequenceNeedsPrefix()
    {
        // FUN_141200390 (equipmodel.cpp:563-604) applies every matching entry in order, so the last one wins; the game's
        // attach path calls this same DummyFor (DummyOf in EquipApplier is a thin wrapper), with an empty sequence
        var asset = ItemModelTable.ParseAsset(5, "RH", XElement.Parse(@"
<asset name=""w.nif"" selfnode=""Point01"" targetnode=""Weapon_Side_L_Point"">
    <dummy gender=""2"" rotation=""1,0,0"" translation=""1,0,0"" />
    <dummy gender=""2"" rotation=""2,0,0"" translation=""2,0,0"" />
    <dummy gender=""2"" sequence=""Sword_Attack"" rotation=""3,0,0"" translation=""3,0,0"" />
    <dummy gender=""1"" rotation=""4,0,0"" translation=""4,0,0"" targetnode=""Weapon_Hand_R_Point"" />
</asset>"));
        AssertVec(asset.DummyFor(0, "Weapon_Side_L_Point").Translation, 2, 0, 0, "last plain match, male");
        AssertVec(asset.DummyFor(0, "Weapon_Side_L_Point", "").Translation, 2, 0, 0, "empty sequence skips sequence entries");
        AssertVec(asset.DummyFor(0, "Weapon_Side_L_Point", "Sword_Attack_01").Translation, 3, 0, 0, "sequence prefix applies");
        AssertVec(asset.DummyFor(1, "Weapon_Hand_R_Point").Translation, 4, 0, 0, "female entry on its own node");
        AssertVec(asset.DummyFor(0, "Weapon_Hand_R_Point").Translation, 0, 0, 0, "male has no entry on the hand node");
    }
}
