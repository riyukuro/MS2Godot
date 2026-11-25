using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

public sealed partial class RemoteLayer
{
    /// <summary>
    /// The other player whose PC body is <paramref name="colliderId"/> (a mount's body is not a PC: FUN_141a8ab90's
    /// DynamicCast&lt;PC&gt; fails on a Ridee), as PopUpMenu (uipartydialog.cpp:1973) reads it: the object id (the
    /// entity key), the position (vfunc_18, MS2 axes), the level, character id, profile image (FUN_141028be0 of the
    /// picture), name, alive (IsDead == 0), job group and job code. STILL-OPEN: the trophy sum of the PC's proxy tally
    /// (FindProxyObject(...)->GetTally()->Sum()) is not kept for other players here, so it is 0. Null when no such PC.
    /// </summary>
    public (int ObjectId, PopupMenuTarget Target)? PopupTargetOf(ulong colliderId, Func<long, string, string> profileImage)
    {
        foreach (var r in users.Values)
        {
            if (r.Pc == null || r.Pc.Body.GetInstanceId() != colliderId) continue;
            return (r.ObjectId, new PopupMenuTarget
            {
                World = r.Pc.Position,
                Level = r.Level, CharacterId = r.CharacterId, Image = profileImage(r.CharacterId, r.Picture ?? ""),
                Name = r.Name ?? "", Alive = !r.Pc.States.Dead, Online = true, JobGroup = r.Pc.JobGroup, JobCode = r.Job,
            });
        }
        return null;
    }

    /// <summary>WorldService::FUN_141bd1530 for StateSkill's couple branch (stateskill.cpp:1928-1985): the other player of
    /// a character id with what the branch reads of it (GetObjectId, vfunc_77 the name, FUN_141656a80 the character id,
    /// vfunc_18 the feet in MS2 axes, vfunc_20 the rotation in degrees). Null when no such PC.</summary>
    public Ms2.Ui.Skills.CouplePeer CouplePeerOf(long characterId)
    {
        foreach (var r in users.Values)
        {
            if (r.Pc == null || r.CharacterId != characterId) continue;
            return new Ms2.Ui.Skills.CouplePeer(r.ObjectId, r.CharacterId, r.Name ?? "", r.Pc.Position,
                new System.Numerics.Vector3(0f, 0f, r.Pc.RotationZ));
        }
        return null;
    }

    /// <summary>WorldService::FindPCById for the couple state: the other player of an object id as
    /// <see cref="CouplePeerOf"/> gives it; null when none.</summary>
    public Ms2.Ui.Skills.CouplePeer CouplePeerByObject(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Pc != null
            ? new Ms2.Ui.Skills.CouplePeer(r.ObjectId, r.CharacterId, r.Name ?? "", r.Pc.Position, new System.Numerics.Vector3(0f, 0f, r.Pc.RotationZ))
            : null;

    /// <summary>What the own couple state reads of its partner (StateCoupleEmotion's GetPartner: the feet, GetCurStateId,
    /// FUN_14166cc80 the sub, the partner state's GetAniIndex); null when the PC is not in the field.</summary>
    public CouplePartnerView? CouplePartnerOf(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Pc != null
            ? new CouplePartnerView(r.Pc.Position, r.Pc.States.Cur, r.Pc.States.Sub, r.Pc.States.CoupleAni)
            : null;

    /// <summary>A scripted live run's line on another player: its state, emotion, hp, stance, head bar and the name tag's and
    /// bar's emotion offsets and screen heights (the run's remote offset check).</summary>
    public string WatchLine(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Pc != null
            ? $"state {r.Pc.States.Cur:x} emotion {r.Pc.States.EmotionId} hp {r.HpCur}/{r.HpMax} battle {r.InBattle} " +
              $"bar {(r.Bar is { } b ? $"active {b.Active} shown {b.Shown}" : "none")} hp bar extra {r.Pc.HpBarExtra}; " +
              $"tag {tags.WatchLine(r.Root)}; bar {bars.WatchLine(r.Root)}"
            : "-";

    /// <summary>The other player's gender (CoupleEmotionAnimInfo::Init's partner gender), -1 when none.</summary>
    public int GenderOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Gender : -1;

    /// <summary>The couple state's partner of any PC: the own PC's view when the id is the own object id.</summary>
    public CouplePartnerView? AnyCouplePartner(int objectId)
    {
        if (objectId != 0 && objectId == Tracker.SelfObjectId)
            return LiveSession.Instance?.OwnPlayer is { } me && IsInstanceValid(me) ? me.CoupleView : null;
        return CouplePartnerOf(objectId);
    }

    /// <summary>PC::GetGender of any PC by object id, -1 when none.</summary>
    public int AnyGender(int objectId)
    {
        if (objectId != 0 && objectId == Tracker.SelfObjectId)
            return LiveSession.Instance?.OwnPlayer is { } me && IsInstanceValid(me) ? me.Gender : -1;
        return GenderOf(objectId);
    }

    /// <summary>The path nif loader the couple states use (set by the HUD, which holds the nif spec).</summary>
    public static Func<string, string, string, Ms2.Ui.Skills.CouplePath> CouplePathLoader;
}
