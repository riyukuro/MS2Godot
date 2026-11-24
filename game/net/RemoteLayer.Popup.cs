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
}
