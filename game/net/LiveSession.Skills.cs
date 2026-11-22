using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The own player's skill sends (SendOp Skill 0x20).
public partial class LiveSession
{
    /// <summary>
    /// The send of an own cast (StateSkill::FUN_1417ea570, stateskill.cpp:1807, <see cref="SkillCast.Use"/>): a state skill's
    /// SendOp StateSkill 0x21, else the mode 0 send (FUN_1417ebc00, :2079) with the server tick taken at the send; both with the
    /// client's elapsed time (CMainSystem::GetElapsedTime); feature 174 (HoldAttack) adds the attached target. False offline.
    /// </summary>
    public bool SendCast(CastRecord r, bool holdAttackFeature)
    {
        if (game == null) return false;
        // UseStateSkill kind 0x26 (stateskill.cpp:2009-2021): MyPC's class takes UseRideeItem for the record's item
        if (!r.Skill.IsEmotion && r.Skill.ActionKind == 0x26 && r.ItemUid != 0) { UseRideeItem(r.ItemUid); return true; }
        if (SkillCast.Use(r, game.Session.ServerTick, game.Session.ElapsedMs, holdAttackFeature) is { } pk) Send(pk);
        return true;
    }

    /// <summary>SendOp RequestAdditionalEffect 0x5C of the GM additional-effect dialog (uiadditionalcontroldialog.cpp:139-160).
    /// STILL-OPEN: the dialog (UIAdditionalControlDialog) and its AdditionalLevelData check have no port; nothing calls this yet.
    /// False offline.</summary>
    public bool SendAdditionalEffect(Ms2.Net.Packets.AdditionalEffectRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The mode 1 sub-mode 0 send of an own attack's targets (StateSkill::FUN_1417ecce0, stateskill.cpp:1370). False offline.</summary>
    public bool SendDamage(Ms2.Net.Packets.SkillDamageRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The mode 1 sub-mode 1 send of an own magic's region hits (CMagic::FUN_141464d00, magicdata.cpp:1830). False offline.</summary>
    public bool SendHit(Ms2.Net.Packets.SkillHitRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The mode 1 sub-mode 2 send of an own magic's splash (CMagic::FUN_14145f570, magicdata.cpp:1156). False offline.
    /// STILL-OPEN: no caller until the magic's flight is ported (its end and piercing intervals send it).</summary>
    public bool SendSplash(Ms2.Net.Packets.SkillSplashRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>The mode 2 sync of the own skill state (StateSkill::SendChargeState, stateskill.cpp:1271). False offline.</summary>
    public bool SendSync(Ms2.Net.Packets.SkillSyncRequest req)
    {
        if (game == null) return false;
        Send(req);
        return true;
    }

    /// <summary>A continue round's mode 3 (CSkillRecord::SendSkill, skillrecord.cpp:441) at the server tick. False offline.</summary>
    public bool SendRound(CastRecord r)
    {
        if (game == null) return false;
        Send(SkillCast.SendSkill(r, game.Session.ServerTick));
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

    /// <summary>A skill motion's effect in the field, through the combat layer the other players' casts use;
    /// <paramref name="casterKind"/> is the effect's object relation (FUN_141bd19d0: 1 the own PC).</summary>
    public void PlayFieldEffect(string effect, Vector3 at, float yawRadians, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => remote?.PlayEffect(effect, at, yawRadians, casterKind);

    /// <summary>The same turned by a full rotation (godot space), as an own magic's end effect.</summary>
    public void PlayFieldEffect(string effect, Vector3 at, Basis rotation, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => remote?.PlayEffect(effect, at, rotation, casterKind);

    /// <summary>
    /// A CommonResource effect on the own PC (CEffectService::InsertEffect with the actor set and kind 1, the own PC):
    /// <paramref name="key"/> is the commonResource.xml key. Played once at the body's place and facing.
    /// STILL-OPEN: the effect is not attached to the actor (it stays where it started) and a kept one
    /// (StateDead's m_nEffect) is not removed early by its owner.
    /// </summary>
    void PlayOwnCommonEffect(string key)
    {
        if (player == null || !IsInstanceValid(player)) return;
        string path;
        try { path = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).CommonResource(key); }
        catch (Exception ex) { GD.PushWarning($"common effect {key}: {ex.Message}"); return; }
        if (string.IsNullOrEmpty(path)) return;
        OwnEffectsPlayed?.Invoke(path);
        remote?.PlayEffect(path, player.GlobalPosition, player.Visual?.Rotation.Y ?? 0, Ms2.Audio.SoundMusicEffectState.CasterMyPc);
    }

    /// <summary>The effect xml of each own CommonResource effect played (for the checks).</summary>
    public Action<string> OwnEffectsPlayed;

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

    /// <summary>OnSkillSync @0x141718150 (scenestategame.cpp:3361) for the own player: the HUD tests it against the own cast.</summary>
    public event System.Action<Ms2.Net.Packets.SkillSync> OwnSkillSync;

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
        else if (game.Is(p, Ms2.Net.Inbound.SkillSync))
        {
            // the other actors' syncs go through the field tracker, the own one to the HUD's cast
            var s = Ms2.Net.Packets.SkillSync.Read(p.Reader());
            if (selfObject >= 0 && s.ActorId == selfObject) OwnSkillSync?.Invoke(s);
            return false;
        }
        else if (game.Is(p, Ms2.Net.Inbound.StateSkill))
        {
            // OnStateSkill @0x141718b80 (:3476): type 6 calls the PC's current state's vfunc_33(true), which only StateHit
            // overrides (statehit.cpp:350, its m_bC8). STILL-OPEN: no player here has a hit state, so it is logged only.
            var st = Ms2.Net.Packets.StateSkill.Read(p.Reader());
            if (LogPackets && st.Type == 6 && selfObject >= 0 && st.ActorId == selfObject) GD.Print($"live: state skill 6, skill {st.SkillId} level {st.Level}");
            return false;
        }
        else return false;
        return true;
    }
}
