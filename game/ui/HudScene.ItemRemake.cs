using Godot;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The Change Attributes window: UIItemRemakeOptionDialog (0x4eeb) as <see cref="ItemRemakeOptionDialogController"/>, fed by the
/// ChangeAttributes packets. The inventory's onClickItemRemakeOption (MyPC::FUN_141528970) and the NPC talk's open-dialog action
/// ("ItemRemakeOptionDialog") open it. Live only. STILL-OPEN: the talk script that FUN_141528970 begins (npc 0xa7dabc kind 0x1e,
/// BeginTalkWithoutNpc): the window opens without a talk; the equipment refresh after a worn item is stored (FUN_1411dd280,
/// FUN_1411e7660).
/// </summary>
public partial class HudScene
{
    ItemRemakeOptionDialogController itemRemake;   // from open until the close
    MovieDialog itemRemakeMovie;                   // the movie, from open until the unload

    bool ItemRemakeOpen => itemRemake != null && !itemRemake.IsClosed;

    /// <summary>The talk's open-dialog action for "ItemRemakeOptionDialog" (UITalkManager::FUN_141b1eb90).</summary>
    bool ItemRemakeTalk(string dialog)
    {
        if (!string.Equals(dialog, ItemRemakeOptionDialogController.Bag, StringComparison.OrdinalIgnoreCase)) return false;
        OpenItemRemake();
        return true;
    }

    void OpenItemRemake()
    {
        if (ItemRemakeOpen) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) { GD.Print("item remake: needs the live session"); return; }
        if (itemRemakeMovie != null) { itemRemakeMovie.Host.Unload(); itemRemakeMovie.QueueFree(); itemRemakeMovie = null; }
        var table = hudModel.Tables.Items;
        var c = new ItemRemakeOptionDialogController
        {
            Send = live.SendChangeAttributes,
            FindItem = uid => ItemEnchantDialogController.FindHeld(uid, live.Items.Get, live.Me.Equips, table),   // FUN_1419be700
            BagItems = cat => BagItemsOf(live, cat, table),
            WornItems = () => ItemEnchantDialogController.WornLayerItems(live.Me.Equips, table, Ms2Godot.Net.LiveSession.WornLayer),
            CountByTag = (tag, rarity) => (int)Ms2.Ui.Shop.ShopController.CountItemsByTag(live.Items.All, table, tag, itemUse.Error, rarity),
            Meso = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0,
            AbilityRate = () => live.OwnAbilityRate(0xa9),                                                        // GetAbilityRate(0xa9)
            Store = item => StoreRemade(live, item),
        };
        c.NoticeText += SystemNoticeText;
        c.ChatLine += (text, kind) => ChatLog.AddSystemLine(text, kind);
        c.PlaySound += key => FieldAudio.Instance?.PlayUi(key);
        c.TalkModify += type => live.SendTalk(new NpcTalkModifyRequest((short)type));                       // vfunc_85
        c.Unloaded += () =>
        {
            itemRemakeMovie?.Host.Unload(); itemRemakeMovie?.QueueFree(); itemRemakeMovie = null;
        };
        itemRemake = c;
        BindBoxAnswers();
        itemRemakeMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item remake: load={itemRemakeMovie.Loaded}");
        uiRoot.AddChild(itemRemakeMovie);
    }

    /// <summary>FUN_1412a1650: an inventory item goes back through ReplaceItem (the inventory redraws); a worn one takes the
    /// worn slot's place. STILL-OPEN: the equipment refresh after a worn store.</summary>
    static void StoreRemade(Ms2Godot.Net.LiveSession live, Item item)
    {
        if (item?.Record is not { } record) return;
        if (live.Items.Get(item.Uid) != null) { live.Items.Apply(new ItemMessage.Update(item.Uid, record)); return; }
        if (Replace(live.Me.Equips as List<EquipItem>)) return;
        Replace(live.Me.Outfit2 as List<EquipItem>);
        bool Replace(List<EquipItem> list)
        {
            if (list == null) return false;
            int i = list.FindIndex(e => e != null && e.Uid == item.Uid);
            if (i < 0) return false;
            list[i] = record;
            return true;
        }
    }

    Action unwatchItemRemake;

    /// <summary>UIItemRemakeOptionDialog::vfunc_79 (@0x14129daa0), the dialog's opcode 200 entry: the answer goes to the open
    /// dialog, which reads the item bodies itself. STILL-OPEN: the router that hands the packet to the dialog; with none open
    /// nothing reads it here.</summary>
    void WatchItemRemake()
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        void On(ChangeAttributesPacket p, Maple2.PacketLib.Tools.ByteReader r)
        {
            if (!ItemRemakeOpen) return;
            itemRemake.OnPacket(p, held => ItemReader.ReadBodyCopy(r, held, live.GameProfile));
        }
        live.ChangeAttributesReceived += On;
        unwatchItemRemake = () => live.ChangeAttributesReceived -= On;
    }

    // ---- the scripted live run's hooks (LiveSession.CombatRun's remake steps) ----------------------------------------

    /// <summary>The window is open with its movie loaded.</summary>
    public bool ItemRemakeOpenScripted() => ItemRemakeOpen && itemRemakeMovie?.Loaded == true;

    /// <summary>The inventory's Change Attributes button (its movie's onClickItemRemakeOption, the bag opened first).</summary>
    public bool ItemRemakeButtonScripted()
    {
        if (!OpenInventoryScripted() || inventoryMovie?.Host is not { } h) return false;
        inventoryMovie.Player.External?.Invoke(h.Id, new object[] { "onClickItemRemakeOption" });
        return true;
    }

    /// <summary>One of the window movie's callbacks (onSelectItem with the uid, onClickRemake with the uid and the lock option,
    /// onClickApply with the pick) as the movie raises it; false while the window is shut.</summary>
    public bool ItemRemakeCallScripted(params object[] call)
    {
        if (!ItemRemakeOpenScripted()) return false;
        itemRemakeMovie.Player.External?.Invoke(itemRemakeMovie.Host.Id, call);
        return true;
    }

    /// <summary>The window's state for the run's log: the state, the picked item and the new roll, the option count, the input lock.</summary>
    public (bool Open, int State, long Cur, long New, int Options, bool Locked) ItemRemakeStateScripted() =>
        ItemRemakeOpen ? (true, itemRemake.State, itemRemake.Cur?.Uid ?? 0, itemRemake.New?.Uid ?? 0, itemRemake.OptionCount, itemRemake.InputLocked) : default;

    /// <summary>The item a cost's tag names (CIngredientInfo::GetItemId), for the run's GM gifts.</summary>
    public int TagItemIdScripted(int tag) => hudModel.Tables.Items.TagItemId(tag);

    /// <summary>The window closed as its close button closes it.</summary>
    public void ItemRemakeCloseScripted() { if (ItemRemakeOpen) itemRemake.Close(); }

    /// <summary>UIItemRemakeOptionDialog's key handler: Escape closes it, except while a remake is out (state 3 with the
    /// animation), when the key is swallowed. vfunc_13 (:345) also swallows the action key 0x493e2 then, but its 1 only ends
    /// the UI key pass (UISwfEntityManager::FUN_1422d1c20, uiswfentitymanager.cpp:321) for the dialogs after it; GFxService::OnTick
    /// (gfxservice.cpp:198) drops the result and the field's action key does not read it, so the key is left to the rest here.</summary>
    bool ItemRemakeKeys(InputEventKey k)
    {
        if (!ItemRemakeOpen || k.Keycode != Key.Escape) return false;
        if (!itemRemake.SwallowsKeys) itemRemake.Close();
        return true;
    }
}
