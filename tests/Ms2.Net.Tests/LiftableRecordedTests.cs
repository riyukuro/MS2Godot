using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Liftable (KMS 0x73, GMS2 0x75;
/// CSceneStateGame::OnLiftable @0x141729250, scenestategame.cpp:11941-12020 -> LiftableObjects.Apply).
/// </summary>
public class LiftableRecordedTests
{
    // kms2: 0x73 mode 3, 30 bytes (add lift "4_528090", kind 1, flag true)
    static readonly byte[] KmsMode3Add = Convert.FromHexString(
        "030800345F35323830393001000000010030000000010030000100300001");

    // kms2: 0x73 mode 2, 17 bytes (update "4_528090", enabled true, kind 1, state 0)
    static readonly byte[] KmsMode2Enable = Convert.FromHexString(
        "020800345F353238303930010100000000");

    // kms2: 0x73 mode 2, 41 bytes (update field lift GUID, enabled false, kind 0, state 3)
    static readonly byte[] KmsMode2DisableField = Convert.FromHexString(
        "0220006136373137646239633734643436653539306439363861326136396533393137000000000003");

    // kms2: 0x73 mode 4, 35 bytes (remove field lift GUID)
    static readonly byte[] KmsMode4RemoveField = Convert.FromHexString(
        "0420006136373137646239633734643436653539306439363861326136396533393137");

    // kms2: 0x73 mode 0, 5 bytes (empty list clears remaining entity lifts)
    static readonly byte[] KmsMode0Empty = Convert.FromHexString("0000000000");

    // gms2: 0x75 mode 3, 66 bytes (add lift "4_389148", kind 1, quest masks, flag true)
    static readonly byte[] Gms2Mode3Add = Convert.FromHexString(
        "030800345F33383931343801000000010030000000110032003000300030003100330031003000" +
        "2C0032003000300030003100330031003000030031002C00310001");

    // gms2: 0x75 mode 0, 134 bytes (non-empty list with 1 field lift and quest masks; removes unlisted field lifts)
    static readonly byte[] Gms2Mode0List = Convert.FromHexString(
        "000100000020003466396532656436376136363462663339623763376564376232323766326564" +
        "0101000000001100390030003000300030003400350033002C0039003000300030003000340035" +
        "003300030031002C0031001100390030003000300030003400350033002C003900300030003000" +
        "3000340035003300030031002C00310001");

    // gms2: 0x75 mode 2, 41 bytes (update field lift GUID, enabled true, kind 1, state 0)
    static readonly byte[] Gms2Mode2EnableField = Convert.FromHexString(
        "0220003466396532656436376136363462663339623763376564376232323766326564010100000000");

    // gms2: 0x75 mode 2, 41 bytes (update field lift GUID, enabled false, kind 0, state 3)
    static readonly byte[] Gms2Mode2DisableField = Convert.FromHexString(
        "0220003466396532656436376136363462663339623763376564376232323766326564000000000003");

    // gms2: 0x75 mode 4, 35 bytes (remove field lift GUID)
    static readonly byte[] Gms2Mode4RemoveField = Convert.FromHexString(
        "0420003466396532656436376136363462663339623763376564376232323766326564");

    static LiftableUpdate DecodeAll(byte[] payload)
    {
        var r = new ByteReader(payload);
        var u = LiftableUpdate.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        return u;
    }

    [Test]
    public void KmsLiftableModesAddUpdateRemoveAndClearFieldLifts()
    {
        const string addedName = "4_528090";
        const string fieldName = "a6717db9c74d46e590d968a2a69e3917";

        var lifts = new LiftableObjects();
        var removed = new List<string>();
        lifts.Removed += name => removed.Add(name);

        var add = DecodeAll(KmsMode3Add);
        Assert.That(add.Mode, Is.EqualTo(LiftableUpdate.ModeAdd));
        lifts.Apply(add);

        var added = lifts.Find(addedName);
        Assert.That(added, Is.Not.Null);
        Assert.That(added.Kind, Is.EqualTo(1));
        Assert.That(added.Flag, Is.True);
        Assert.That(added.Enabled, Is.False);
        Assert.That(added.CanLift, Is.False);

        var enable = DecodeAll(KmsMode2Enable);
        lifts.Apply(enable);
        Assert.That(added.Enabled, Is.True);
        Assert.That(added.CanLift, Is.True);

        var disableField = DecodeAll(KmsMode2DisableField);
        var fieldLift = lifts.AddFieldLift(fieldName, kind: 1);
        Assert.That(fieldLift.Kind, Is.EqualTo(1));

        lifts.Apply(disableField);
        Assert.That(fieldLift.Enabled, Is.False);
        Assert.That(fieldLift.Kind, Is.EqualTo(0));
        Assert.That(fieldLift.State, Is.EqualTo(3));

        var rem = DecodeAll(KmsMode4RemoveField);
        Assert.That(rem.Mode, Is.EqualTo(LiftableUpdate.ModeRemove));
        lifts.Apply(rem);
        Assert.That(lifts.Find(fieldName), Is.Null);
        Assert.That(removed, Is.EqualTo(new[] { fieldName }));

        // Empty mode 0 removes entity lifts while keeping entity-less mode 3 lifts.
        lifts.AddFieldLift("extra_field_lift", kind: 1);
        var emptyList = DecodeAll(KmsMode0Empty);
        lifts.Apply(emptyList);
        Assert.That(lifts.Find("extra_field_lift"), Is.Null);
        Assert.That(lifts.Find(addedName), Is.Not.Null);
        Assert.That(removed, Is.EqualTo(new[] { fieldName, "extra_field_lift" }));
    }

    [Test]
    public void Gms2LiftableModesListAddUpdateActiveSetAndRemove()
    {
        const string addedName = "4_389148";
        const string fieldName = "4f9e2ed67a664bf39b7c7ed7b227f2ed";

        var lifts = new LiftableObjects();
        var activeEvents = new List<(string Name, bool Active)>();
        var removed = new List<string>();
        lifts.ActiveSet += (name, active) => activeEvents.Add((name, active));
        lifts.Removed += name => removed.Add(name);

        var add = DecodeAll(Gms2Mode3Add);
        Assert.That(add.Mode, Is.EqualTo(LiftableUpdate.ModeAdd));
        lifts.Apply(add);

        var added = lifts.Find(addedName);
        Assert.That(added, Is.Not.Null);
        Assert.That(added.QuestC, Is.EqualTo("20001310,20001310"));
        Assert.That(added.QuestD, Is.EqualTo("1,1"));
        Assert.That(added.Flag, Is.True);

        // Non-empty mode 0 updates the listed field lift and removes any unlisted field lifts.
        var fieldLift = lifts.AddFieldLift(fieldName, kind: 0);
        lifts.AddFieldLift("gms2_extra_lift", kind: 1);
        var list = DecodeAll(Gms2Mode0List);
        Assert.That(list.Mode, Is.EqualTo(LiftableUpdate.ModeList));
        Assert.That(list.Entries.Count, Is.EqualTo(1));
        lifts.Apply(list);

        Assert.That(lifts.Find("gms2_extra_lift"), Is.Null);
        Assert.That(lifts.Find(addedName), Is.Not.Null);
        Assert.That(fieldLift.Enabled, Is.True);
        Assert.That(fieldLift.Kind, Is.EqualTo(1));
        Assert.That(fieldLift.State, Is.EqualTo(0));
        Assert.That(fieldLift.QuestA, Is.EqualTo("90000453,90000453"));
        Assert.That(fieldLift.QuestB, Is.EqualTo("1,1"));
        Assert.That(fieldLift.CanLift, Is.True);
        Assert.That(activeEvents, Is.EqualTo(new[] { (fieldName, true) }));
        Assert.That(removed, Is.EqualTo(new[] { "gms2_extra_lift" }));

        var disable = DecodeAll(Gms2Mode2DisableField);
        lifts.Apply(disable);
        Assert.That(fieldLift.Enabled, Is.False);
        Assert.That(fieldLift.Kind, Is.EqualTo(0));
        Assert.That(fieldLift.State, Is.EqualTo(3));
        Assert.That(fieldLift.CanLift, Is.False);

        var enable = DecodeAll(Gms2Mode2EnableField);
        lifts.Apply(enable);
        Assert.That(fieldLift.Enabled, Is.True);
        Assert.That(fieldLift.Kind, Is.EqualTo(1));
        Assert.That(fieldLift.State, Is.EqualTo(0));
        Assert.That(fieldLift.CanLift, Is.True);
        Assert.That(activeEvents, Is.EqualTo(new[] { (fieldName, true), (fieldName, true) }));

        var rem = DecodeAll(Gms2Mode4RemoveField);
        lifts.Apply(rem);
        Assert.That(lifts.Find(fieldName), Is.Null);
        Assert.That(removed, Is.EqualTo(new[] { "gms2_extra_lift", fieldName }));
    }
}
