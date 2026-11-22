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
        if (levelUpGuide is { IsClosed: false }) levelUpGuide.Close();     // the level-up guide window opens over the field and hides the shots
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
        if (inventory is { IsClosed: false }) inventory.Close();     // the window would cover the field in the shots
        return true;
    }

    /// <summary>The click on the bag cell of item <paramref name="itemId"/> as the movie raises it: notifySlotUse(slot, button), button 0 the
    /// double click, 1 the right click. Opens the inventory (and leaves it open). False without the item or the inventory.</summary>
    public bool SlotClickScripted(int itemId, int button)
    {
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.SlotNameOfItem(itemId) is not { } name) return false;
        return inventory.OnCallback("notifySlotUse", new object[] { name, button });
    }

    /// <summary>The saved tutorial step (save id 110) set as the server's guide record would: the events up to it are cleared, the next start.</summary>
    public bool GuideStepScripted(int step)
    {
        if (Ms2.Ui.Guide.GuideManager.Current is not { } g) return false;
        g.ReceiveStates(new Ms2.Net.Packets.GuideRecordIn(new[] { (110, step) }, 0));
        return true;
    }

    /// <summary>Opens the inventory (the guide's InventoryDialog occurrence) when it is shut; the character window through the same menu route.</summary>
    public bool OpenInventoryScripted() { if (inventory == null || inventory.IsClosed) OpenInventory(); return inventory is { IsClosed: false }; }

    /// <summary>Opens the character window through the menu's route (OpenMyInfo); whether it is up afterwards.</summary>
    public bool OpenCharScripted() { OpenMyInfo(); return myInfoMovie != null; }

    /// <summary>The guide's running events and held limits, for the log.</summary>
    public string GuideStateText() => Ms2.Ui.Guide.GuideManager.Current?.DescribeState() ?? "no guide manager";

    /// <summary>The worn item <paramref name="itemId"/> taken off: the inventory view's unequip request, as the character window's
    /// take-off sends it. False when it is not worn.</summary>
    public bool UnequipScripted(int itemId)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.Me.Equips?.FirstOrDefault(e => e.Id == itemId) is not { } worn || live.Inventory?.RequestUnequip == null) return false;
        live.Inventory.RequestUnequip(worn.Uid);
        return true;
    }

    /// <summary>The player turned to face <paramref name="target"/> (a world position) on the flat, as a turn before a press.
    /// Returns the distance to it, or -1 without a player.</summary>
    public float FaceScripted(Vector3 target)
    {
        if (player == null) return -1;
        var d = target - player.GlobalPosition; d.Y = 0;
        player.Face(d);
        return d.Length();
    }

    /// <summary>The Escape key pressed, through the dialog key handler. True when a dialog took it.</summary>
    public bool EscScripted() =>
        DialogKeys(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true, Echo = false });
}
