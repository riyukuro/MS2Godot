using System.Xml.Linq;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The auto loot options (Unk_GsAutoLoot: FUN_141260ed0, LoadGameOptionAutoLooting, SaveGameOptionAutoLooting, FUN_141261340) and
/// one frame of MyPC::FUN_141530db0 (mypc.cpp:4712-4760) in <see cref="PickupPlanner.Frame"/>.
/// </summary>
public class AutoLootTests
{
    // table/default/defaultcharacteroption.xml, the same on KMS2 and GMS2
    const string Default = """<ms2><autoLooting isEnableEquip="true" IsEnableGemStone="true" equipGrade="0" isEnableConsume="true" isEnableInstall="false" isEnableETC="true" isEnableMeso="true" /></ms2>""";

    static GameOption.AutoLoot Loaded(string xml, bool cn = false, bool gem = true, bool pet = true)
    {
        var a = new GameOption.AutoLoot();
        a.Load(XDocument.Parse(xml), cn, gem, pet);
        return a;
    }

    [Test] public void TheResetIsAllOffGradeOneAndPetOn() {
        var a = new GameOption.AutoLoot();
        Assert.That((a.Equip, a.Consume, a.Etc, a.Meso, a.Merat, a.Token, a.Other, a.GemStone, a.Pet, a.EquipGrade),
            Is.EqualTo((false, false, false, false, false, false, false, false, true, 1)));
    }

    [Test] public void TheDefaultFileTurnsMostKindsOnAndMeratAndTokenFollowMeso() {
        var a = Loaded(Default);
        Assert.That((a.Equip, a.EquipGrade, a.Consume, a.Etc, a.Meso), Is.EqualTo((true, 0, true, true, true)));
        Assert.That((a.Merat, a.Token), Is.EqualTo((true, true)), "missing isEnableMerat and isEnableToken take isEnableMeso");
        Assert.That(a.Other, Is.False);
        Assert.That(a.GemStone, Is.True);
        Assert.That(a.Pet, Is.True, "no isEnablePet: the reset's true stays");
    }

    [Test] public void TheFeaturesAndTheRegionGateTheirSwitches() {
        var a = Loaded(Default, cn: true, gem: false, pet: false);
        Assert.That(a.Merat, Is.False, "the CN region reads no merat switch, so the reset's false stays");
        Assert.That(a.Token, Is.True);
        Assert.That(a.GemStone, Is.False, "IsEnableGemStone is read only with feature 0x2e");
        var b = Loaded("""<ms2><autoLooting isEnableMeso="true" isEnableMerat="false" isEnablePet="false" /></ms2>""", pet: false);
        Assert.That((b.Merat, b.Token, b.Pet), Is.EqualTo((false, true, true)), "isEnablePet is read only with feature 0x8c");
    }

    [Test] public void TheSaveWritesWhatTheLoadReads() {
        var d = new XDocument(new XElement("ms2"));
        var a = Loaded(Default);
        a.Other = true; a.EquipGrade = 4;
        a.Save(d, false, false, true);
        var e = d.Root!.Element("autoLooting")!;
        Assert.That(e.Attribute("IsEnableGemStone"), Is.Null);
        Assert.That(e.Attribute("isEnablePet")?.Value, Is.EqualTo("true"));
        var back = new GameOption.AutoLoot();
        back.Load(d, false, false, true);
        Assert.That((back.Other, back.EquipGrade, back.Merat, back.Token), Is.EqualTo((true, 4, true, true)));
    }

    [Test] public void TakesFollowsTheClientsOrder() {
        var a = Loaded(Default);
        Assert.That(a.Takes(20000992, 3, 1, otherDrop: true, 20), Is.False, "a 0x3ed drop needs the other switch");
        a.Other = true;
        Assert.That(a.Takes(20000992, 3, 1, otherDrop: true, 20), Is.True);
        a.Etc = false;
        Assert.That(a.Takes(0x55d4a8a, 3, 1, false, null), Is.True, "a special id goes before the player test");
        Assert.That(a.Takes(20000992, 3, 1, false, null), Is.False, "no local player");
        Assert.That(a.Takes(20000992, 3, 1, false, 0), Is.False, "level 0");
        a.Pet = false;
        Assert.That(a.Takes(1, 0xb, 1, false, 20), Is.False, "a pet item takes the pet switch");
        a.GemStone = false;
        Assert.That(a.Takes(1, 0xd, 1, false, 20) || a.Takes(1, 0xe, 1, false, 20), Is.False, "types 0xd and 0xe take the gem stone switch");
        a.Meso = false;
        Assert.That(a.Takes(90000001, 0, 1, false, 20), Is.False, "a meso id");
        a.Merat = false;
        Assert.That(a.Takes(0x55d4a84, 0, 1, false, 20), Is.False, "a merat id");
        a.Token = false;
        Assert.That(a.Takes(90000004, 0, 1, false, 20) || a.Takes(1, 0x14, 1, false, 20), Is.False, "a token id or type 0x14");
        a.Consume = false;
        Assert.That(a.Takes(1, 2, 1, false, 20), Is.False, "type 2");
        a.EquipGrade = 3;
        Assert.That(a.Takes(1, 1, 2, false, 20), Is.False, "equipment below the grade");
        Assert.That(a.Takes(1, 1, 3, false, 20), Is.True, "equipment at the grade");
        a.Equip = false;
        Assert.That(a.Takes(1, 1, 6, false, 20), Is.False);
        Assert.That(a.Takes(1, 3, 1, false, 20), Is.False, "the rest take the etc switch");
        a.Etc = true;
        Assert.That(a.Takes(1, 3, 1, false, 20), Is.True);
    }

    static PickupPlanner Planner(params PickupCandidate[] c) { var p = new PickupPlanner(); p.Candidates.AddRange(c); return p; }
    static PickupCandidate C(int id, float dist, bool meso = false, bool takes = true, bool owner = true, bool requested = false, bool self = false) =>
        new() { ObjectId = id, Distance = dist, Meso = meso, Owner = owner, AutoTakes = takes, Requested = requested, FromSelf = self, State = DropMotion.Rest };

    [Test] public void AutoLootRequestsTheTakenItemsInRangeThatAreNotFlaggedNorThePlayers() {
        var p = Planner(C(1, 10), C(2, 10, takes: false), C(3, 10, requested: true), C(4, 10, self: true), C(5, 10, owner: false), C(6, 150), C(7, 50));
        p.Frame(5000, 0, 100, 300, 0x80, autoMeso: false, key: false);
        Assert.That(p.Auto, Is.EqualTo(new[] { 0, 6 }));
        Assert.That((p.Candidates[0].Requested, p.Candidates[0].LastRequest), Is.EqualTo((true, 5000u)), "SendRequestItemPickup flags and stamps");
        Assert.That((p.MesoSends, p.Nearest, p.Hop), Is.EqualTo((0, -1, -1)), "no key: no nearest drop and no hop");
    }

    [Test] public void TheKeyAfterAutoLootTakesTheNearestNotJustRequested() {
        var p = Planner(C(1, 10), C(2, 30, takes: false));
        p.Frame(5000, 0, 100, 300, 0x80, autoMeso: false, key: true);
        Assert.That((p.Auto.Count, p.Nearest), Is.EqualTo((1, 1)), "the auto-requested drop was stamped, so the key takes the other");
    }

    [Test] public void TheMesoOptionSendsTheMesosAndTheKeySendsThemAgain() {
        var p = Planner(C(1, 200, meso: true), C(2, 10, meso: true, owner: false));
        p.Frame(5000, 0, 100, 300, 0x80, autoMeso: true, key: false);
        Assert.That((p.Mesos.Count, p.MesoSends), Is.EqualTo((1, 1)));
        p = Planner(C(1, 200, meso: true));
        p.Frame(5000, 0, 100, 300, 0x80, autoMeso: true, key: true);
        Assert.That(p.MesoSends, Is.EqualTo(2), "FUN_1415310e0 runs for the option and for the key over the same list");
        p = Planner(C(1, 200, meso: true));
        p.Frame(5000, 0, 100, 300, 0x80, autoMeso: false, key: false);
        Assert.That((p.Mesos.Count, p.MesoSends), Is.EqualTo((0, 0)));
    }

    [Test] public void ARefusedModeOneRequestIsAskedAgainAfterTheRepeatDelay() {
        var p = Planner(C(1, 10));
        p.Frame(5000, 0, 100, 300, 0x80, false, false);
        var c = p.Candidates[0];
        Assert.That(c.Requested, Is.True);
        p = Planner(c);
        p.Frame(6500, 0, 100, 300, 0x80, false, false);
        Assert.That(p.Auto, Is.Empty, "still flagged: no new request");
        c.Requested = false;   // OnFieldPickupItem, not picked, mode 1
        p = Planner(c);
        p.Frame(5500, 0, 100, 300, 0x80, false, false);
        Assert.That(p.Auto, Is.Empty, "HasPickupDelayElapsed: not before 1000 ms");
        p.Candidates[0] = c;
        p.Frame(6000, 0, 100, 300, 0x80, false, false);
        Assert.That(p.Auto, Is.EqualTo(new[] { 0 }));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheDataDefaultFileLoadsAsTheClientReadsIt(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !File.Exists(Path.Combine(d, "Xml.m2d"))) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var xml = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/default/defaultcharacteroption.xml")).TrimStart('﻿');
        var a = Loaded(xml);
        Assert.That((a.Equip, a.EquipGrade, a.Consume, a.Etc, a.Meso, a.Merat, a.Token, a.Other, a.Pet),
            Is.EqualTo((true, 0, true, true, true, true, true, false, true)));
    }
}
