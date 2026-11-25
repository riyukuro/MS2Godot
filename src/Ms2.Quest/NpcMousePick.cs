using System.Numerics;

namespace Ms2.Quest;

/// <summary>
/// CMouseInteractNpc::FindTarget (mouseinteract.cpp:307, @0x140a79580), the npc a click with mouse control picks: over the
/// field's npcs in object id order, the first one that is not hidden (GameObject::FUN_14123c660), not an enemy (CNpc::vfunc_116,
/// the friendly value, not 0), has npc data, passes <see cref="Picks"/> and whose sphere the ray passes through
/// (<see cref="RayHitsSphere"/>; centre half the capsule height above its position, radius half the height).
/// </summary>
public static class NpcMousePick
{
    /// <summary>
    /// The pick test (mouseinteract.cpp:323-334): an npc with a function kind, with npc talks (HasNpcTalks) or with a quest mark
    /// (CNpc::GetQuestState, the shown mark's state, not 4) while talking is allowed, or one that can be held while holding is
    /// allowed. Talking is not allowed while a guide event restricts it (CGuideManager::IsRestricted(2)) or before the talk
    /// interval has passed (UITalkManager::IsTalkIntervalElapsed); holding not in state 0x20. Unlike the talk key
    /// (<see cref="NpcQuestMarks.HasTalk"/>) a quest the npc has a talk 200 for but no mark does not count.
    /// </summary>
    public static bool Picks(int kind, bool hasNpcTalks, int questState, bool holdable, bool talkAllowed, bool holdAllowed)
    {
        bool pick = false;
        if (kind != 0 && talkAllowed) pick = true;
        if (hasNpcTalks && talkAllowed) pick = true;
        if (questState != 4 && talkAllowed) pick = true;
        if (holdable && holdAllowed) pick = true;
        return pick;
    }

    /// <summary>
    /// FUN_14147ad10 (unit_14147a2a0.cpp:192): the ray from <paramref name="origin"/> along <paramref name="dir"/> (made unit)
    /// passes within <paramref name="radius"/> of <paramref name="center"/>; a centre behind the origin only when the origin is
    /// inside the sphere.
    /// </summary>
    public static bool RayHitsSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius)
    {
        var unit = Vector3.Normalize(dir);
        var to = center - origin;
        float proj = Vector3.Dot(to, unit);
        float sqr = Vector3.Dot(to, to);
        float r2 = radius * radius;
        if (proj < 0f && sqr > r2) return false;
        return sqr - proj * proj <= r2;
    }
}
