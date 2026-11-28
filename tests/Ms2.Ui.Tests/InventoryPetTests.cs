using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>A pet item used from the bag: InvenSlot::vfunc_13 (invenslot.cpp:868-871), vfunc_14's mode 16 (:1071-1078) and the
/// bind box's yes (UIMessageBox::HandleSummonBindPetButton, uimessagebox.cpp:1669-1686) (needs MS2_DATA).</summary>
public partial class InventoryTests
{
    const int PetItemId = 61000159;

    (UiHost, InventoryController, List<string> raised, List<(long Uid, bool Confirmed, bool Binds)> used, List<MessageBoxData> shown)
        LoadPet(UiModel model, bool bindsOnUse, PetItemUse.Outcome answer)
    {
        if (model.Tables.Items.Get(PetItemId) is not { IsPetItem: true }) Assert.Ignore($"no pet item {PetItemId} in this data");
        var book = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        var rec = new EquipItem { Id = PetItemId, Uid = 40, InventorySlot = 0, Amount = 1, Rarity = 1 };
        if (bindsOnUse) rec.Transfer = new ItemTransfer { Rule = 8 };
        book.Apply(new ItemMessage.LoadTab(PetItemId / 10000000, new[] { rec }));
        var (h, c, _) = Load(model, book);
        var raised = new List<string>(); c.Button += raised.Add;
        var used = new List<(long, bool, bool)>();
        var shown = new List<MessageBoxData>(); c.ShowBox = shown.Add;
        c.UsePet = (i, confirmed, binds) => { used.Add((i.Uid, confirmed, binds)); return answer; };
        c.ShowTab(PetItemId / 10000000); c.Process();
        return (h, c, raised, used, shown);
    }

    [Test]
    public void APetItemsDoubleClickUsesIt()
    {
        Packs();
        var model = NewModel(); model.HasLocalPlayer = true;
        var (_, c, raised, used, shown) = LoadPet(model, false, PetItemUse.Outcome.Summon);
        c.OnCallback("notifySlotUse", new object[] { c.SlotNameOfItem(PetItemId) });
        Assert.That(used, Is.EqualTo(new[] { (40L, false, false) }), "MyPC::FUN_14152aa90(item, 0)");
        Assert.That((raised.Count, shown.Count), Is.EqualTo((0, 0)), "no longer raised");
    }

    [Test]
    public void ABindOnUsePetAsksAndItsYesUsesItConfirmed()
    {
        Packs();
        var model = NewModel(); model.HasLocalPlayer = true;
        var (_, c, _, used, shown) = LoadPet(model, true, PetItemUse.Outcome.BindConfirm);
        c.OnCallback("notifySlotUse", new object[] { c.SlotNameOfItem(PetItemId) });
        Assert.That(used, Is.EqualTo(new[] { (40L, false, true) }));
        var box = shown.Single();
        Assert.That((box.Extra, box.ItemUid), Is.EqualTo((PetItemUse.BindBoxTag, 40L)), "mypc.cpp:3755-3768: the summonBindPet box");

        var boxes = new MessageBoxController { SummonBindPet = c.ConfirmSummonBindPet, data = box };
        Assert.That(boxes.HandlePlainTag(1, PetItemUse.BindBoxTag), Is.True, "no: 1, nothing used");
        Assert.That(used.Count, Is.EqualTo(1));
        Assert.That(boxes.HandlePlainTag(0, PetItemUse.BindBoxTag), Is.True);
        Assert.That(used[^1], Is.EqualTo((40L, true, true)), "yes: the item used confirmed");
        Assert.That(c.ConfirmSummonBindPet(99, 0), Is.False, "FindItemByUid null: 0");
        model.HasLocalPlayer = false;
        Assert.That(c.ConfirmSummonBindPet(40, 0), Is.False, "no local player: 0");
        Assert.That(used.Count, Is.EqualTo(2));
        boxes.data = null;
        Assert.That(boxes.HandlePlainTag(0, PetItemUse.BindBoxTag), Is.True, "no record: 1");
        Assert.That(used.Count, Is.EqualTo(2));
    }

    [Test]
    public void WithThePetDialogAtTheHeadARightClickUsesThePetItem()
    {
        Packs();
        var model = NewModel(); model.HasLocalPlayer = true;
        var (_, c, raised, used, _) = LoadPet(model, false, PetItemUse.Outcome.Summon);
        var item = c.FindSlotItem(c.SlotNameOfItem(PetItemId));
        Assert.That(c.UseMode(item, false), Is.Not.EqualTo(16), "no pet dialog: not mode 16");
        c.DialogHead = () => InventoryController.PetDialogId;
        Assert.That(c.UseMode(item, false), Is.EqualTo(16), "invenslot.cpp:2015-2021: 16 for a pet item");
        c.OnCallback("notifySlotUse", new object[] { c.SlotNameOfItem(PetItemId), 1 });
        Assert.That(used, Is.EqualTo(new[] { (40L, false, false) }), "case 16: FUN_14152aa90(item, 0)");
        model.HasLocalPlayer = false;
        c.OnCallback("notifySlotUse", new object[] { c.SlotNameOfItem(PetItemId), 1 });
        Assert.That((used.Count, raised.Count), Is.EqualTo((1, 0)), "no local player: nothing");
    }
}
