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
    // kms2: 0x4F mode 0, 93 bytes (2 breakable entries, state 2)
    static readonly byte[] KmsMode0 = Convert.FromHexString(
        "000200000020003735326230393834633639663433666538303061636532306331663662386130" +
        "020100000000000000002000393636653239393637326166343239303837353635613161303361" +
        "643830356102010000000000000000");

    // kms2: 0x4F mode 1, 45 bytes (single marker transition: state 3, then state 4)
    static readonly byte[] KmsMode1State3 = Convert.FromHexString(
        "012000393036303963303031613366343462666238373365643532303634343961613603010000000000000000");

    static readonly byte[] KmsMode1State4 = Convert.FromHexString(
        "012000393036303963303031613366343462666238373365643532303634343961613604010000000000000000");

    // gms2: 0x50 mode 0, 93 bytes (2 breakable entries, state 2)
    static readonly byte[] Gms2Mode0 = Convert.FromHexString(
        "000200000020003735326230393834633639663433666538303061636532306331663662386130" +
        "020100000000000000002000393636653239393637326166343239303837353635613161303361" +
        "643830356102010000000000000000");

    // gms2: 0x50 mode 1, 45 bytes (single marker transition: state 3, then state 4)
    static readonly byte[] Gms2Mode1State3 = Convert.FromHexString(
        "012000313362323539336339393332346635396161356435636336323633313539326303010000000000000000");

    static readonly byte[] Gms2Mode1State4 = Convert.FromHexString(
        "012000313362323539336339393332346635396161356435636336323633313539326304010000000000000000");

    [Test]
    public void KmsMode0AndMode1UpdateMarkersAndCubeStates()
    {
        var r0 = new ByteReader(KmsMode0);
        var u0 = BreakableUpdate.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(u0.Mode, Is.EqualTo(0));
        Assert.That(u0.Entries.Count, Is.EqualTo(2));

        var objects = new BreakableObjects();
        var events = new List<(string Name, int State)>();
        objects.StateSet += (name, state) => events.Add((name, state));

        // First entry is a known field marker; second is unknown and routes to CubeStates.
        objects.AddMarker(u0.Entries[0].Name);
        objects.Apply(u0);

        var marker = objects.Find(u0.Entries[0].Name);
        Assert.That(marker, Is.Not.Null);
        Assert.That(marker.State, Is.EqualTo(u0.Entries[0].State));
        Assert.That(marker.SyncA, Is.EqualTo(u0.Entries[0].SyncA));
        Assert.That(marker.SyncB, Is.EqualTo(u0.Entries[0].SyncB));
        Assert.That(marker.SyncC, Is.EqualTo(u0.Entries[0].SyncC));
        Assert.That(events, Is.EqualTo(new[] { (u0.Entries[0].Name, (int)u0.Entries[0].State) }));
        Assert.That(objects.CubeStates[u0.Entries[1].Name], Is.EqualTo(u0.Entries[1].State));

        var r3 = new ByteReader(KmsMode1State3);
        var u3 = BreakableUpdate.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        Assert.That(u3.Mode, Is.EqualTo(1));
        Assert.That(u3.Entries.Count, Is.EqualTo(1));
        Assert.That(u3.Entries[0].State, Is.EqualTo(3));

        var r4 = new ByteReader(KmsMode1State4);
        var u4 = BreakableUpdate.Read(r4);
        Assert.That(r4.Available, Is.EqualTo(0));
        Assert.That(u4.Mode, Is.EqualTo(1));
        Assert.That(u4.Entries[0].State, Is.EqualTo(4));
        Assert.That(u4.Entries[0].Name, Is.EqualTo(u3.Entries[0].Name));

        objects.AddMarker(u3.Entries[0].Name);
        events.Clear();
        objects.Apply(u3);
        objects.Apply(u4);

        Assert.That(objects.Find(u3.Entries[0].Name).State, Is.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[]
        {
            (u3.Entries[0].Name, 3),
            (u4.Entries[0].Name, 4),
        }));
    }

    [Test]
    public void Gms2Mode0AndMode1UpdateMarkersAndCubeStates()
    {
        var r0 = new ByteReader(Gms2Mode0);
        var u0 = BreakableUpdate.Read(r0);
        Assert.That(r0.Available, Is.EqualTo(0));
        Assert.That(u0.Mode, Is.EqualTo(0));
        Assert.That(u0.Entries.Count, Is.EqualTo(2));

        var objects = new BreakableObjects();
        var events = new List<(string Name, int State)>();
        objects.StateSet += (name, state) => events.Add((name, state));

        objects.AddMarker(u0.Entries[0].Name);
        objects.Apply(u0);

        var marker = objects.Find(u0.Entries[0].Name);
        Assert.That(marker.State, Is.EqualTo(u0.Entries[0].State));
        Assert.That(marker.SyncA, Is.EqualTo(u0.Entries[0].SyncA));
        Assert.That(objects.CubeStates[u0.Entries[1].Name], Is.EqualTo(u0.Entries[1].State));
        Assert.That(events, Is.EqualTo(new[] { (u0.Entries[0].Name, (int)u0.Entries[0].State) }));

        var r3 = new ByteReader(Gms2Mode1State3);
        var u3 = BreakableUpdate.Read(r3);
        Assert.That(r3.Available, Is.EqualTo(0));
        var r4 = new ByteReader(Gms2Mode1State4);
        var u4 = BreakableUpdate.Read(r4);
        Assert.That(r4.Available, Is.EqualTo(0));

        objects.AddMarker(u3.Entries[0].Name);
        events.Clear();
        objects.Apply(u3);
        objects.Apply(u4);

        Assert.That(objects.Find(u3.Entries[0].Name).State, Is.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[]
        {
            (u3.Entries[0].Name, (int)u3.Entries[0].State),
            (u4.Entries[0].Name, (int)u4.Entries[0].State),
        }));
    }
}
