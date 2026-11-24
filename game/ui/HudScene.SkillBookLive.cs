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
}
