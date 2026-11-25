using System;
using System.Collections.Generic;
using Godot;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The HUD's part of the global inventory's auto-equip (<see cref="AutoEquip"/>, the session's): what it reads of the field
/// (the bag's tab 0, the local player's level, worn items and item use error, the equip request), the notice of its put-on,
/// the new item call (OnItemInventory mode 7) and the MyPC's frame (ProcessAutoEquipQueue).
/// </summary>
public partial class HudScene
{
    /// <summary>The global inventory's equip request (g_143ab2a90's RequestEquipItem), whether or not the bag dialog is open:
    /// the auto-equip's and the bind-on-equip box's yes.</summary>
    InventoryEquip liveEquip;
    Ms2Godot.Net.LiveSession autoEquipLive;

    /// <summary>Sets the auto-equip's hooks to this HUD's field and subscribes its put-on notice; the returned action undoes it
    /// (the hooks only while they are still this HUD's).</summary>
    Action WatchAutoEquip(Ms2Godot.Net.LiveSession live)
    {
        var a = live.AutoEquip;
        var tables = hudModel.Tables;
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
        a.MinLevel = constants.Int("AutoInstallEquipmentMinLevel", AutoEquip.DefaultMinLevel);
        a.MaxLevel = constants.Int("AutoInstallEquipmentMaxLevel", AutoEquip.DefaultMaxLevel);
        Func<long> now = () => live.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        liveEquip = new InventoryEquip
        {
            Model = () => hudModel,
            View = () => live.Inventory,
            Wearer = () => live.Me?.ToPcInfo(now()),
            LocalPlayerDead = () => live.Me?.Dead == true,
            SystemNotice = SystemNotice,
            ShowBox = MessageBoxQueue.Show,
        };
        Func<IEnumerable<Item>> tab = () => live.Inventory is { } v ? EquipTabItems(v, tables.Items) : null;
        Func<long, Item> find = uid => live.Inventory?.FindByUid(uid) is { } e ? InventoryController.ToItem(e, tables.Items) : null;
        Func<int?> level = () => hudModel.HasLocalPlayer && live.Me?.User is { } u ? u.Level : null;
        Func<int, int, Item> worn = (slot, layer) => WornEquipment.Of(live.Me?.ToPcInfo(now()), tables.Items)?.At(slot, layer);
        Func<Item, int> useError = item => itemUse?.Error(item) ?? 11;
        var equip = liveEquip;
        Func<Item, int> request = item =>
        {
            int r = equip.RequestEquipItem(item, 0, 0);
            if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: auto-equip asks {item.ItemId} uid {item.Uid}: {r}");
            return r;
        };
        a.EquipTabItems = tab; a.FindItemByUid = find; a.PlayerLevel = level; a.WornAt = worn; a.ItemUseError = useError;
        a.RequestEquipItem = request; a.FirstGroupType = preset => tables.ItemModels.Get(preset)?.FirstSlotType ?? 0;
        Action<int> onPutOn = AutoEquipPutOnNotice;
        live.AutoEquipPutOn += onPutOn;
        // a type 10 chat line (uimessenger.cpp:2929): CUserCommandCli::ExecuteInternal, its AutoInstallEquipMent the switch
        var internalHooks = new UserCommands.Hooks
        {
            SetAutoEquipEnabled = on =>
            {
                a.SetAutoEquipEnabled(on);
                if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: auto-equip switch {on}");
            },
            Unported = (name, args) => { GD.Print($"live: internal command {name} is not ported"); return 1; },
        };
        Action<string> internalCommand = text => UserCommands.ExecuteInternal(text, internalHooks);
        ChatLog.UserCommand = internalCommand;
        autoEquipLive = live;
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: auto-equip watch on (levels {a.MinLevel}-{a.MaxLevel}, switch {a.IsAutoEquipEnabled})");
        return () =>
        {
            live.AutoEquipPutOn -= onPutOn;
            if (ChatLog.UserCommand == internalCommand) ChatLog.UserCommand = null;
            if (a.RequestEquipItem == request)
            {
                a.EquipTabItems = null; a.FindItemByUid = null; a.PlayerLevel = null; a.WornAt = null; a.ItemUseError = null;
                a.RequestEquipItem = null;
            }
            if (autoEquipLive == live) autoEquipLive = null;
        };
    }

    /// <summary>GetItemsInCategory(0) (inventory.cpp:986): the bag tab 0's items in slot order.</summary>
    static IEnumerable<Item> EquipTabItems(Ms2.Net.Packets.InventoryView view, ItemDataTable items)
    {
        var cells = view.Cells(0);
        for (int i = 0; i < cells.Count; i++)
            if (view.ItemAt(0, i) is { } e && InventoryController.ToItem(e, items) is { } item) yield return item;
    }

    /// <summary>OnItemPutOn's auto-equip part (scenestategame.cpp:3129-3137): ShowSystemNotice of
    /// s_msg_item_auto_install_equipment with the item's name (1, 1); then the message box layer's current box, when it is a
    /// bind-on-equip box (extra "useBindEquipItem", DAT_143ae7980), is closed (FUN_1419f8120).</summary>
    void AutoEquipPutOnNotice(int itemId)
    {
        var t = hudModel.Tables;
        SystemNoticeText(AutoEquip.PutOnNotice(t.Common, t.ItemName(itemId)));
        bool closed = false;
        if (MessageBoxQueue.Layer is { } layer && layer.CurrentBox is { } box && box.Extra == InventoryController.BindOnEquipBox)
        {
            layer.CloseBox();
            closed = true;
        }
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: auto-equip notice {itemId}{(closed ? ", bind box closed" : "")}");
    }

    /// <summary>MyPC::vfunc_53 (mypc.cpp:3008), each frame with the own PC: Unk_Inventory::ProcessAutoEquipQueue.</summary>
    void TickAutoEquip()
    {
        if (autoEquipLive == null || !hudModel.HasLocalPlayer) return;
        autoEquipLive.AutoEquip.ProcessAutoEquipQueue();
    }
}
