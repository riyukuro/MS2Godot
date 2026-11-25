using System.Linq;
using Ms2.Avm2;

namespace Ms2Godot.Ui;

// ---- the scripted live run's hooks on the skill window (MS2_LIVE_COMBAT_STEP skillgate): its slots' lock, the preview's
// point up through the movie's callback, and the skill tooltip's requirement lines

public partial class HudScene
{
    /// <summary>Opens the skill window when it is closed; whether it is up.</summary>
    public bool SkillBookOpenScripted()
    {
        if (skillBook == null || skillBook.IsClosed) OpenSkillBook();
        return skillBook != null && !skillBook.IsClosed;
    }

    /// <summary>The preview of the open window (null before its tab packet).</summary>
    Ms2.Ui.Skills.SkillPointSimulator SkillBookPreview => skillBook != null && !skillBook.IsClosed ? skillBook.Simulator : null;

    /// <summary>Whether the open window's tree is built (the tab packet came).</summary>
    public bool SkillBookTreeShown => SkillBookPreview != null && SkillBookSlotCount > 0;

    int SkillBookSlotCount => skillBook?.Host?.Child("m_treeSkills") is { } tree ? (int)Rt.ToNumber(skillBook.Host.Get(tree, "numChildren")) : 0;

    object SkillBookSlot(uint id)
    {
        var h = skillBook?.Host;
        if (h?.Child("m_treeSkills") is not { } tree) return null;
        int n = (int)Rt.ToNumber(h.Get(tree, "numChildren"));
        for (int i = 0; i < n; i++)
            if (h.Invoke(tree, "getChildAt", i) is { } s && (uint)Rt.ToNumber(h.Get(s, "m_skillID")) == id) return s;
        return null;
    }

    /// <summary>A tree slot: its m_disabled lock, the preview's level and learnt flag, CanLevelUp and the rank's remaining
    /// points; null when the tree has no such skill.</summary>
    public string SkillBookSlotState(uint id)
    {
        var sim = SkillBookPreview;
        if (sim == null || SkillBookSlot(id) is not { } slot) return null;
        var h = skillBook.Host;
        bool disabled = Rt.ToBoolean(h.Get(h.Get(slot, "m_skillSlot"), "m_disabled"));
        var e = sim.GetEditSkill(id);
        return $"disabled {disabled}, level {e?.Level ?? 0}, learnt {e?.Learned ?? false}, canLevelUp {sim.CanLevelUp(id)}, points {sim.GetRemainPoints(1)}";
    }

    /// <summary>The slot's plus button (the movie's onSkillPointUp); false when the window does not take it.</summary>
    public bool SkillBookPointUpScripted(uint id) => skillBook != null && !skillBook.IsClosed
        && skillBook.OnCallback("onSkillPointUp", new object[] { (double)id });

    /// <summary>The skill tooltip's requirement lines at the preview's level (text and color), as the window builds it.</summary>
    public string SkillBookRequireText(uint id)
    {
        if (skillBook?.SkillTooltip is not { } tip || SkillBookPreview?.GetEditSkill(id) is not { } e) return null;
        var o = tip(skillBook, id, e.Level);
        return o?.TryGetValue("requireDescription", out var r) == true && r is System.Collections.Generic.List<object> list
            ? string.Join(" | ", list.OfType<System.Collections.Generic.Dictionary<string, object>>().Select(x => $"{x["text"]} {x["color"]}"))
            : "none";
    }

    // ---- skillkey / skilldrag / skillreset / subjob ------------------------------------------------------------------------

    /// <summary>A learnt active (not passive) skill the open window's tree shows, on the quick slot page or not; 0 when none.</summary>
    public uint SkillBookPickScripted(bool onQuickSlots)
    {
        if (skillBook is not { IsClosed: false } c || c.Jobs == null) return 0;
        foreach (var name in c.GetBagSlotBindings(Ms2.Ui.Hud.SkillBookTreeController.BagName))
        {
            uint id = c.BookSkillId(name);
            if (id == 0 || !c.Jobs.IsLearned(id) || hudModel.Tables.Skills.GetSkillData(id) is not { IsPassive: false }) continue;
            if (SkillBookSlot(id) == null) continue;        // in the bag, not drawn in the shown tree
            if ((QuickSlotOfSkill(id) >= 0) == onQuickSlots) return id;
        }
        return 0;
    }

    /// <summary>The slot's right click: its SkillSlot (m_skillSlot) gets the "rightClick" mouse event its configUI listens to (onRegisterShortcutkey).</summary>
    public bool SkillBookRightClickScripted(uint id)
    {
        if (SkillBookSlot(id) is not { } slot || skillBook.Host.Get(slot, "m_skillSlot") is not Ms2.Avm2.AsObject skillSlot) return false;
        var h = skillBook.Host;
        h.Player.Call(skillSlot, "dispatchEvent", new object[] { h.RT.GetClass("flash.events.MouseEvent").Construct("rightClick", true, false) });
        return true;
    }

    /// <summary>The book slot of the skill dragged onto quick slot <paramref name="slot"/> (NotifyDragSlot::SlotDragEnd over the
    /// HUD's binding map, as the movie's notifySlotDragEnd).</summary>
    public bool SkillBookDragScripted(uint id, int slot)
    {
        if (skillBook is not { IsClosed: false } c || skillBookMovie == null) return false;
        var name = c.GetBagSlotBindings(Ms2.Ui.Hud.SkillBookTreeController.BagName).FirstOrDefault(n => c.BookSkillId(n) == id);
        if (name == null) return false;
        Ms2.Ui.NotifyDragSlot.SlotDragEnd(skillBookMovie.Host.DragSlots, new object[] { name, Ms2.Ui.Hud.MainPlayerInfoController.SlotBag + "." + slot });
        return true;
    }

    /// <summary>The quick slot of the current page that holds the skill (CKeyTable::FindQuickSlotBySkillAndItemUid), -1 when none.</summary>
    public int QuickSlotOfSkill(uint id) => hudModel.ServerKeys?.FindQuickSlotBySkillAndItemUid((int)id, 0) ?? -1;

    /// <summary>The skill the quick slot bar shows on slot <paramref name="slot"/>.</summary>
    public int QuickSlotShownSkill(int slot) => playerInfo?.ShownEntry(slot).SkillId ?? 0;

    /// <summary>An empty quick slot of the bar that takes a drop (not key bound, 0..21), -1 when none.</summary>
    public int QuickSlotEmptyScripted()
    {
        for (int i = 0; i < 22; i++) if (playerInfo != null && playerInfo.ShownEntry(i).IsEmpty) return i;
        return -1;
    }

    /// <summary>The quick slot's right click (SkillSlot::vfunc_14 / vfunc_17): its entry taken off.</summary>
    public void QuickSlotRemoveScripted(int slot) => playerInfo?.RemoveSlot(slot);

    /// <summary>A skill of the open window that can take a point now (the preview's CanLevelUp), 0 when none.</summary>
    public uint SkillBookLevelableScripted()
    {
        if (skillBook is not { IsClosed: false } c || SkillBookPreview is not { } sim) return 0;
        foreach (var name in c.GetBagSlotBindings(Ms2.Ui.Hud.SkillBookTreeController.BagName))
            if (c.BookSkillId(name) is var id and > 0 && sim.CanLevelUp(id)) return id;
        return 0;
    }

    /// <summary>A movie button of the window by its callback (onApplySkill, onResetSkill, onChangeSubJob with the rank index).</summary>
    public bool SkillBookCallbackScripted(string name, params object[] args) => skillBook is { IsClosed: false } c && c.OnCallback(name, args);

    /// <summary>The window waits for the server's answer (m_nWaitServer).</summary>
    public bool SkillBookWaiting => skillBook is { IsClosed: false, WaitServer: true };

    /// <summary>The rank the window shows (1 or 2), then the tree's skills and the job ranks of their rows ("rank 2, tree 15
    /// skills of ranks 2").</summary>
    public string SkillBookRankScripted()
    {
        if (skillBook is not { IsClosed: false } c || c.Host?.Child("m_treeSkills") is not { } tree) return null;
        var h = c.Host;
        var ranks = new System.Collections.Generic.SortedSet<int>();
        int n = (int)Rt.ToNumber(h.Get(tree, "numChildren"));
        for (int i = 0; i < n; i++)
            if (h.Invoke(tree, "getChildAt", i) is { } slot && c.Jobs?.Job?.FindSkill((uint)Rt.ToNumber(h.Get(slot, "m_skillID"))) is { } row)
                ranks.Add(row.JobLevel);
        return $"rank {c.JobLevel}, tree {n} skills of ranks {string.Join(",", ranks)}";
    }

    /// <summary>The player's job rank (JobTable::GetJobRank of the job system's sub job), 0 without the window.</summary>
    public int SkillBookJobRankScripted => skillBook is { IsClosed: false, Jobs: { } j } ? Ms2.Ui.Skills.JobTable.GetJobRank(j.SubJob) : 0;

    /// <summary>The level of a skill in the live session's book (0 when not learnt).</summary>
    public int BookLevelScripted(uint id) => Ms2Godot.Net.LiveSession.Instance?.SkillBook?.Skills.FirstOrDefault(s => s.SkillId == id) is { Learned: true } b ? b.Level : 0;
}
