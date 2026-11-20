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

    /// <summary>The mode 1 sub-mode 0 send of an own attack's targets (StateSkill::FUN_1417ecce0, stateskill.cpp:1370). False offline.</summary>
    public bool SendDamage(Ms2.Net.Packets.SkillDamageRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The field's other players and npcs an own attack can hit (cleared first); empty before the field is built.</summary>
    public void HitCandidates(System.Collections.Generic.List<HitCandidate> into)
    {
        into.Clear();
        remote?.HitCandidates(into);
    }

    /// <summary>The cast id of a new own cast: GenerateUniqueId over the server time (offline the local clock).</summary>
    public static long NewCastId() =>
        Ms2.Net.Packets.UniqueId.Next(Instance?.GameSession?.ServerTime ?? System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>The server tick a new record takes (CMainSystem::GetServerTick); offline the elapsed time.</summary>
    public static int CastTick() => Instance?.GameSession?.ServerTick ?? (int)Time.GetTicksMsec();

    /// <summary>A skill motion's effect in the field, through the combat layer the other players' casts use.</summary>
    public void PlayFieldEffect(string effect, Vector3 at, float yawRadians) => remote?.PlayEffect(effect, at, yawRadians);
    /// <summary>The skill rows the cooldowns look up (SkillDataTableCli); the HUD sets it once its tables are read.</summary>
    public System.Func<uint, SkillData> SkillRows;
    SkillCooltimes cooltimes;
    /// <summary>The own player's CSkillSystem cooldowns and charge uses, fed by the cooldown packets.</summary>
    public SkillCooltimes Cooltimes => cooltimes ??= new SkillCooltimes(id => SkillRows?.Invoke(id));

    /// <summary>
    /// The own state's cancel (State::vfunc_7(1)): the cast uid to match (0 for any) and the state id the player must be in
    /// (0x10, the skill state, for OnSkillCancel and OnSkillUseFailed mode 5; mode 6's own state id).
    /// </summary>
    public event System.Action<long, int> OwnStateCancelled;

    /// <summary>The own player's skill packets; false when the packet is not one of them.</summary>
    bool HandleOwnSkill(Ms2.Net.Wire.RawPacket p)
    {
        if (game.Is(p, Ms2.Net.Inbound.SkillUseFailed))
        {
            // OnSkillUseFailed @0x1417187b0 (scenestategame.cpp:3425): only with MyPC; mode 5 cancels the own skill state of the
            // uid, mode 6 the own state of the id; then the cooldown and the uses
            var f = Ms2.Net.Packets.SkillUseFailed.Read(p.Reader());
            if (selfObject < 0) return true;
            if (f.Mode == 5) OwnStateCancelled?.Invoke(f.SkillUid, 0x10);
            else if (f.Mode == 6) OwnStateCancelled?.Invoke(0, f.State);
            Cooltimes.Apply(f);
            GD.Print($"live: skill use failed, skill {f.SkillId} error {f.Error} mode {f.Mode}");
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillCooldown))
        {
            // OnSkillCooldown @0x14172f030 (scenestategame.cpp:7856), only with MyPC
            var c = Ms2.Net.Packets.SkillCooldown.Read(p.Reader());
            if (selfObject >= 0) Cooltimes.Apply(c, (uint)game.Session.ServerTick);
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillResetCooldown))
        {
            // OnSkillResetCooldown @0x14172f1c0 (scenestategame.cpp:4231): FUN_141765de0, only with MyPC
            if (selfObject >= 0) Cooltimes.ResetAll();
        }
        else if (game.Is(p, Ms2.Net.Inbound.SkillCancel))
        {
            // OnSkillCancel @0x141718680 (scenestategame.cpp:3409): the actor's skill state of the uid is cancelled; the other
            // players' go through the field tracker, the own one here
            var c = Ms2.Net.Packets.SkillCancel.Read(p.Reader());
            if (c.ActorId == selfObject) OwnStateCancelled?.Invoke(c.CastUid, 0x10);
            return false;
        }
        else return false;
        return true;
    }
}
