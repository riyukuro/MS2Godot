using System.Collections.Generic;
using System.Linq;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>The scripted live run's item enchant hooks (MS2_LIVE_COMBAT_STEP, <see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does
/// what a click does, through the same handler.</summary>
public partial class HudScene
{
    /// <summary>The talk's open-dialog action with the mode by name (empty: the Ophelia mode): true when the window is open afterwards.
    /// The scripted open has no NPC talk behind it (the client opens the dialog from a smith's talk), so the talk companion
    /// packet of a select carries no NPC.</summary>
    public bool EnchantOpenScripted(string mode)
    {
        OnTalkDialogOpen(ItemEnchantDialogController.Bag, mode);
        if (!ItemEnchantOpen) OpenItemEnchant();
        return ItemEnchantOpen;
    }

    public void EnchantCloseScripted() { if (ItemEnchantOpen) itemEnchant.Close(); }

    /// <summary>The uids in the window's enchantable bag, in its order (bag first, then the worn layer-0 equips).</summary>
    public List<long> EnchantBagScripted() => ItemEnchantOpen ? itemEnchant.EnchantableItems().Select(i => i.Uid).ToList() : new List<long>();

    /// <summary>The slot click (onSelectItem): the select request and its box. True when the window was open.</summary>
    public bool EnchantSelectScripted(long uid)
    {
        if (!ItemEnchantOpen) return false;
        itemEnchant.OnCallback("onSelectItem", new object[] { ItemEnchantDialogController.BagName(uid) });
        return true;
    }

    /// <summary>The enchant button (onEnchant) with every confirm box answered yes; the boxes asked are returned.</summary>
    public int EnchantPressScripted()
    {
        if (!ItemEnchantOpen) return -1;
        int asked = 0;
        var old = itemEnchant.ShowData;
        itemEnchant.ShowData = d => { asked++; d.Callback?.Invoke(0, d); };
        itemEnchant.OnCallback("onEnchant", System.Array.Empty<object>());
        itemEnchant.ShowData = old;
        return asked;
    }

    /// <summary>What the book holds now: the selected uid, its state, the result item and the three rates.</summary>
    public (long Selected, int State, bool Ready, int ResultId, float Chance, int Ingredients) EnchantStateScripted()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var b = live?.EnchantBook;
        return b == null ? default : (b.SelectedUid, b.State, b.Ready, b.ResultItemId, itemEnchant?.Chance ?? 0, b.Ingredients.Count);
    }

    /// <summary>The selected item's costs as the server sent them, each as the item it names (CIngredientInfo::GetItemId) and its count.</summary>
    public List<(int ItemId, int Count)> EnchantCostsScripted()
    {
        var b = Ms2Godot.Net.LiveSession.Instance?.EnchantBook;
        var r = new List<(int, int)>();
        if (b != null) foreach (var g in b.Ingredients) r.Add((g.ItemId != 0 ? g.ItemId : hudModel.Tables.Items.TagItemId(g.Tag), g.Count));
        return r;
    }
}
