using System;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// MyPC::FUN_1415302e0 (@0x1415302e0, mypc.cpp:4445-4497): the cube item is locked (ConfirmPlacement's build notice 0x58,
/// cubecraft_2.cpp:955-959; also read by the cube palette, ugccubepalette.cpp:178, and the build validator,
/// ugcbuildvalidator.cpp:483).
/// <para>In order: no item data is locked; no housing row, money type 3, the pc's construction unlock (m_b15d4) or, in the
/// owner's house, the owner on the ignored-lock list (CGameRule::FUN_14047e150) is not locked. With feature 0xdb the item's
/// interiorLevel above the housing grade (the owner's in the owner's house, else the own) is locked; then, when its
/// category has an interior row, in the owner's house the same interiorLevel test again, else the row's requireHousingLevel
/// above the own grade. Last, an item with a trophy: in the owner's house the (trophy, grade) pair being in the owner's set
/// is locked; elsewhere the own grade of that trophy not cleared (CAchieveManager::IsGradeCleared) is locked.</para>
/// </summary>
public sealed class CubeLock
{
    /// <summary>The build notice for a locked item (cubecraft_2.cpp:957).</summary>
    public const int LockedNotice = 0x58;
    /// <summary>The feature the housing grade checks need (CFeatureManager::IsFeatureEnabled(0xdb)).</summary>
    public const int HousingLevelFeature = 0xdb;
    /// <summary>The money type that is never locked (ItemHousing priceCount == 3).</summary>
    public const int UnlockedMoneyType = 3;

    public HousingTable Housing;
    public CubeInteriorTable Interiors;
    public HousingState State = new();
    /// <summary>CGameRule::FUN_14047e150: the id is on the ignored construction lock list
    /// (GameEventIgnoreConstructionLockEvent::OnStart, gameevent.cpp:4952-4958; <see cref="ConstructionEvents"/>); null is
    /// the empty list.</summary>
    public Func<long, bool> IgnoredConstructionLock;
    public Func<int, bool> IsFeatureEnabled;
    /// <summary>CAchieveManager::IsGradeCleared (<see cref="AchieveBook.IsGradeCleared"/>); null clears nothing.</summary>
    public Func<int, int, bool> IsGradeCleared;

    /// <summary>The check for <paramref name="itemId"/>; <paramref name="hasItemData"/> is FUN_14053f870's row being there
    /// and <paramref name="constructionUnlocked"/> the pc's m_b15d4.</summary>
    public bool IsLocked(int itemId, bool hasItemData, bool constructionUnlocked)
    {
        if (!hasItemData) return true;
        var row = Housing?.Get(itemId);
        if (row == null) return false;
        if (row.MoneyType == UnlockedMoneyType) return false;
        if (constructionUnlocked) return false;
        var state = State;
        bool inHouse = state.InOwnersHouse;
        if (inHouse && IgnoredConstructionLock != null && IgnoredConstructionLock(state.OwnerId)) return false;
        ushort level = row.InteriorLevel;
        if (IsFeatureEnabled != null && IsFeatureEnabled(HousingLevelFeature))
        {
            if (inHouse) { if (level > state.OwnerGrade) return true; }
            else if (level > state.Grade) return true;
            if (Interiors?.RequireLevel(row.CategoryIndex) is { } require)
            {
                if (inHouse) { if (level > state.OwnerGrade) return true; }
                else if (require > state.Grade) return true;
            }
        }
        if (row.TrophyId > 0)
        {
            if (inHouse) { if (state.OwnerTrophies.Contains(((int)row.TrophyId, (int)row.TrophyLevel))) return true; }
            else if (IsGradeCleared == null || !IsGradeCleared((int)row.TrophyId, (int)row.TrophyLevel)) return true;
        }
        return false;
    }
}
