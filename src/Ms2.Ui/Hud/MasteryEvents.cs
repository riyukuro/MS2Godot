using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The decisions CSceneStateGame::OnMastery (scenestategame.cpp:3920) and the damage paths make for the mastery dialog:
/// which of the dialog, its tabs and the game menu's 0x4f1e count are redone, whether a hit cancels the craft, and which
/// damage records the own player's OnDamagedMyPC gets. The HUD and the live session call these, so the rules are tested
/// without a movie or a field.
/// </summary>
public static class MasteryEvents
{
    /// <summary>What one Mastery message redraws (UIMasteryDialog::FUN_140eb4c10 / FUN_140eb4be0, UIGameMenuDialog::
    /// FUN_141938190). <see cref="LevelGuide"/> is the reward's AddLevelGuide.</summary>
    public readonly record struct Update(bool Dialog, bool Tabs, bool GameMenu, bool LevelGuide, int Type = -1, int Grade = -1);

    /// <summary>
    /// Mode 0 (the exp change): the mastery dialog's own types only — 1 (fishing), 2 (instrument) and 0xb (pet taming)
    /// are other systems' and skip it; the tabs are redone only on a grade up and the game menu's count then too. Types 5
    /// and 6 also refresh the housing cubes and the field's category 6 objects (STILL-OPEN).
    /// </summary>
    public static Update OnExpChanged(int type, bool gradeUp) =>
        type is 1 or 2 or 0xb ? default : new(true, gradeUp, gradeUp, false);

    /// <summary>Mode 1 (a grade reward): AddLevelGuide, then FUN_140eb4be0 (the tabs, no pending sub); the game menu's
    /// count is always redone.</summary>
    public static Update OnRewardReceived(uint rewardId) =>
        new(true, true, true, true, MasteryRecipeTable.TypeFromID(rewardId), MasteryRecipeTable.GradeFromID(rewardId));

    /// <summary>Mode 2 (a craft done): SetPendingSub, no redraw of the tabs.</summary>
    public static Update OnCrafted(int type) => new(true, false, false, false);

    /// <summary>OnDamagedMyPC (@0x140eb5320): a negative total damage (a hit) cancels the craft.</summary>
    public static bool CancelsCraft(long totalDamage) => totalDamage < 0;

    /// <summary>
    /// The damage records of a SkillDamage (0x3D) that hit the own player, one total per target record (MyPC::vfunc_113
    /// posts NotifyDamagedMyPC for each). The client's path drops a mode 3 record whose caster is not a PCBase in the field
    /// (OnDotDamage: FUN_141bd1200(nOwnerId) == 0, FindObjectById then DynamicCast&lt;PCBase&gt; — players, npcs and pets),
    /// a mode 5 packet when the region skill object is gone (OnRegionSkillDamage: GetRegionSkillObject(nRegionId) == 0),
    /// and a mode 6 record whose own object (the record's id, the port's HitTarget) is not in the field (OnSplashDamage:
    /// FindObjectById(nOwnerId) == 0, a test per record); mode 1 has no such test. Mode 0 is a cast, not damage, and the
    /// undecoded modes are dropped.
    /// </summary>
    public static List<long> SelfRecords(SkillDamage d, int selfObject, Func<int, bool> isPcBaseObject, Func<int, bool> objectExists,
        Func<int, bool> isRegionSkill)
    {
        var totals = new List<long>();
        if (d == null || d.Raw != null || selfObject <= 0) return totals;
        switch (d.Mode)
        {
            case 1:
                break;
            case 3:
                if (isPcBaseObject == null || !isPcBaseObject(d.OwnerId)) return totals;
                break;
            case 5:
                if (isRegionSkill == null || !isRegionSkill(d.RegionId)) return totals;
                break;
            case 6:
                // one record per target: its own object is the record's id (the reader sets no owner for the mode)
                foreach (var t in d.Targets)
                    if (t.TargetId == selfObject && (objectExists == null || objectExists(t.TargetId)))
                        totals.Add(t.Hits.Sum(h => h.Damage));
                return totals;
            default:
                return totals;
        }
        foreach (var t in d.Targets)
            if (t.TargetId == selfObject)
                totals.Add(t.Hits.Sum(h => h.Damage));
        return totals;
    }
}
