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

    /// <summary>The open revival dialog's paid mode, its meso and merat costs and its two labels; null without the dialog.</summary>
    public string RevivalState() => RevivalOpen
        ? $"mode {revival.PaidMode}, meso cost {revival.MesoCost}, merat cost {revival.MeratCost}, labels \"{revival.Label1}\" / \"{revival.Label2}\""
        : null;

    /// <summary>The revival dialog's "onRevival(button)" callback, as its movie raises it. False without the dialog.</summary>
    public bool ReviveScripted(int button) => RevivalOpen && revival.OnCallback("onRevival", new object[] { button });

    /// <summary>The inventory's equip action on the bag item <paramref name="itemId"/> (the right click's RequestEquipItem(item, 0, 0)).
    /// False without the item or the inventory.</summary>
    public bool EquipScripted(int itemId, bool another = false)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var worn = another ? live?.Me.Equips?.Select(w => w.Uid).ToHashSet() : null;     // a second copy: the one not worn yet
        if (live?.Items.All.FirstOrDefault(e => e.Id == itemId && worn?.Contains(e.Uid) != true) is not { } e) return false;
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
        g.ClearStates();     // the account's own record holds save id 1 (everything cleared)
        g.ReceiveStates(new Ms2.Net.Packets.GuideRecordIn(new[] { (110, step) }, 0));
        g.QueueOccurrence(Ms2.Ui.Guide.GuideOccurrence.GuideClear, 0);     // the open conditions are tried on an occurrence
        return true;
    }

    /// <summary>Opens the inventory (the guide's InventoryDialog occurrence) when it is shut; the character window through the same menu route.</summary>
    public bool OpenInventoryScripted() { if (inventory == null || inventory.IsClosed) OpenInventory(); return inventory is { IsClosed: false }; }

    /// <summary>Shuts every dialog, then opens one of the everyday windows the way its menu button does (inventory: the guide's
    /// InventoryDialog occurrence, quest: the quest log toggle, sysmenu: the system menu); whether it is open. A live shot of the
    /// same window in two builds compares how the whole swf UI draws.</summary>
    /// <summary>The quest log's give-up button for a quest: the log opened when it is shut, then its movie's onGiveUpQuest
    /// (GiveUpQuest, uiquestdialog.cpp:1792); whether the open log took it.</summary>
    public bool QuestLogGiveUpScripted(int questId)
    {
        if (questLog == null || questLogSide == null || questLogSide.IsClosed) OpenQuestLog();
        if (questLog?.Host == null || questLogSide is not { IsClosed: false }) return false;
        questLog.Player.External?.Invoke(questLog.Host.Id, new object[] { "onGiveUpQuest", questId });
        return true;
    }

    /// <summary>Whether the minimap's mark of a portal shows; null without the minimap or the mark.</summary>
    public bool? MinimapPortalMarkScripted(int portalId) => minimapSide?.PortalMarkShown(portalId);

    public bool OpenWindowScripted(string name)
    {
        CloseAllDialogs();
        switch (name)
        {
            case "inventory": return OpenInventoryScripted();
            case "quest": ToggleQuestLog(); return questLogSide is { IsClosed: false };
            case "sysmenu": OpenSystemMenu(); return systemMenu is { IsClosed: false };
        }
        return false;
    }

    /// <summary>The open inventory switched to a tab (<see cref="Ms2.Ui.Hud.InventoryController.ShowTab"/>); whether it shows it.</summary>
    public bool InventoryTabScripted(int tab) => inventory is { IsClosed: false } inv && inv.ShowTab(tab);

    /// <summary>Opens the character window through the menu's route (OpenMyInfo); whether it is up afterwards.</summary>
    public bool OpenCharScripted() { OpenMyInfo(); return myInfoMovie != null; }

    /// <summary>The guide event's state for the run: 0 idle, 1 opening, 2 open (GuideEntity m_nState), 3 cleared; -1 without
    /// the guide or the event.</summary>
    public int GuideEventScripted(int id)
    {
        if (Ms2.Ui.Guide.GuideManager.Current is not { } g || g.FindEntity(id) is not { } e) return -1;
        return g.IsCleared(id) ? 3 : e.State;
    }

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

    /// <summary>The own cast of skill <paramref name="skillId"/> through the request and route a quick slot click takes
    /// (<see cref="CastOwn"/>), without the slot. True when the request was not refused.</summary>
    public bool CastSkillScripted(uint skillId)
    {
        var w = QuickSlotWorld();
        var held = w.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        if (w.Tables.GetSkillData(skillId) is { } sd)
            GD.Print($"live: combat skill {skillId} weapons {string.Join(' ', sd.Weapons.Select(x => $"R{x.RightHand}/L{x.LeftHand}"))}, held R{w.Weapon?.Right}/L{w.Weapon?.Left}");
        if (RequestOwn(w, skillId, held, 0) is not { } r) return false;
        RouteOwn(r);
        return true;
    }

    /// <summary>The quick slots' cooldown answer for skill <paramref name="skillId"/>: each slot holding it, whether the slot
    /// shows the skill's running cooldown (<see cref="Ms2.Ui.Skills.SkillSlotState.NotifyCooldownStart"/>, the check that runs
    /// the slot's onCooldownStart) with its time left and total; the same check without a slot when none holds it.</summary>
    public string CooldownScripted(uint skillId)
    {
        var w = QuickSlotWorld();
        if (w.Tables.GetSkillData(skillId) is not { } sd) return "no skill data";
        string One(Ms2.Ui.Skills.SkillSlotState s) =>
            s.NotifyCooldownStart(sd.CooldownKey, out float remain, out float total) ? $"shown {remain:F2}/{total:F2}" : "not shown";
        var parts = new System.Collections.Generic.List<string> { $"useCoolDownShowUI {sd.Condition.UseCoolDownShowUI}" };
        for (int i = 0; playerInfo != null && i < Ms2.Ui.Hud.MainPlayerInfoController.SlotCount; i++)
            if (playerInfo.ShownEntry(i) is var e && e.SkillId == (int)skillId)
                parts.Add($"slot {i}: {One(new Ms2.Ui.Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid))}");
        if (parts.Count == 1) parts.Add($"no slot holds it; without a slot: {One(new Ms2.Ui.Skills.SkillSlotState(w, skillId, 0, 0))}");
        return string.Join(", ", parts);
    }

    /// <summary>The Escape key pressed, through the dialog key handler. True when a dialog took it.</summary>
    public bool EscScripted() =>
        DialogKeys(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true, Echo = false });

    /// <summary>Harness: the player's move input driven by the script (x right, y forward, camera relative); false without a player.</summary>
    public bool DriveScripted(Vector2 input, bool walk)
    {
        if (player == null) return false;
        player.ScriptDriven = true; player.Input2 = input; player.WalkMode = walk;
        return true;
    }

    /// <summary>Harness: the camera-relative move input (the inverse of Player.WorldDir) that runs the player toward
    /// <paramref name="target"/> (a world position) on the flat. Returns the flat distance, or -1 without a player.</summary>
    public float DriveTowardScripted(Vector3 target, bool walk)
    {
        if (player == null) return -1;
        var d = target - player.GlobalPosition; d.Y = 0;
        float len = d.Length();
        if (len < 1f) { DriveScripted(Vector2.Zero, walk); return len; }
        d /= len;
        var fwd = new Vector3(-Mathf.Sin(player.CameraYaw), 0, -Mathf.Cos(player.CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        DriveScripted(new Vector2(d.Dot(right), d.Dot(fwd)), walk);
        return len;
    }

    /// <summary>Harness: gives the player back to the keyboard.</summary>
    public void DriveEnd() { if (player != null) { player.Input2 = Vector2.Zero; player.ScriptDriven = false; } }
}
