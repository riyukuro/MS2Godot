using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The own player's skill sends (SendOp Skill 0x20).
public partial class LiveSession
{
    /// <summary>
    /// The mode 0 send of an own cast (StateSkill::FUN_1417ebc00, stateskill.cpp:2079): the server tick taken at the send and the
    /// client's elapsed time (CMainSystem::GetElapsedTime); feature 174 (HoldAttack) adds the attached target. False offline.
    /// </summary>
    public bool SendCast(CastRecord r, bool holdAttackFeature)
    {
        if (game == null) return false;
        Send(SkillCast.Send(r, game.Session.ServerTick, game.Session.ElapsedMs, holdAttackFeature));
        return true;
    }

    /// <summary>The cast id of a new own cast: GenerateUniqueId over the server time (offline the local clock).</summary>
    public static long NewCastId() =>
        Ms2.Net.Packets.UniqueId.Next(Instance?.GameSession?.ServerTime ?? System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>The server tick a new record takes (CMainSystem::GetServerTick); offline the elapsed time.</summary>
    public static int CastTick() => Instance?.GameSession?.ServerTick ?? (int)Time.GetTicksMsec();

    /// <summary>A skill motion's effect in the field, through the combat layer the other players' casts use.</summary>
    public void PlayFieldEffect(string effect, Vector3 at, float yawRadians) => remote?.PlayEffect(effect, at, yawRadians);
}
