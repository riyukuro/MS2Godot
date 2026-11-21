using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The item enchant window UIItemEnchantDialog (0x4e92) with its C++ side as <see cref="ItemEnchantDialogController"/>, over the
/// session's <see cref="Ms2.Ui.Items.ItemEnchantBook"/>. Live only: the NPC talk's open-dialog action (NpcTalk mode 3 sub 4,
/// argument "expMode" for the peachy mode) opens it. STILL-OPEN: the worn equips in the enchantable bag.
/// </summary>
public partial class HudScene
{
    ItemEnchantDialogController itemEnchant;
    MovieDialog itemEnchantMovie;

    bool ItemEnchantOpen => itemEnchant != null && !itemEnchant.IsClosed;

    /// <summary>The talk's open-dialog action: the mode by name, then the window.</summary>
    void OnTalkDialogOpen(string dialog, string argument)
    {
        if (!string.Equals(dialog, ItemEnchantDialogController.Bag, StringComparison.OrdinalIgnoreCase)) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (ItemEnchantOpen) return;
        live.EnchantBook.SetModeByName(argument);
        OpenItemEnchant();
    }

    void OpenItemEnchant()
    {
        if (ItemEnchantOpen) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item enchant: needs the live session"); return; }
        var book = live.EnchantBook;
        book.Send = live.SendEnchant;
        book.Talk = () => (live.TalkNpcId != 0, live.TalkNpcId);
        var table = hudModel.Tables.Items;
        ShowItemEnchant(book, cat => BagItemsOf(live, cat, table), (id, rank) => CountItems(live, id, rank),
            () => live.GameSession?.ServerTime ?? 0, () => { if (live.TalkNpcId != 0) live.EndTalk(); });   // vfunc_67: SendNpcTalkMode0
    }

    void ShowItemEnchant(Ms2.Ui.Items.ItemEnchantBook book, Func<int, IEnumerable<Ms2.Ui.Items.Item>> bag, Func<int, int, int> count,
        Func<long> now, Action closed)
    {
        if (itemEnchantMovie != null) { itemEnchantMovie.Host.Unload(); itemEnchantMovie.QueueFree(); itemEnchantMovie = null; }
        var c = new ItemEnchantDialogController { Book = book, Now = now, BagItems = bag, CountOf = count };
        c.NoticeText += SystemNoticeText;
        c.Closed += closed;
        c.Unloaded += () =>
        {
            itemEnchantMovie?.Host.Unload(); itemEnchantMovie?.QueueFree(); itemEnchantMovie = null;
        };
        itemEnchant = c;
        itemEnchantMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item enchant: load={itemEnchantMovie.Loaded}");
        uiRoot.AddChild(itemEnchantMovie);
    }

    /// <summary>--uigen-open itemenchant: the dialog on a local book with one item and its info, for the gallery (no server).</summary>
    void OpenItemEnchantShot()
    {
        var table = hudModel.Tables.Items;
        int id = new[] { 11300001, 12000001, 20000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(hudModel.Tables.ItemName(i)));
        if (id == 0) { GD.Print("item enchant shot: no item"); return; }
        var item = Ms2.Ui.Items.Item.Create(table, id, 1);
        item.Uid = 1;
        item.Record = new Ms2.Net.Packets.EquipItem { Id = id, Uid = 1 };
        var book = new Ms2.Ui.Items.ItemEnchantBook { Send = _ => { }, Find = u => u == 1 ? item : null, CanEnchantItem = _ => true };
        ShowItemEnchant(book, cat => cat == 0 ? new[] { item } : System.Array.Empty<Ms2.Ui.Items.Item>(), (_, _) => 0, () => 0, () => { });
        book.Select(1);
        book.Apply(new Ms2.Net.Packets.ItemEnchantMessage.Info(1, 1, new Ms2.Net.Packets.EnchantIngredient[0], 0,
            new System.Collections.Generic.Dictionary<int, int> { [1] = 100 }, new Ms2.Net.Packets.EnchantRates(40, 0, 25, 5, 2), 0, 0, 0, id, 1, 1));
    }

    static IEnumerable<Ms2.Ui.Items.Item> BagItemsOf(Ms2Godot.Net.LiveSession live, int tab, Ms2.Ui.Items.ItemDataTable table)
    {
        if (live.Inventory == null) yield break;
        foreach (var cell in live.Inventory.Cells(tab))
            if (!cell.Empty && live.Items.Get(cell.Uid) is { } e && InventoryController.ToItem(e, table) is { } item) yield return item;
    }

    static int CountItems(Ms2Godot.Net.LiveSession live, int id, int rank)
    {
        int n = 0;
        if (live.Inventory == null) return 0;
        for (int tab = 0; tab < ItemBook.Tabs; tab++)
            foreach (var cell in live.Inventory.Cells(tab))
                if (!cell.Empty && cell.ItemId == id && (rank == 0 || live.Items.Get(cell.Uid)?.Rarity == rank)) n += cell.Amount;
        return n;
    }

    bool ItemEnchantKeys(InputEventKey k)
    {
        if (!ItemEnchantOpen || k.Keycode != Key.Escape) return false;
        itemEnchant.Close();
        return true;
    }
}
