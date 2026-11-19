using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The adventure level data the client keeps in process globals, so it outlives a field change (CSceneStateGame::OnPrestige,
/// scenestategame.cpp:8815-8955): the exp in this level (DAT_143ab2958), the level at or below which a level shows full
/// (DAT_143ab2960), the exp shown for the level above it (DAT_143ab2968), the claimed level rewards (DAT_143ab3308) and
/// the missions (DAT_143ab3318). A live session owns one and applies every Prestige packet to it on arrival
/// (<see cref="Apply"/>); each HUD's <see cref="PlayerState"/> reads the session's (<see cref="PlayerState.Adventure"/>).
/// The adventure level itself is the PC's (vfunc_141), not one of these.
/// </summary>
public sealed class AdventureData
{
    public long Exp, NextExp;
    public int FilledLevel;
    /// <summary>A std::set: kind 0 replaces it, kind 4 adds to it.</summary>
    public readonly SortedSet<uint> Claimed = new();
    /// <summary>A std::map by mission id: the progress count and the claimed flag; kinds 6 and 7 replace it.</summary>
    public readonly SortedDictionary<uint, (uint Progress, bool Complete)> Missions = new();

    /// <summary>ServerEnter (login, character switch, channel change): CSceneStateGame::OnServerEnter (scenestategame.cpp:2070)
    /// first runs FUN_14092f420 (unit_14092f420_accountsessionstate.cpp:292-311), which clears the missions alone; the other
    /// values stay until a kind 0 replaces them.</summary>
    public void ServerEnter() => Missions.Clear();

    /// <summary>
    /// The globals OnPrestige writes for <paramref name="m"/>:
    /// <list type="bullet">
    /// <item>1: the exp (:8825);</item>
    /// <item>2 for the local player (<paramref name="ownOldLevel"/> is its level before the packet, null for another PC
    /// or none found): a first level (old 0) fills level 1 (:8856);</item>
    /// <item>0: the exp, the filled level, the next exp and the claimed set (:8867-8870);</item>
    /// <item>4: a claim with result 1 adds its levels (:8876);</item>
    /// <item>7 with feature 0xfb (<paramref name="missionFeature"/>), and 6 with the feature and result 1: the missions are
    /// replaced (:8901-8915, :8927-8943).</item>
    /// </list>
    /// </summary>
    public void Apply(PrestigeMessage m, int? ownOldLevel, bool missionFeature)
    {
        switch (m)
        {
            case PrestigeMessage.ExpGain g:
                Exp = g.Value;
                break;
            case PrestigeMessage.LevelUp:
                if (ownOldLevel == 0) FilledLevel = 1;
                break;
            case PrestigeMessage.Loaded l:
                Exp = l.Exp;
                FilledLevel = l.FilledLevel;
                NextExp = l.NextExp;
                Claimed.Clear();
                Claimed.UnionWith(l.Claimed);
                break;
            case PrestigeMessage.ClaimResult c:
                if (c.Result == 1) Claimed.UnionWith(c.Levels);
                break;
            case PrestigeMessage.MissionList ml:
                if (!missionFeature || ml.Result != 1) break;
                Missions.Clear();
                foreach (var (id, v) in ml.List) Missions[id] = v;
                break;
        }
    }
}
