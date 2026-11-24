using System;

namespace Ms2.Ui.Hud;

/// <summary>The own level-up checks of CSceneStateGame::OnLevelUp (@0x14171aec0, scenestategame.cpp:9511).</summary>
public static class LevelUpNotices
{
    /// <summary>
    /// The skill point notice (:9553-9561): the PC's level (already the new one) at least 10, a rise, and the level at most
    /// max(SkillPointLimitLevel1, SkillPointLimitLevel2), the levels that still give skill points
    /// (JobSystem::GetLevelSkillPoint).
    /// </summary>
    public static bool SkillPointNotice(int level, int oldLevel, int limit1, int limit2) =>
        level >= 10 && level > oldLevel && level <= Math.Max(limit1, limit2);
}
