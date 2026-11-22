using System.Linq;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>The scripted live run's item lock hooks (MS2_LIVE_COMBAT_STEP, <see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does
/// what a click does, through the same handler.</summary>
public partial class HudScene
{
    /// <summary>The inventory's lock button (ToggleItemLock): true when the dialog is open afterwards.</summary>
    public bool LockOpenScripted()
    {
        if (!ItemLockOpen) ToggleItemLock();
        return ItemLockOpen;
    }

    /// <summary>The dialog's close (what Escape does).</summary>
    public void LockCloseScripted() { if (ItemLockOpen) itemLock.Close(); }

    /// <summary>The dialog's tab change (OnChangeTab): 0 lock, 1 unlock.</summary>
    public bool LockTabScripted(int tab)
    {
        if (!ItemLockOpen) return false;
        itemLock.ChangeTab(tab);
        return true;
    }

    /// <summary>The right click of a bag item with the dialog at the head: the item goes into the lock bag. True when it was sent.</summary>
    public bool LockSelectScripted(long uid)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (!ItemLockOpen || live?.Items.Get(uid) is not { } e) return false;
        int before = live.LockBook.HasLockedItem ? 1 : 0;
        itemLock.SelectFromInventory(InventoryController.ToItem(e, hudModel.Tables.Items));
        return before == 0;
    }

    /// <summary>The lock / unlock button and its confirm box answered yes. True when the box was asked.</summary>
    public bool LockConfirmScripted()
    {
        if (!ItemLockOpen) return false;
        bool asked = false;
        var old = itemLock.ShowData;
        itemLock.ShowData = d => { asked = true; d.Callback?.Invoke(0, d); };
        itemLock.OnCallback("onItemLock", System.Array.Empty<object>());
        itemLock.ShowData = old;
        return asked;
    }

    /// <summary>What the lock rules say of an item now: the test the trade, the shop sale and the dismantle run
    /// (ItemTradeRules.NotLocked), its lock state and the unlock time.</summary>
    public (bool NotLocked, byte State, long UnlockAt, long Now) LockRulesScripted(long uid)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.Items.Get(uid) is not { } e) return (false, 0, 0, 0);
        var item = InventoryController.ToItem(e, hudModel.Tables.Items);
        long now = live.GameSession?.ServerTime ?? 0;
        return (ItemTradeRules.NotLocked(item, now, out _), e.LockState, e.UnlockTime, now);
    }
}
