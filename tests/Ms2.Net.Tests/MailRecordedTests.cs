using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Recorded-packet decode and handler-effect tests for Mail (KMS 0x54, GMS2 0x55;
/// CSceneStateGame::OnMail @0x141721cd0, scenestategame.cpp:11496 -> MailBook.Apply).
/// </summary>
public class MailRecordedTests
{
    const long KmsMailId = 0x1111_0000_0000_0001L;
    const long KmsSenderId = 0x1111_0000_0000_0099L;
    const long Gms2MailId = 0x2222_0000_0000_0001L;
    const long Gms2SenderId = 0x2222_0000_0000_0099L;

    // kms2: 0x54 mode 0, 126 bytes (1 mail, kind 101; mailId, senderId and 9-char title masked)
    static readonly byte[] KmsMode0List = Convert.FromHexString(
        "000100000065010000000000111199000000000011110000000009002A002A002A002A002A002A00" +
        "2A002A002A0000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000A071276500000000A0E4FF64" +
        "000000000000");

    // kms2: 0x54 mode 2, 17 bytes (MarkedRead: KmsMailId, readTime 0x61882C87)
    static readonly byte[] KmsMode2Read = Convert.FromHexString(
        "020100000000001111872C886100000000");

    // kms2: 0x54 mode 11 (0x0B), 17 bytes (CollectedAll: KmsMailId, time 0x61882C8A)
    static readonly byte[] KmsMode11CollectAll = Convert.FromHexString(
        "0B01000000000011118A2C886100000000");

    // kms2: 0x54 mode 13 (0x0D), 9 bytes (Deleted: KmsMailId)
    static readonly byte[] KmsMode13Delete = Convert.FromHexString(
        "0D0100000000001111");

    // gms2: 0x55 mode 0, 124 bytes (1 mail, kind 101; mailId, senderId and 8-char title masked)
    static readonly byte[] Gms2Mode0List = Convert.FromHexString(
        "000100000065010000000000222299000000000022220000000008002A002A002A002A002A002A00" +
        "2A002A00000000000000000000000000000000000000000000000000000000000000000000000000" +
        "000000000000000000000000000000000000BF65A25E00000000A5F1C95E00000000A564A25E0000" +
        "00000000");

    // gms2: 0x55 mode 1, 9 bytes (Sent: GMS2 appends i64 sent mail id, masked)
    static readonly byte[] Gms2Mode1Sent = Convert.FromHexString("010200000000002222");

    // gms2: 0x55 mode 2, 17 bytes (MarkedRead: Gms2MailId, readTime 0x5EA265BF)
    static readonly byte[] Gms2Mode2Read = Convert.FromHexString(
        "020100000000002222BF65A25E00000000");

    // gms2: 0x55 mode 11 (0x0B), 17 bytes (CollectedAll: Gms2MailId, time 0x5EA265C0)
    static readonly byte[] Gms2Mode11CollectAll = Convert.FromHexString(
        "0B0100000000002222C065A25E00000000");

    // gms2: 0x55 mode 13 (0x0D), 9 bytes (Deleted: Gms2MailId)
    static readonly byte[] Gms2Mode13Delete = Convert.FromHexString(
        "0D0100000000002222");

    static MailMessage DecodeAll(byte[] payload, ProtocolProfile profile)
    {
        var r = new ByteReader(payload);
        var msg = MailMessage.Read(r, profile);
        Assert.That(r.Available, Is.EqualTo(0));
        return msg;
    }

    [Test]
    public void KmsMailListReadCollectAndDeleteMutateMailBook()
    {
        var book = new MailBook(MailTexts.Empty());
        var readIds = new List<long>();
        int menuUpdates = 0;
        book.ReadMail += id => readIds.Add(id);
        book.MenuCount += () => menuUpdates++;

        var list = (MailMessage.Listed)DecodeAll(KmsMode0List, ProtocolProfile.Kms2525);
        book.Apply(list, listOpen: true, now: 1_700_000_000L);

        Assert.That(book.Mails.Count, Is.EqualTo(1));
        var mail = book.Find(KmsMailId);
        Assert.That(mail, Is.Not.Null);
        Assert.That(mail.Kind, Is.EqualTo(101));
        Assert.That(mail.SenderId, Is.EqualTo(KmsSenderId));
        Assert.That(mail.IsRead, Is.False);
        Assert.That(book.CountUnreadByKind(101), Is.EqualTo(1));

        var marked = (MailMessage.MarkedRead)DecodeAll(KmsMode2Read, ProtocolProfile.Kms2525);
        book.Apply(marked, listOpen: true, now: 1_700_000_000L);
        Assert.That(mail.IsRead, Is.True);
        Assert.That(mail.ReadTime, Is.EqualTo(0x61882C87L));
        Assert.That(readIds, Is.EqualTo(new[] { KmsMailId }));
        Assert.That(book.CountUnreadByKind(101), Is.EqualTo(0));

        var collected = (MailMessage.CollectedAll)DecodeAll(KmsMode11CollectAll, ProtocolProfile.Kms2525);
        book.Apply(collected, listOpen: true, now: 1_700_000_000L);
        Assert.That(book.ListInfoId, Is.EqualTo(KmsMailId));
        Assert.That(book.ListRefresh, Is.True);

        var deleted = (MailMessage.Deleted)DecodeAll(KmsMode13Delete, ProtocolProfile.Kms2525);
        book.Apply(deleted, listOpen: true, now: 1_700_000_000L);
        Assert.That(book.Mails, Is.Empty);
        Assert.That(book.Find(KmsMailId), Is.Null);
        Assert.That(book.ListRemoved, Is.True);
        Assert.That(menuUpdates, Is.EqualTo(4));
    }

    [Test]
    public void Gms2MailListSentReadCollectAndDeleteMutateMailBook()
    {
        var book = new MailBook(MailTexts.Empty());
        var readIds = new List<long>();
        int sentCount = 0, menuUpdates = 0;
        book.ReadMail += id => readIds.Add(id);
        book.Sent += () => sentCount++;
        book.MenuCount += () => menuUpdates++;

        var list = (MailMessage.Listed)DecodeAll(Gms2Mode0List, ProtocolProfile.Gms2);
        book.Apply(list, listOpen: true, now: 1_600_000_000L);

        Assert.That(book.Mails.Count, Is.EqualTo(1));
        var mail = book.Find(Gms2MailId);
        Assert.That(mail, Is.Not.Null);
        Assert.That(mail.Kind, Is.EqualTo(101));
        Assert.That(mail.SenderId, Is.EqualTo(Gms2SenderId));
        Assert.That(mail.IsRead, Is.True);
        Assert.That(mail.ReadTime, Is.EqualTo(0x5EA265BFL));

        var sent = (MailMessage.Sent)DecodeAll(Gms2Mode1Sent, ProtocolProfile.Gms2);
        book.Apply(sent, listOpen: true, now: 1_600_000_000L);
        Assert.That(sentCount, Is.EqualTo(1));

        var marked = (MailMessage.MarkedRead)DecodeAll(Gms2Mode2Read, ProtocolProfile.Gms2);
        book.Apply(marked, listOpen: true, now: 1_600_000_000L);
        Assert.That(readIds, Is.EqualTo(new[] { Gms2MailId }));

        var collected = (MailMessage.CollectedAll)DecodeAll(Gms2Mode11CollectAll, ProtocolProfile.Gms2);
        book.Apply(collected, listOpen: true, now: 1_600_000_000L);
        Assert.That(book.ListInfoId, Is.EqualTo(Gms2MailId));
        Assert.That(book.ListRefresh, Is.True);

        var deleted = (MailMessage.Deleted)DecodeAll(Gms2Mode13Delete, ProtocolProfile.Gms2);
        book.Apply(deleted, listOpen: true, now: 1_600_000_000L);
        Assert.That(book.Mails, Is.Empty);
        Assert.That(book.ListRemoved, Is.True);
        Assert.That(menuUpdates, Is.EqualTo(5));
    }
}
