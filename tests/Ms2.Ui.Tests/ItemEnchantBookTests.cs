using System.Collections.Generic;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

// UIItemEnchantDialog's model and its checks before each send (client ms2/uiitemenchantdialog.cpp).
public class ItemEnchantBookTests {
    class Rig {
        public ItemEnchantBook B = new();
        public List<Ms2.Net.Wire.IOutPacket> Sent = new();
        public Dictionary<long, Item> Inv = new();
        public List<(int, int)> Notices = new();
        public Rig() {
            B.Find = u => Inv.TryGetValue(u, out var i) ? i : null;
            B.Send = Sent.Add;
            B.Notice += (a, b) => Notices.Add((a, b));
            B.Limits.Add(2, new ItemEnchantLimitTable.Limit(10, 60, 15));
        }
        public Item Add(long uid, int type = 1, int sub = 2, int level = 30, int enchant = 0, int charge = 0, int version = 1) {
            var d = new ItemData { Id = (int) uid, Type = type, Subtype = sub, LevelLimit = level };
            var it = Item.Create(d, 1);
            it.Uid = uid; it.EnchantLevel = enchant;
            it.Record = new EquipItem { Id = (int) uid, Uid = uid };
            it.Record.Enchant.Level = enchant; it.Record.Enchant.Charge = charge; it.Record.Enchant.Version = (byte) version;
            Inv[uid] = it; return it;
        }
    }

    [Test] public void SelectSendsModeOneAndTheTalkCompanion() {
        var t = new Rig(); t.Add(5);
        Assert.That(t.B.Select(5), Is.EqualTo(EnchantStep.Sent));
        Assert.That(t.Sent, Has.Count.EqualTo(2));
        Assert.That(t.Sent[0], Is.EqualTo(new ItemEnchantSelectRequest(1, 5)));
        Assert.That(t.Sent[1], Is.EqualTo(new ItemEnchantTalkRequest(false, 0, 0, 5)));
    }

    [Test] public void SelectRefusesWhatTheLimitTableRefuses() {
        var t = new Rig(); int resets = 0; t.B.SlotReset += () => resets++;
        t.Add(1, type: 2);                      // not an equip
        t.Add(2, sub: 9);                       // no limit row for its slot
        t.Add(3, level: 5);                     // below the band
        t.Add(4, level: 70);                    // above the band
        t.Add(5, enchant: 15);                  // at the top grade
        var p = t.Add(6); p.ExpiryTime = 100;   // has a period
        var l = t.Add(7); l.UnlimitedEnchantLevel = 1;
        var x = t.Add(8); x.Data.ExceptEnchant = true;
        foreach (var u in new long[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            Assert.That(t.B.Select(u), Is.EqualTo(EnchantStep.Reset), $"item {u}");
        Assert.That(resets, Is.EqualTo(8));
        Assert.That(t.Sent.FindAll(s => s is ItemEnchantSelectRequest), Is.Empty);   // the talk companion still goes out
        Assert.That(t.Sent, Has.Count.EqualTo(8));
        Assert.That(t.B.Select(99), Is.EqualTo(EnchantStep.NoItem));
        var n = t.Add(9); n.Record = null;
        Assert.That(t.B.Select(9), Is.EqualTo(EnchantStep.NoItem));
    }

    [Test] public void AnOldChargeAsksTheRefundFirst() {
        var t = new Rig(); t.Add(5, charge: 4000, version: 0);
        Assert.That(t.B.Select(5), Is.EqualTo(EnchantStep.AskRefund));
        Assert.That(t.Sent, Is.Empty);
        t.B.ConfirmRefund();
        Assert.That(t.Sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new ItemEnchantRefundRequest(5) }));
        t.Add(6, charge: 0, version: 0);           // no charge: nothing to refund
        Assert.That(t.B.Select(6), Is.EqualTo(EnchantStep.Sent));
    }

    static Rig Selected(int exp, out Item it) {
        var t = new Rig(); t.B.ExpMode = (byte) exp; it = t.Add(5);
        t.B.Apply(new ItemEnchantMessage.Info((short) exp, 5, new[] { new EnchantIngredient(1, 0, 1) }, 0, new Dictionary<int, int>(),
            exp == 1 ? new EnchantRates(40, 0, 0, 0, 0) : new EnchantRates(100, 0, 0, 0, 0), 31, 32, 0, 555, 3, 1));
        t.Sent.Clear(); return t;
    }

    [Test] public void InfoForAnotherModeClosesTheDialog() {
        var t = new Rig(); int closed = 0; t.B.CloseRequested += () => closed++;
        t.B.Apply(new ItemEnchantMessage.Info(2, 5, new EnchantIngredient[0], 0, new Dictionary<int, int>(), default, 0, 0, 0, 1, 1, 1));
        Assert.That(closed, Is.EqualTo(1)); Assert.That(t.B.Ready, Is.False); Assert.That(t.B.SelectedUid, Is.EqualTo(0));
    }

    [Test] public void OpheliaEnchantAsksProtectorOnlyWithAProtectionRate() {
        var t = Selected(1, out _);
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.Sent));
        Assert.That(t.Sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new ItemEnchantRequest(5, 0) }));
        t.Sent.Clear();
        t.B.Apply(new ItemEnchantMessage.FailStackInfo(0, 0, new long[0], new EnchantRates(40, 0, 25, 0, 0)));
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.AskProtector)); Assert.That(t.Sent, Is.Empty);
        t.B.ConfirmProtector(2);
        Assert.That(t.Sent[0], Is.EqualTo(new ItemEnchantRequest(5, 32)));
        t.B.ConfirmProtector(1);
        Assert.That(t.Sent[1], Is.EqualTo(new ItemEnchantRequest(5, 31)));
    }

    [Test] public void PeachyEnchantSendsModeSixAfterItsBindingBox() {
        var t = Selected(2, out _);
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.Sent));
        Assert.That(t.Sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new ItemEnchantExpRequest(5) }));
        t.Sent.Clear(); t.B.NeedsBindingBox = _ => true;
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.AskBindingCharacter)); Assert.That(t.Sent, Is.Empty);
        t.B.ConfirmBinding(); Assert.That(t.Sent, Has.Count.EqualTo(1));
    }

    [Test] public void EnchantRefusesAMissingItemAndAnOldCharge() {
        var t = Selected(1, out var it);
        t.Inv.Remove(5);
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.NoItem));
        t.Inv[5] = it; it.Record.Enchant.Charge = 10; it.Record.Enchant.Version = 0;
        Assert.That(t.B.Enchant(), Is.EqualTo(EnchantStep.AskRefund)); Assert.That(t.Sent, Is.Empty);
    }

    [Test] public void IngredientNeedsAResultAndAsksBeforeAnEnchantedOne() {
        var t = new Rig(); t.Add(7);
        Assert.That(t.B.SelectIngredient(7, 1), Is.EqualTo(EnchantStep.NotReady));
        t = Selected(1, out _); t.Add(7, enchant: 3).EnchantLevel = 3;
        t.Add(8);
        Assert.That(t.B.SelectIngredient(8, 1), Is.EqualTo(EnchantStep.Sent));
        Assert.That(t.B.SelectIngredient(7, 0), Is.EqualTo(EnchantStep.Sent));          // flag 0: no question
        Assert.That(t.B.SelectIngredient(7, 1), Is.EqualTo(EnchantStep.AskEnchantedIngredient));
        t.Sent.Clear(); t.B.ConfirmEnchantedIngredient();
        Assert.That(t.Sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new ItemEnchantIngredientRequest(7, 1) }));
        Assert.That(t.B.SelectIngredient(99, 0), Is.EqualTo(EnchantStep.NoItem));
    }

    [Test] public void LockedItemsAreNotIngredients() {
        var t = Selected(1, out _);
        var a = t.Add(555); a.Rarity = 3;
        var b = t.Add(555 + 1000); b.Rarity = 3;
        Assert.That(t.B.IsIngredientCandidate(a, 0), Is.True);
        a.LockState = 1; Assert.That(t.B.IsIngredientCandidate(a, 0), Is.False);
        a.LockState = 0; a.UnlockTime = 50; Assert.That(t.B.IsIngredientCandidate(a, 10), Is.False);   // unlock time ahead
        Assert.That(t.B.IsIngredientCandidate(a, 60), Is.True);
        a.Rarity = 2; Assert.That(t.B.IsIngredientCandidate(a, 60), Is.False);                         // wrong rank
        Assert.That(t.B.IsIngredientCandidate(b, 0), Is.False);                                       // wrong id
        var sel = t.Inv[5]; sel.Rarity = 3;
        Assert.That(t.B.IsIngredientCandidate(sel, 0), Is.False);                                     // the selected item itself
    }

    [Test] public void FailStackStaysInsideZeroAndTheItemsCount() {
        var t = Selected(1, out var it); it.Record.Enchant.FailStack = 2; t.B.MaxFailStackConst = 5;
        Assert.That(t.B.ChangeFailStack(-1), Is.EqualTo(EnchantStep.OutOfRange));
        Assert.That(t.B.ChangeFailStack(1), Is.EqualTo(EnchantStep.Sent));
        Assert.That(t.Sent[0], Is.EqualTo(new ItemEnchantFailStackRequest(1)));
        t.B.Apply(new ItemEnchantMessage.FailStackInfo(2, 0, new long[0], default));
        Assert.That(t.B.ChangeFailStack(1), Is.EqualTo(EnchantStep.OutOfRange));
        t.B.MaxFailStackConst = 1; t.B.Apply(new ItemEnchantMessage.FailStackInfo(1, 0, new long[0], default));
        Assert.That(t.B.ChangeFailStack(1), Is.EqualTo(EnchantStep.OutOfRange));
    }

    [Test] public void ResultsRaiseTheMovieEventAndErrorsTheirNotices() {
        var t = Selected(1, out var it); it.EnchantLevel = 4;
        var res = new List<(bool, long, int, int)>(); var rew = new List<(int, int, long)>();
        t.B.Result += (s, u, p, c) => res.Add((s, u, p, c)); t.B.Reward += (a, b, c) => rew.Add((a, b, c));
        var stored = new List<EquipItem>(); t.B.StoreItem = stored.Add;
        var body = new EquipItem { Id = 5, Uid = 5 }; body.Enchant.Level = 5;
        t.B.Apply(new ItemEnchantMessage.Success(5, body, new Dictionary<int, int>()));
        Assert.That(res[0], Is.EqualTo((true, 5L, 4, 5))); Assert.That(t.B.State, Is.EqualTo(1)); Assert.That(stored, Has.Count.EqualTo(1));
        t.B.Apply(new ItemEnchantMessage.Failure(5, body, 100, 0, 0, 0));
        Assert.That(rew, Is.Empty); Assert.That(t.B.State, Is.EqualTo(2));
        t.B.Apply(new ItemEnchantMessage.Failure(5, body, 100, 2, 0, 1)); Assert.That(rew, Is.EqualTo(new[] { (100, 2, 0L) }));
        t.B.Apply(new ItemEnchantMessage.Failure(5, body, 100, 0, 9, 1)); Assert.That(rew, Has.Count.EqualTo(2));   // meso only
        t.B.Apply(new ItemEnchantMessage.Success(5, null, new Dictionary<int, int>())); Assert.That(res, Has.Count.EqualTo(4));   // unknown uid: nothing
        foreach (var c in new short[] { 1, 2, 3, 9 }) t.B.Apply(new ItemEnchantMessage.Error(c));
        Assert.That(t.Notices, Is.EqualTo(new[] { (0x94d, 0), (0x94f, 0), (0x950, 0), (0x94c, 9) }));
    }

    [Test] public void RefundedReselectsTheSelectedItem() {
        var t = Selected(1, out _);
        t.B.Apply(new ItemEnchantMessage.Refunded());
        Assert.That(t.Sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new ItemEnchantSelectRequest(1, 5) }));
        Assert.That(t.Notices, Is.EqualTo(new[] { (0, 0) }));
    }

    [Test] public void ChargeChangeClearsTheFailStackUids() {
        var t = Selected(1, out var it);
        t.B.Apply(new ItemEnchantMessage.FailStackList(3, new long[] { 1, 2 }));
        t.B.Apply(new ItemEnchantMessage.ChargeChanged(5, 777));
        Assert.That(it.Record.Enchant.Charge, Is.EqualTo(777)); Assert.That(t.B.FailStackUids, Is.Empty); Assert.That(t.B.N2BC, Is.EqualTo(0));
    }

    [Test] public void TheTalkArgumentPicksTheMode() {
        var b = new ItemEnchantBook();
        b.SetModeByName("expMode"); Assert.That((b.ExpMode, b.Mode), Is.EqualTo(((byte) 2, 4)));
        b.SetModeByName(""); Assert.That((b.ExpMode, b.Mode), Is.EqualTo(((byte) 1, 3)));
        b.SetModeByName("other"); Assert.That((b.ExpMode, b.Mode), Is.EqualTo(((byte) 1, 3)));
    }
}
