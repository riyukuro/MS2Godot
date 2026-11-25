using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.ChatEmoticons;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and ChatEmoticonState handler tests for ChatStamp (KMS 0x126, GMS2 0x128;
/// CSceneStateGame::OnChatStamp @0x14173e440, scenestategame.cpp:5177 -> CChatEmoticonCli::DispatchPacket -> ChatEmoticonState.Apply).
/// </summary>
public class ChatStampRecordedTests
{
    // kms2: 0x126 mode 0, 45 bytes (4 favorites, 2 groups)
    static readonly byte[] KmsMode0List = Convert.FromHexString(
        "000400480D0300470D0300A8860100A7860100020001000000FFFFFFFFFFFFFF7F02000000FFFFFF" +
        "FFFFFFFF7F");

    // kms2: 0x126 mode 0, 17 bytes (0 favorites, 1 group: id 2, permanent)
    static readonly byte[] KmsMode0SingleGroup = Convert.FromHexString("000000010002000000FFFFFFFFFFFFFF7F");

    // gms2: 0x128 mode 0, 17 bytes (0 favorites, 1 group: id 4, permanent)
    static readonly byte[] Gms2Mode0SingleGroup = Convert.FromHexString("000000010004000000FFFFFFFFFFFFFF7F");

    // gms2: 0x128 mode 3, 48 bytes (typed emoticon 300008 in "messengerdialog0.xml", flag 0)
    static readonly byte[] Gms2Mode3Typed = Convert.FromHexString(
        "03E893040014006D0065007300730065006E006700650072006400690061006C006F00670030002E" +
        "0078006D006C0000");

    // gms2: 0x128 mode 5, 5 bytes (move favorite 300008 to end)
    static readonly byte[] Gms2Mode5MoveToEnd = Convert.FromHexString("05E8930400");

    // gms2: 0x128 mode 6, 5 bytes (remove favorite 300008)
    static readonly byte[] Gms2Mode6Remove = Convert.FromHexString("06E8930400");

    static ChatStampMessage DecodeAll(byte[] payload)
    {
        var r = new ByteReader(payload);
        var m = ChatStampMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        return m;
    }

    [Test]
    public void KmsChatStampMode0ReplacesFavoritesAndGroupsInChatEmoticonState()
    {
        var state = new ChatEmoticonState(null);
        int clears = 0, refreshes = 0;
        state.Clear += () => clears++;
        state.Refresh += () => refreshes++;

        var m0 = DecodeAll(KmsMode0List);
        Assert.That(m0, Is.InstanceOf<ChatStampMessage.List>());
        state.Apply(m0);

        Assert.That(clears, Is.EqualTo(1));
        Assert.That(refreshes, Is.EqualTo(1));
        Assert.That(state.Favorites, Is.EqualTo(new uint[] { 200_008u, 200_007u, 100_008u, 100_007u }));
        Assert.That(state.Groups, Is.EqualTo(new[]
        {
            new ChatEmoticonGroup(1u, long.MaxValue),
            new ChatEmoticonGroup(2u, long.MaxValue),
        }));

        // Second mode 0 list replaces the previous favorites and groups.
        state.Apply(DecodeAll(KmsMode0SingleGroup));
        Assert.That(clears, Is.EqualTo(2));
        Assert.That(refreshes, Is.EqualTo(2));
        Assert.That(state.Favorites, Is.Empty);
        Assert.That(state.Groups, Is.EqualTo(new[] { new ChatEmoticonGroup(2u, long.MaxValue) }));
    }

    [Test]
    public void Gms2ChatStampModes0And3And5And6DriveChatEmoticonState()
    {
        var state = new ChatEmoticonState(null);
        int refreshes = 0;
        var typed = new List<(string Window, byte Flag, uint Id)>();
        state.Refresh += () => refreshes++;
        state.TypeInWindow += (w, f, id) => typed.Add((w, f, id));

        state.Apply(DecodeAll(Gms2Mode0SingleGroup));
        Assert.That(state.Favorites, Is.Empty);
        Assert.That(state.Groups, Is.EqualTo(new[] { new ChatEmoticonGroup(4u, long.MaxValue) }));
        Assert.That(refreshes, Is.EqualTo(1));

        state.Apply(DecodeAll(Gms2Mode3Typed));
        Assert.That(typed, Is.EqualTo(new[] { ("messengerdialog0.xml", (byte)0, 300_008u) }));

        state.Apply(DecodeAll(Gms2Mode5MoveToEnd));
        Assert.That(state.Favorites, Is.EqualTo(new uint[] { 300_008u }));
        Assert.That(refreshes, Is.EqualTo(2));

        state.Apply(DecodeAll(Gms2Mode6Remove));
        Assert.That(state.Favorites, Is.Empty);
        Assert.That(refreshes, Is.EqualTo(3));
    }
}
