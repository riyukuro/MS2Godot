using System.Linq;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>The scripted live combat run's hooks (MS2_LIVE_COMBAT_STEP, <see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does what
/// a key press or a button click does, through the same handler.</summary>
public partial class HudScene
{
    /// <summary>The attack key pressed: the key bound to the attack action (0x49401) goes the way <see cref="CastKeys"/> sends it.
    /// True when the press gave a cast request.</summary>
    public bool AttackScripted()
    {
        if (hudModel?.Keys is not { } keys) return false;
        for (int dik = 1; dik < 0xd4; dik++)
            if (keys.IsKeyBoundToAction(dik, 0x49401)) return CastKey(dik) == true;
        return false;
    }

    /// <summary>Whether the revival dialog or the auto-revive countdown is open (the own death's dialogs).</summary>
    public bool RevivalUiUp() => RevivalOpen || (autoRevive is { IsClosed: false });

    /// <summary>The revival dialog's "onRevival(button)" callback, as its movie raises it. False without the dialog.</summary>
    public bool ReviveScripted(int button) => RevivalOpen && revival.OnCallback("onRevival", new object[] { button });

    /// <summary>The inventory's equip action on the bag item <paramref name="itemId"/> (the right click's RequestEquipItem(item, 0, 0)).
    /// False without the item or the inventory.</summary>
    public bool EquipScripted(int itemId)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.Items.All.FirstOrDefault(e => e.Id == itemId) is not { } e) return false;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.FindItem(e.Uid) is not { } item) return false;
        inventory.RequestEquipItem(item, 0, 0);
        return true;
    }

    /// <summary>The worn item <paramref name="itemId"/> taken off: the inventory view's unequip request, as the character window's
    /// take-off sends it. False when it is not worn.</summary>
    public bool UnequipScripted(int itemId)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.Me.Equips?.FirstOrDefault(e => e.Id == itemId) is not { } worn || live.Inventory?.RequestUnequip == null) return false;
        live.Inventory.RequestUnequip(worn.Uid);
        return true;
    }
}
