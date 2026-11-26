using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
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

    /// <summary>CNpc::GetTalkUiId reads CNpcData::GetNpcType (npcdata.cpp:893), not the raw kind: kinds 1 and 11-29 are shops (type 1),
    /// 30-39 are type 4. A shop npc of kind 12 (the data's own shop npcs) must open the shop.</summary>
    [Test]
    public void TheTalkUiIsChosenByTheNpcTypeNotTheRawKind()
    {
        static int Ui(int kind) => NpcTalkUi.UiIdOf(new NpcRow { Kind = kind }.NpcType, null);
        Assert.That(Ui(1), Is.EqualTo(0x4e3b));
        for (int k = 11; k < 30; k++) Assert.That(Ui(k), Is.EqualTo(0x4e3b), "shop kind " + k);
        Assert.That(Ui(10), Is.EqualTo(0), "kind 10 is not a shop kind");
        Assert.That(Ui(30), Is.EqualTo(0), "kinds 30-39 are type 4");
        Assert.That(new NpcRow { Kind = 35 }.NpcType, Is.EqualTo(4));
        Assert.That(Ui(2), Is.EqualTo(0x4e47), "the bank kind stays its own type");
        Assert.That(new NpcRow { Kind = 0x56 }.NpcType, Is.EqualTo(0x56));
    }

    /// <summary>Every npc of the data: a shop kind opens the shop, whatever number its kind is.</summary>
    [TestCase("MS2_DATA")]
    public void EveryShopKindNpcOfTheDataOpensTheShop(string variable)
    {
        string data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore(variable + " not set");
        using var packs = new PackSet(data);
        var rows = new NpcTable(packs).Rows.Where(r => r.IsShopKind).ToList();
        TestContext.WriteLine($"{rows.Count} shop-kind npcs, {rows.Count(r => r.Kind != 1)} of them not kind 1");
        Assert.That(rows, Is.Not.Empty);
        foreach (var r in rows) Assert.That(NpcTalkUi.UiIdOf(r.NpcType, null), Is.EqualTo(0x4e3b), "npc " + r.Id + " kind " + r.Kind);
    }
}
