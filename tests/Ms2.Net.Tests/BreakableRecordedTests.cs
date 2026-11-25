using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Breakable (KMS 0x4F, GMS2 0x50;
/// CSceneStateGame::OnBreakable @0x1417212d0, scenestategame.cpp:3667-3694 -> BreakableObjects.Apply).
/// </summary>
public class BreakableRecordedTests
{
    // kms2: 0x4F mode 0, 93 bytes (2 breakable entries, state 2, sync 1/0/0)
    static readonly byte[] KmsMode0 = Convert.FromHexString(
        "000200000020003735326230393834633639663433666538303061636532306331663662386130" +
        "020100000000000000002000393636653239393637326166343239303837353635613161303361" +
        "643830356102010000000000000000");

    // kms2: 0x4F mode 1, 45 bytes (single marker transition: state 3, then state 4)
    static readonly byte[] KmsMode1State3 = Convert.FromHexString(
        "012000393036303963303031613366343462666238373365643532303634343961613603010000000000000000");

    static readonly byte[] KmsMode1State4 = Convert.FromHexString(
        "012000393036303963303031613366343462666238373365643532303634343961613604010000000000000000");

    // gms2: 0x50 mode 0, 93 bytes (2 distinct GMS2 breakable entries, state 4, sync 0/0/0x24BE8268)
    static readonly byte[] Gms2Mode0 = Convert.FromHexString(
        "000200000020003561313339633463363435373465653061633663316238333664353563646465" +
        "0400000000006882BE242000323830663334376334663038346131656230633539613837393065" +
        "62643236330400000000006882BE24");

    // gms2: 0x50 mode 1, 45 bytes (single marker transition: state 3, then state 4)
    static readonly byte[] Gms2Mode1State3 = Convert.FromHexString(
        "012000313362323539336339393332346635396161356435636336323633313539326303010000000000000000");

    static readonly byte[] Gms2Mode1State4 = Convert.FromHexString(
        "012000313362323539336339393332346635396161356435636336323633313539326304010000000000000000");

    [Test]
    public void KmsMode0AndMode1UpdateMarkersAndCubeStates()
    {
        const string marker0 = "752b0984c69f43fe800ace20c1f6b8a0";
        const string cube1 = "966e299672af429087565a1a03ad805a";
        const string marker1 = "90609c001a3f44bfb873ed5206449aa6";

        var r0 = new ByteReader(KmsMode0);
        var u0 = BreakableUpdate.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(u0.Mode, Is.EqualTo(0));
        Assert.That(u0.Entries.Count, Is.EqualTo(2));
        Assert.That(u0.Entries[0], Is.EqualTo(new BreakableEntry(marker0, 2, 1, 0, 0)));
        Assert.That(u0.Entries[1], Is.EqualTo(new BreakableEntry(cube1, 2, 1, 0, 0)));

        var objects = new BreakableObjects();
        var events = new List<(string Name, int State)>();
        objects.StateSet += (name, state) => events.Add((name, state));

        // First entry is a known field marker; second is unknown and routes to CubeStates.
        objects.AddMarker(marker0);
        objects.Apply(u0);

        var marker = objects.Find(marker0);
        Assert.That(marker, Is.Not.Null);
        Assert.That(marker.State, Is.EqualTo(2));
        Assert.That(marker.SyncA, Is.EqualTo(1));
        Assert.That(marker.SyncB, Is.EqualTo(0));
        Assert.That(marker.SyncC, Is.EqualTo(0));
        Assert.That(events, Is.EqualTo(new[] { (marker0, 2) }));
        Assert.That(objects.CubeStates[cube1], Is.EqualTo(2));

        var r3 = new ByteReader(KmsMode1State3);
        var u3 = BreakableUpdate.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        Assert.That(u3.Mode, Is.EqualTo(1));
        Assert.That(u3.Entries, Is.EqualTo(new[] { new BreakableEntry(marker1, 3, 1, 0, 0) }));

        var r4 = new ByteReader(KmsMode1State4);
        var u4 = BreakableUpdate.Read(r4);
        Assert.That(r4.Available, Is.EqualTo(0));
        Assert.That(u4.Mode, Is.EqualTo(1));
        Assert.That(u4.Entries, Is.EqualTo(new[] { new BreakableEntry(marker1, 4, 1, 0, 0) }));

        objects.AddMarker(marker1);
        events.Clear();
        objects.Apply(u3);
        objects.Apply(u4);

        Assert.That(objects.Find(marker1).State, Is.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[] { (marker1, 3), (marker1, 4) }));
    }

    [Test]
    public void Gms2Mode0AndMode1UpdateMarkersAndCubeStates()
    {
        const string marker0 = "5a139c4c64574ee0ac6c1b836d55cdde";
        const string cube1 = "280f347c4f084a1eb0c59a8790ebd263";
        const string marker1 = "13b2593c99324f59aa5d5cc62631592c";

        var r0 = new ByteReader(Gms2Mode0);
        var u0 = BreakableUpdate.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(u0.Mode, Is.EqualTo(0));
        Assert.That(u0.Entries.Count, Is.EqualTo(2));
        Assert.That(u0.Entries[0], Is.EqualTo(new BreakableEntry(marker0, 4, 0, 0, 0x24BE8268)));
        Assert.That(u0.Entries[1], Is.EqualTo(new BreakableEntry(cube1, 4, 0, 0, 0x24BE8268)));

        var objects = new BreakableObjects();
        var events = new List<(string Name, int State)>();
        objects.StateSet += (name, state) => events.Add((name, state));

        objects.AddMarker(marker0);
        objects.Apply(u0);

        var marker = objects.Find(marker0);
        Assert.That(marker.State, Is.EqualTo(4));
        Assert.That(marker.SyncA, Is.EqualTo(0));
        Assert.That(marker.SyncB, Is.EqualTo(0));
        Assert.That(marker.SyncC, Is.EqualTo(0x24BE8268));
        Assert.That(objects.CubeStates[cube1], Is.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[] { (marker0, 4) }));

        var r3 = new ByteReader(Gms2Mode1State3);
        var u3 = BreakableUpdate.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        var r4 = new ByteReader(Gms2Mode1State4);
        var u4 = BreakableUpdate.Read(r4);
        Assert.That(r4.Available, Is.EqualTo(0));

        objects.AddMarker(marker1);
        events.Clear();
        objects.Apply(u3);
        objects.Apply(u4);

        Assert.That(objects.Find(marker1).State, Is.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[] { (marker1, 3), (marker1, 4) }));
    }
}
