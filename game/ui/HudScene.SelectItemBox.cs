using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The item boxes' dialogs: UISelectItemBoxDialog (0x4eff, <see cref="SelectItemBoxController"/>) that SelectItemBox opens
/// (@0x140d22430), and UIItemMultiOpenBoxDialog (0x4f88, <see cref="ItemMultiOpenBoxController"/>) that OnOpenItemBoxMulti
/// opens (@0x140d1dc60). Live only. The answers of RequestItemBox (ItemBox 0xAC) go to the multi open window.
/// STILL-OPEN: the limit constant (Common::Constants multiOpenMax, default 1000 here).
/// </summary>
public partial class HudScene
{
    SelectItemBoxController selectItemBox;
    MovieDialog selectItemBoxMovie;
    ItemMultiOpenBoxController itemMultiOpen;
    MovieDialog itemMultiOpenMovie;
    Action<ItemBoxResult> itemBoxAnswer;

    bool SelectItemBoxOpen => selectItemBox != null && !selectItemBox.IsClosed;
    bool ItemMultiOpenOpen => itemMultiOpen != null && !itemMultiOpen.IsClosed;

    /// <summary>SelectItemBox (@0x140d22430): GetItemUseDialog(0x4eff); open returns; else SetItemUid and open.</summary>
    void OpenSelectItemBox(Item item)
    {
        if (SelectItemBoxOpen) return;
        if (selectItemBoxMovie != null) { selectItemBoxMovie.Host.Unload(); selectItemBoxMovie.QueueFree(); selectItemBoxMovie = null; }
        var tables = hudModel.Tables;
        var c = new SelectItemBoxController
        {
            ItemUid = item.Uid,
            FindItem = uid => uid == item.Uid ? item : inventory?.FindItem(uid),
            HaveCount = id => inventory?.BagCountOf(id) ?? 0,
            OpenContents = (box, indices) => inventory?.OpenItemBoxContents(box, indices),
            GetBoxItems = boxId =>
            {
                var list = new List<ItemDropEntry>();
                var me = inventory?.Wearer?.Invoke();
                tables.IndividualDrops.GetBoxItems(tables.Items, boxId, me?.JobGroup ?? 0, me?.Gender ?? 2, true, list);
                return list;
            },
        };
        c.NoticeKey += key => SystemNoticeText(tables.Common(key) ?? "");
        c.Unloaded += () => { selectItemBoxMovie?.Host.Unload(); selectItemBoxMovie?.QueueFree(); selectItemBoxMovie = null; };
        selectItemBox = c;
        selectItemBoxMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"select item box: load={selectItemBoxMovie.Loaded}");
        uiRoot.AddChild(selectItemBoxMovie);
    }

    /// <summary>OnOpenItemBoxMulti's window (GetItemUseDialog(0x4f88), FUN_140c3f9d0): not opened again while open.</summary>
    void OpenItemMultiOpen(int itemId, int rarity, string args, int count)
    {
        if (ItemMultiOpenOpen) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (itemMultiOpenMovie != null) { itemMultiOpenMovie.Host.Unload(); itemMultiOpenMovie.QueueFree(); itemMultiOpenMovie = null; }
        var c = new ItemMultiOpenBoxController(InventoryController.ItemBoxMultiOpenLimit)
        {
            NowMs = () => (long)Time.GetTicksMsec(),
            InBag = id => inventory?.BagCountOf(id) > 0,
            Send = request => live?.SendItemBox(request),
            ShowNotice = text => MessageBoxQueue.Show(MessageBoxQueue.Create(0, text, null, "")),
        };
        c.State.Open(itemId, (short)rarity, args, count);
        itemBoxAnswer = c.OnItemBox;
        if (live != null) live.ItemBoxResulted += itemBoxAnswer;
        c.Unloaded += () =>
        {
            if (live != null) live.ItemBoxResulted -= itemBoxAnswer;
            itemMultiOpenMovie?.Host.Unload(); itemMultiOpenMovie?.QueueFree(); itemMultiOpenMovie = null;
        };
        itemMultiOpen = c;
        BindBoxAnswers();
        itemMultiOpenMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item multi open: load={itemMultiOpenMovie.Loaded}");
        uiRoot.AddChild(itemMultiOpenMovie);
    }

    /// <summary>UIDialog's key handling: Escape closes the select dialog.</summary>
    bool SelectItemBoxKeys(InputEventKey k)
    {
        if (!SelectItemBoxOpen || k.Keycode != Key.Escape) return false;
        selectItemBox.Close();
        return true;
    }

    /// <summary>--uigen-open selectitembox|multiopenbox: the dialog over a fixture item (no live session; the shot never sends).</summary>
    void OpenSelectItemBoxShot(bool multi)
    {
        var tables = hudModel.Tables;
        if (multi) { OpenItemMultiOpen(20300327, 1, "", 7); return; }
        for (int id = 20300000; id < 20400000; id++)
        {
            if (tables.Items.Get(id) is not { FunctionName: "SelectItemBox" } d || d.FunctionArgInt(1, 0) == 0) continue;
            var list = new List<ItemDropEntry>();
            tables.IndividualDrops.GetBoxItems(tables.Items, d.FunctionArgInt(1, 0), 1, 0, true, list);
            if (list.Count == 0) continue;
            var item = Item.Create(tables.Items, id, 1);
            item.Uid = 1;
            OpenSelectItemBox(item);
            return;
        }
        GD.Print("select item box shot: no SelectItemBox item with a drop box");
    }
}
