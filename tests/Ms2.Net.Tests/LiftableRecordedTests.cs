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

    // gms2: 0x75 mode 2, 41 bytes (update field lift GUID, enabled true, kind 1, state 0)
    static readonly byte[] Gms2Mode2EnableField = Convert.FromHexString(
        "0220003466396532656436376136363462663339623763376564376232323766326564010100000000");

    // gms2: 0x75 mode 2, 41 bytes (update field lift GUID, enabled false, kind 0, state 3)
    static readonly byte[] Gms2Mode2DisableField = Convert.FromHexString(
        "0220003466396532656436376136363462663339623763376564376232323766326564000000000003");

    // gms2: 0x75 mode 4, 35 bytes (remove field lift GUID)
    static readonly byte[] Gms2Mode4RemoveField = Convert.FromHexString(
        "0420003466396532656436376136363462663339623763376564376232323766326564");

    // gms2: 0x75 mode 0, 5 bytes (empty list)
    static readonly byte[] Gms2Mode0Empty = Convert.FromHexString("0000000000");

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
        var lifts = new LiftableObjects();
        var removed = new List<string>();
        lifts.Removed += name => removed.Add(name);

        var add = DecodeAll(KmsMode3Add);
        Assert.That(add.Mode, Is.EqualTo(LiftableUpdate.ModeAdd));
        lifts.Apply(add);

        var added = lifts.Find(add.Entries[0].Name);
        Assert.That(added, Is.Not.Null);
        Assert.That(added.Kind, Is.EqualTo(add.Entries[0].Kind));
        Assert.That(added.Flag, Is.True);
        Assert.That(added.Enabled, Is.False);
        Assert.That(added.CanLift, Is.False);

        var enable = DecodeAll(KmsMode2Enable);
        lifts.Apply(enable);
        Assert.That(added.Enabled, Is.True);
        Assert.That(added.CanLift, Is.True);

        var disableField = DecodeAll(KmsMode2DisableField);
        var fieldName = disableField.Entries[0].Name;
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
        Assert.That(removed, Is.EqualTo(new[] { rem.Name }));

        // Empty mode 0 removes entity lifts while keeping entity-less mode 3 lifts.
        lifts.AddFieldLift("extra_field_lift", kind: 1);
        var emptyList = DecodeAll(KmsMode0Empty);
        lifts.Apply(emptyList);
        Assert.That(lifts.Find("extra_field_lift"), Is.Null);
        Assert.That(lifts.Find(add.Entries[0].Name), Is.Not.Null);
        Assert.That(removed, Is.EqualTo(new[] { rem.Name, "extra_field_lift" }));
    }

    [Test]
    public void Gms2LiftableModesAddUpdateActiveSetAndRemove()
    {
        var lifts = new LiftableObjects();
        var activeEvents = new List<(string Name, bool Active)>();
        var removed = new List<string>();
        lifts.ActiveSet += (name, active) => activeEvents.Add((name, active));
        lifts.Removed += name => removed.Add(name);

        var add = DecodeAll(Gms2Mode3Add);
        Assert.That(add.Mode, Is.EqualTo(LiftableUpdate.ModeAdd));
        lifts.Apply(add);

        var added = lifts.Find(add.Entries[0].Name);
        Assert.That(added, Is.Not.Null);
        Assert.That(added.QuestC, Is.EqualTo("20001310,20001310"));
        Assert.That(added.QuestD, Is.EqualTo("1,1"));
        Assert.That(added.Flag, Is.True);

        var enable = DecodeAll(Gms2Mode2EnableField);
        var fieldName = enable.Entries[0].Name;
        var fieldLift = lifts.AddFieldLift(fieldName, kind: 0);

        lifts.Apply(enable);
        Assert.That(fieldLift.Enabled, Is.True);
        Assert.That(fieldLift.Kind, Is.EqualTo(enable.Entries[0].Kind));
        Assert.That(fieldLift.State, Is.EqualTo(enable.Entries[0].State));
        Assert.That(fieldLift.CanLift, Is.True);
        Assert.That(activeEvents, Is.EqualTo(new[] { (fieldName, true) }));

        var disable = DecodeAll(Gms2Mode2DisableField);
        lifts.Apply(disable);
        Assert.That(fieldLift.Enabled, Is.False);
        Assert.That(fieldLift.State, Is.EqualTo(3));
        Assert.That(fieldLift.CanLift, Is.False);

        var rem = DecodeAll(Gms2Mode4RemoveField);
        lifts.Apply(rem);
        Assert.That(lifts.Find(fieldName), Is.Null);
        Assert.That(removed, Is.EqualTo(new[] { rem.Name }));

        var emptyList = DecodeAll(Gms2Mode0Empty);
        lifts.AddFieldLift("gms2_extra_lift", kind: 1);
        lifts.Apply(emptyList);
        Assert.That(lifts.Find("gms2_extra_lift"), Is.Null);
        Assert.That(lifts.Find(add.Entries[0].Name), Is.Not.Null);
    }
}
