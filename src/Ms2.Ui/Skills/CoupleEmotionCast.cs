using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>What the couple branch reads of the field's physics (CPhysXHelper) for <see cref="CoupleEmotionCast.Placeable"/>.</summary>
public interface ICouplePlaceProbe
{
    /// <summary>CPhysXHelper::GetGroundDistance(point, &amp;d, <paramref name="len"/>): ground within the length below the point.</summary>
    bool GroundWithin(Vector3 point, float len);
    /// <summary>CPhysXHelper::CheckCapsuleOverlap: the PC capsule at the point overlaps something.</summary>
    bool CapsuleOverlaps(Vector3 point);
    /// <summary>RayCastFixedDirection straight down, unlimited: false when nothing is hit or the hit has no entity
    /// (kHit.m_hitEntity == 0); else <paramref name="blocked"/> is the entity's own refusal (its tag, its models, the world
    /// service's registered model names, statecoupleemotion.cpp:1298-1309).</summary>
    bool EntityBelow(Vector3 point, out bool blocked);
}

/// <summary>A PC in the field as the couple branch reads it: GetObjectId, vfunc_77 (the name), FUN_141656a80 (the
/// character id), vfunc_18 (the feet) and vfunc_20 (the rotation).</summary>
public sealed record CouplePeer(int ObjectId, long CharacterId, string Name, Vector3 Position, Vector3 Rotation);

/// <summary>StateCoupleEmotionData (statecoupleemotion.cpp, ctor @0x140b7fa70): the emotion, the partner's and the requester's
/// object ids, the start (the partner's feet) and the end (the partner's rotation).</summary>
public sealed record CoupleEmotionStart(uint Emotion, int PartnerId, int RequesterId, Vector3 PosStart, Vector3 PosEnd);

/// <summary>
/// StateSkill::FUN_1417eaa50's main type 0xf branch (stateskill.cpp:1914-1990): the own cast of a couple emotion on the couple
/// action dialog's selected character.
/// </summary>
public static class CoupleEmotionCast
{
    /// <summary>StateCoupleEmotion's client state id and the requester's first sub state (vfunc_59(0x3a, data, 0x93, 1)).</summary>
    public const int StateCoupleEmotion = 0x3a, RequestSub = 0x93;
    /// <summary>The notices: the partner blocked (0xcef, the name), a couple request box up (0xced), not here (0xcec), too far
    /// (0xcee, the name).</summary>
    public const int BlockedNotice = 0xcef, BoxShownNotice = 0xced, NotHereNotice = 0xcec, TooFarNotice = 0xcee;
    /// <summary>The message box extra of the couple request box (s_strCoupleEmotion).</summary>
    public const string BoxExtra = "coupleEmotion";
    /// <summary>CoupleEmotionMaxDistance's default (Unk_CeConsts m_maxDist).</summary>
    public const int DefaultMaxDistance = 300;

    /// <summary>What the branch read.</summary>
    public sealed class Input
    {
        /// <summary>The body (the ridee's when riding) is jumping: FUN_1417eaa50 returns before the switch (:1880-1885).</summary>
        public bool Jumping;
        /// <summary>FindCoupleActionDialog(0x4f4e) exists (assert break when not, :1922-1926).</summary>
        public bool HasDialog = true;
        /// <summary>The dialog's GetSelectedCharacterId (m_178).</summary>
        public long Selected;
        /// <summary>WorldService::FUN_141bd1530: the PC of a character id in the field, null when none.</summary>
        public Func<long, CouplePeer> FindPeer = _ => null;
        /// <summary>CBuddyManager::FUN_140ae51b0: the character is on the block list.</summary>
        public Func<long, bool> Blocked = _ => false;
        /// <summary>FUN_14151f820's shown box extra, null when no box is up.</summary>
        public string ShownBoxExtra;
        public bool InCubeMode, OnWater;
        /// <summary>GetCurStateId.</summary>
        public int StateId;
        public int ObjectId;
        /// <summary>vfunc_18, the own feet.</summary>
        public Vector3 Position;
        /// <summary>FUN_140b84dc0 on the own feet.</summary>
        public Func<Vector3, bool> Placeable = _ => false;
        /// <summary>CoupleEmotionMaxDistance.</summary>
        public int MaxDistance = DefaultMaxDistance;
    }

    /// <summary>What the branch did: <see cref="Handled"/> is *pResult; a notice id (with the partner's name for 0xcef /
    /// 0xcee); or the state entered and the BuddyEmote mode 0 sent.</summary>
    public readonly record struct Outcome(bool Handled, int Notice, string NoticeName, CoupleEmotionStart Start,
        BuddyEmoteInviteRequest Send);

    /// <summary>
    /// The branch in the client's order: nothing while jumping (:1880-1885), without the dialog (:1922) or when the selected
    /// character is not in the field (:1928-1933, the asserts' breaks). The partner on the block list: notice 0xcef with the
    /// name (:1934-1938). A couple request box up (FUN_140b83ec0 false): 0xced (:1939-1943). Cube mode, on water
    /// (FUN_141675ed0), a state other than 1, 2, 4, 6, 7 (FUN_140b83f60) or the own feet not placeable (FUN_140b84dc0): 0xcec
    /// (:1944-1964). The partner farther than CoupleEmotionMaxDistance (FUN_140b83fa0): 0xcee with the name (:1965-1969).
    /// Else StateCoupleEmotion 0x3a in sub 0x93 with the data and BuddyEmote mode 0 (the skill id, the partner's character
    /// id), *pResult = 1 (:1970-1988). Only that last path sets *pResult; every other one leaves it 0.
    /// </summary>
    public static Outcome Use(SkillData skill, Input i)
    {
        if (skill == null || skill.MainType != EmotionCast.MainTypeCouple) return default;
        if (i.Jumping || !i.HasDialog) return default;
        var peer = i.FindPeer(i.Selected);
        if (peer == null) return default;
        if (i.Blocked(i.Selected)) return new Outcome(false, BlockedNotice, peer.Name, null, null);
        if (!NoCoupleBox(i.ShownBoxExtra)) return new Outcome(false, BoxShownNotice, null, null, null);
        if (i.InCubeMode || i.OnWater || !MayStart(i.StateId) || !i.Placeable(i.Position))
            return new Outcome(false, NotHereNotice, null, null, null);
        if (!WithinDistance(i.Position, peer.Position, i.MaxDistance)) return new Outcome(false, TooFarNotice, peer.Name, null, null);
        var start = new CoupleEmotionStart(skill.Id, peer.ObjectId, i.ObjectId, peer.Position, peer.Rotation);
        return new Outcome(true, 0, null, start, new BuddyEmoteInviteRequest((int)skill.Id, peer.CharacterId));
    }

    /// <summary>FUN_140b83ec0 @0x140b83ec0 (statecoupleemotion.cpp:1179): no box up, or the box up is not the couple
    /// request (its extra is not "coupleEmotion").</summary>
    public static bool NoCoupleBox(string shownExtra) => shownExtra == null || shownExtra != BoxExtra;

    /// <summary>FUN_140b83f60 @0x140b83f60 (:1191): states 1, 2, 4, 6 and 7.</summary>
    public static bool MayStart(int state) => state is 1 or 2 or 4 or 6 or 7;

    /// <summary>FUN_140b83fa0 @0x140b83fa0 (:1207): |a - b| &lt;= (float)max.</summary>
    public static bool WithinDistance(Vector3 a, Vector3 b, int max) => !((a - b).Length() > (float)max);

    /// <summary>
    /// FUN_140b84dc0 @0x140b84dc0 (statecoupleemotion.cpp:1282-1310): the point raised by 5 (the exe raises the caller's
    /// point itself), ground within 10 below it, the PC capsule there overlapping nothing, and the ray down hitting an entity
    /// that does not refuse (<see cref="ICouplePlaceProbe.EntityBelow"/>).
    /// </summary>
    public static bool Placeable(ICouplePlaceProbe probe, Vector3 point)
    {
        point.Z += 5f;
        if (!probe.GroundWithin(point, 10f)) return false;
        if (probe.CapsuleOverlaps(point)) return false;
        if (!probe.EntityBelow(point, out bool blocked)) return false;
        return !blocked;
    }
}
