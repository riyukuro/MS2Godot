using System.Collections.Generic;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The dialog an npc opens on a talk without a script (CNpc::GetTalkUiId / OpenTalkUi,
/// unit_14108e1e0_npcactorandvibrate.cpp:1327-1366): the bank npc (kind 2) opens the bank.
/// </summary>
public class NpcTalkUiTests
{
    [Test]
    public void EachNpcKindOpensItsDialog()
    {
        bool Officer(int k) => k == 70;
        Assert.That(NpcTalkUi.UiIdOf(1, Officer), Is.EqualTo(0x4e3b), "shop");
        Assert.That(NpcTalkUi.UiIdOf(2, Officer), Is.EqualTo(0x4e47), "bank");
        Assert.That(NpcTalkUi.UiIdOf(3, Officer), Is.EqualTo(0x4e4a));
        Assert.That(NpcTalkUi.UiIdOf(0x56, Officer), Is.EqualTo(0x4e97));
        Assert.That(NpcTalkUi.UiIdOf(0x58, Officer), Is.EqualTo(0x4f55));
        Assert.That(NpcTalkUi.UiIdOf(70, Officer), Is.EqualTo(0x4f50), "a fame officer kind");
        Assert.That(NpcTalkUi.UiIdOf(0, Officer), Is.EqualTo(0));
        Assert.That(NpcTalkUi.UiIdOf(4, Officer), Is.EqualTo(0));
        Assert.That(NpcTalkUi.UiIdOf(70, null), Is.EqualTo(0), "no fame table");
    }

    [Test]
    public void ABankNotReadyIsOnlyAskedFor()
    {
        var book = new StorageBook();
        var calls = new List<string>();
        NpcTalkUi.OpenStore(book, () => calls.Add("load"), () => calls.Add("open"));
        Assert.That(calls, Is.EqualTo(new[] { "load" }), "the server's answer opens it");
    }

    [Test]
    public void AReadyBankIsResetAndOpened()
    {
        var book = new StorageBook();
        book.Apply(new StorageMessage.Begin(0, 0));
        Assert.That(book.Ready, Is.True);
        var calls = new List<string>();
        NpcTalkUi.OpenStore(book, () => calls.Add("load"), () => calls.Add(book.Ready ? "open ready" : "open reset"));
        Assert.That(calls, Is.EqualTo(new[] { "open reset" }), "CStore::Reset before OpenDialog(0x4e47)");
    }
}
