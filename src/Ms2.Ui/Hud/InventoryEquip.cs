using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The global inventory's (g_143ab2a90, Unk_Inventory) equip request: RequestEquipItem and CanEquipItem with their boxes and
/// the bind-on-equip box's yes. The client has one inventory whatever window is open; the bag dialog
/// (<see cref="InventoryController"/>), the bind box's answer and the auto-equip (<see cref="AutoEquip"/>) all go through it,
/// so one without the bag dialog open still asks and sends. What it reads comes from its owner: the HUD model, the live bag
/// view, the own character's record and the box and notice outputs.
/// </summary>
public sealed class InventoryEquip
{
    /// <summary>The HUD model: features, tables, the local player's name and WorldService::GetLocalPlayer.</summary>
    public Func<UiModel> Model = () => null;
    /// <summary>The bag view (FindItemByUid, the server time, the equip send); null without a bag.</summary>
    public Func<InventoryView> View = () => null;
    /// <summary>The wearer's record (gender, job, level and the worn items), null when not known.</summary>
    public Func<PcInfo> Wearer = () => null;
    /// <summary>PCBase::IsDead of the local player.</summary>
    public Func<bool> LocalPlayerDead = () => false;
    /// <summary>ShowSystemNotice of a common string, by its key.</summary>
    public Action<string> SystemNotice = _ => { };
    /// <summary>The message box layer's show.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    bool Feature(int id) => Model()?.IsFeatureEnabled(id) == true;

    /// <summary>
    /// Unk_Inventory::RequestEquipItem(item, slot, layer, skipBinding) (inventory.cpp:1236); 1 when the request went out or the
    /// bind box was shown (the caller's "asked" result), else 0. While the guide restricts putting items on
    /// (CGuideManager::IsRestricted(4), :1237): notice 2185 (<see cref="Guide.GuideManager.ItemPutOnOffNotice"/>), 0; no item
    /// or no local player, 0; a dead local player (IsDead, :1241), box 66 (<see cref="MyInfoController.DeadKey"/>), 0.
    /// FUN_140a65e30 (:1630) on GetItemUseError (:1572): an expired item (CItem::IsExpired, server time) without a maid id:
    /// box 225 (FUN_140d73bc0: ShowMessageBox(GetStringCommon(id), 0)); GetItemRequirementError (:1586) on the wearer's
    /// record: gender box 45, job box 232 for an equipment item else 233, level box 224; each 0. A slot of 0 is
    /// <see cref="WornEquipment.EquipSlotFor"/> (FUN_141420a70; worn items usable as PC::CanUseItem's expiry,
    /// CItem::FUN_140528980 (item.cpp:1717: expired, not a pet and no maid id), and codi-layer parts). CanEquipItem (:1418)
    /// refusing: 0. Unless <paramref name="skipBinding"/>, an item that binds on equip and is not bound asks first
    /// (:1245-1256): box 17 with s_msg_transfer_bind_when_equip and the player's name, tag
    /// <see cref="InventoryController.BindOnEquipBox"/>, the slot asked for and the layer appended, the item uid and entry,
    /// yes / no; 1. Its yes comes back as <see cref="ConfirmBindEquip"/>, which asks again with the skip. FUN_140a661e0 is
    /// always 1; then the send (ItemEquip 0x16 mode 0), with the layer mapped as FUN_1401fb870 (2 only with feature 142);
    /// 1. Nothing is put on here: the server's equip packets do.
    /// STILL-OPEN: the PC bang, VIP and wedding requirements (their player states are not in the port), the music score part of
    /// FUN_140528980, the rest of CanEquipItem (PC::vfunc_132 / CItem::FUN_140525e50 slot acceptance, the worn two-slot pair
    /// boxes 0xd2 / 0xd4 / 0xd6 / 0x43, s_item_err_codi_slot, the cube texture block), the warning sounds (s_warningLevel,
    /// s_warningGender, s_warningClass) and the item sound. Without the wearer's record the requirement checks are skipped (the
    /// client always has the player).
    /// </summary>
    public int RequestEquipItem(Item item, int slot, int layer, bool skipBinding = false)
    {
        if (Guide.GuideManager.Current?.IsRestricted(Guide.GuideLimit.ItemPutOn) == true)
        { SystemNotice(Guide.GuideManager.ItemPutOnOffNotice); return 0; }
        var model = Model();
        var view = View();
        if (item?.Data == null || model == null || view == null || !model.HasLocalPlayer) return 0;
        if (LocalPlayerDead()) { ShowCommonBox(MyInfoController.DeadKey); return 0; }      // inventory.cpp:1241
        var d = item.Data;
        long now = view.ServerTime();
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= now;
        if (expired && !d.HasMaidId) { ShowCommonBox(InventoryController.ExpiredKey); return 0; }
        var record = Wearer();
        if (record != null && !d.IsGenderAllowed(record.Gender)) { ShowCommonBox(InventoryController.GenderKey); return 0; }
        if (record != null && !d.IsJobAllowed(record.JobGroup))
        { ShowCommonBox(d.Type == 1 ? InventoryController.EquipJobKey : InventoryController.DisabledJobKey); return 0; }
        if (record != null && !d.IsLevelAllowed(record.Level)) { ShowCommonBox(InventoryController.LowLevelKey); return 0; }
        var models = model.Tables.ItemModels;
        var worn = WornEquipment.Of(record, model.Tables.Items) ?? WornEquipment.Of(new PcInfo { Equips = new List<EquipItem>() }, model.Tables.Items);
        bool CanUse(Item w) => ItemLevels.Usable(w, now) && (record?.CodiActive == true || !IsLayer2(w));
        bool IsLayer2(Item w) => record?.Outfit2?.Any(o => o.Uid == w.Uid) == true;
        int selected = slot == 0 ? worn.EquipSlotFor(item, models, Feature, CanUse, w => ItemLevels.Of(w)?.Total) : slot;
        if (!CanEquipItem(item, selected, layer)) return 0;      // inventory.cpp:1244: the slot picked, not the one asked for
        if (!skipBinding && InventoryController.NeedsBindConfirm(item))
        {
            // inventory.cpp:1245-1256: the box keeps the slot asked for (0: chosen again on the yes) and the layer
            var box = BindBox(model, item, InventoryController.BindOnEquipKey, InventoryController.BindOnEquipBox);
            box.Append((uint)slot);
            box.Append((uint)layer);
            ShowBox(box);
            return 1;
        }
        byte mode = layer switch { 1 => 1, 2 => Feature(142) ? (byte)2 : (byte)1, _ => 0 };
        view.RequestEquip?.Invoke(item.Uid, (byte)selected, mode);
        return 1;
    }

    /// <summary>The part of CanEquipItem (inventory.cpp:1418) the port has (see <see cref="RequestEquipItem"/>): not an
    /// equipment item with model groups none of type 0 (FUN_14053c710): false; layer 2 without feature 0x8e or a skin type: box
    /// s_item_err_Invalid_slot_codi; layer 1 without a skin type: box 0xd3 (211). The slot is not read (its acceptance is
    /// STILL-OPEN, see <see cref="RequestEquipItem"/>).</summary>
    public bool CanEquipItem(Item item, int slot, int layer)
    {
        var model = Model();
        var d = item?.Data;
        if (model == null || d == null) return false;
        if (d.Type != 1 || model.Tables.ItemModels.Get(d.ItemPreset) is not { SlotTypes.Count: > 0 } m || m.SlotTypes.Contains(0)) return false;
        if (layer == 2 && (!Feature(0x8e) || !d.HasSkinType)) { ShowCommonBox(InventoryController.InvalidCodiSlotKey); return false; }
        if (layer == 1 && !d.HasSkinType) { ShowCommonBox(InventoryController.InvalidSlotKey); return false; }
        return true;
    }

    /// <summary>HandleUseBindEquipItemButton @0x1419fdf80 (uimessagebox.cpp:1885), the yes: FindItemByUid of the box's item
    /// uid, then RequestEquipItem(item, arg 0, arg 1, 1), the slot and mode the box kept; its result. Null when the inventory
    /// no longer has the item (the handler's 0).</summary>
    public int? ConfirmBindEquip(long uid, uint slot, uint mode)
    {
        var model = Model();
        if (View()?.FindByUid(uid) is not { } e || model == null) return null;
        var item = InventoryController.ToItem(e, model.Tables.Items);
        if (item == null) return null;
        return RequestEquipItem(item, (int)slot, (int)mode, true);
    }

    /// <summary>
    /// The bind box of RequestEquipItem (inventory.cpp:1245-1256) and UseItem (unit_140d1ae30_itemfunctiondispatch.cpp:509-518):
    /// CreateMessageBox(0x11 / 17, the key's text with the player's name, no listener, the tag), SetItemUid, SetItemEntry(item,
    /// FUN_140527b10 (the item's name), 1, 0, -1, -1, 1), SetButtonLabelIds(0x477, 0x478, 0). The equip box appends its slot
    /// and mode first.
    /// </summary>
    internal static MessageBoxData BindBox(UiModel model, Item item, string key, string tag)
    {
        string text = Ms2.Quest.ClientFormat.Format(model.Tables.Common(key) ?? "", model.Player.Name);
        var box = MessageBoxQueue.Create(0x11, text, null, tag);
        box.ItemUid = item.Uid;
        box.SetItemEntry(item, new ItemTooltip(model, 0).Name(item), 1, 0, -1, -1, 1, model.Tables.Items.TextColor);
        box.SetButtonLabelIds(0x477, 0x478, 0);
        return box;
    }

    void ShowCommonBox(string key) => ShowBox(MessageBoxQueue.Create(0, Model()?.Tables.Common(key) ?? "", null, ""));
}
