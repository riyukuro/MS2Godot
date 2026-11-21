using System;
using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The own player's casts from the keyboard: the attack key and the quick slot keys.
public partial class HudScene
{
    readonly CastWorld castWorld = new();          // CSkillSystem's last cast time / skill, kept across casts
    Ms2.Formats.World.SkillTable castMotions;      // the motion effects, the table the replayed casts read
    // StateSkill's record and m_nAttack for the own cast whose sequence plays, the sequence, and the next point's text key time
    CastRecord ownCast;
    int ownAttack;
    string ownSequence;
    ulong ownStateStartMs;                         // when the own skill state began (StateSkill's m_nElapsed counts from it)
    double ownPointTime = -1;
    static readonly SerialIds serialIds = new();   // GenerateSerialId's function static
    static readonly Random crtRandom = new();
    static readonly Func<int> crtRand = () => crtRandom.Next(0x8000);
    Func<long> nextSerial;
    readonly System.Collections.Generic.List<HitCandidate> hitObjects = new();
    readonly System.Collections.Generic.List<HitCandidate> hitQueried = new();
    readonly System.Collections.Generic.List<int> hitIds = new();

    /// <summary>
    /// A key press that gives a skill: MyPC's cast request (mypc.cpp:1690-1704) asks the key's quick slot for an item first
    /// (CKeyTable::GetQuickSlotItemUidByKey), else CKeyTable::GetSkillIdByKey (keytable.cpp:1452): the attack key's normal
    /// attack, or the skill of the quick slot the key is bound to on the current page (GetKeySkillId). A non-zero skill goes to
    /// the request (<see cref="CastOwn"/>). True when the key gave a skill.
    /// STILL-OPEN: a quick slot holding an item (CanUseItem, HasFunction, the item's skill) does not cast; the held item key
    /// (0x493e2) has no held item here; the skill expansion keys are not read.
    /// </summary>
    bool CastKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping || hudModel?.Keys is not { } keys) return false;
        var slots = hudModel.ServerKeys;
        for (int dik = 1; dik < 0xd4; dik++)
        {
            if (GodotKeyOfDik(dik) != k.PhysicalKeycode) continue;
            if (keys.GetQuickSlotItemUidByKey(dik, slots) != 0) return false;
            castKeyWorld = QuickSlotWorld();
            castKeyHeld = castKeyWorld.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
            // STILL-OPEN: the right-hand item's own skill (an action whose normalAttack is 0) is not read; 0 here
            castAttackSkill ??= () => (int)SkillCast.AttackKeySkill(castKeyWorld, castWorld, castKeyHeld, weaponActions, 0);
            int id = keys.GetSkillIdByKey(dik, castKeyWorld.HasPc, castAttackSkill, null, slots);
            if (id == 0) continue;
            CastOwn(castKeyWorld, (uint)id, castKeyHeld);
            return true;
        }
        return false;
    }
    SlotWorld castKeyWorld;
    WeaponAction castKeyHeld;
    Func<int> castAttackSkill;

    /// <summary>
    /// CSkillSystem::FUN_141764e30 for the own player (<see cref="SkillCast.Request"/>) at MyPC::GetSkillLevel, then
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1146-1234): the cooldown gate (<see cref="SkillCast.CooldownGate"/>), the mode 0
    /// send, FUN_141766cc0's reuse record, the record's motion on the own body, the cast's cooldown (FUN_141525ff0) and the
    /// charge use (OnRechargeSkillUsed), then the motion effect.
    /// STILL-OPEN, not fired here: FUN_141525580's refusals (states 0x2b / 0x2f, FUN_141527960, mypc.cpp:2436-2443) and its
    /// IsUnrideOnUse ride-off (:2447-2470); the item kind and quick slot item checks with the binding confirm (:1167-1193);
    /// NotifyUseSKill and the DPS meter (:1206, :1210); a level with a sequence while riding, and a level without one
    /// (the ridee FUN_141706650 / FUN_141524a80 and StateSkill::FireFirstAttack, :1219-1226); FUN_141767990 (:1228).
    /// </summary>
    void CastOwn(SlotWorld w, uint skillId, WeaponAction held)
    {
        if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player me) return;
        var level = w.Jobs?.SkillLevel(skillId) ?? 1;
        var d = w.Tables.GetSkillData(skillId);
        var lv = d?.GetLevelData(level);
        var g = me.GlobalPosition;
        float rotZ = me.FacingRotZ, rad = Mathf.DegToRad(rotZ);
        var req = new CastRequest(skillId, level, Net.LiveSession.NewCastId(), Net.LiveSession.CastTick())
        {
            HasSequence = lv?.HasSequenceName ?? true,
            Position = new System.Numerics.Vector3(g.X, -g.Z, g.Y),
            Direction = new System.Numerics.Vector3(MathF.Sin(rad), -MathF.Cos(rad), 0),
            RotZ = rotZ,
        };
        var sp = me.Velocity;
        castWorld.Jumping = me.State == Player.MoveState.Jump;
        castWorld.Rising = sp.Y > 0;
        castWorld.Falling = me.State == Player.MoveState.Fall;
        castWorld.GameTime = Time.GetTicksMsec() / 1000.0;
        var res = SkillCast.Request(w, castWorld, req, _ => SkillCast.NormalAttackSkillId(held, weaponActions, 0));
        if (res.Record is not { } r)
        {
            GD.Print($"cast: skill {skillId} refused, reason {res.Why}{(res.Notice != null ? " " + res.Notice : "")}");
            return;
        }
        var live = Net.LiveSession.Instance;
        uint now = (uint)Net.LiveSession.CastTick();
        // MyPC::vfunc_102 (mypc.cpp:1152-1166): a skill still cooling is queued in the skill state or dropped, never sent.
        // STILL-OPEN: StateSkill::SetChargeRecord (the queued press cast when the running one ends) and the running record's
        // m_n90 = 0 are not ported, so a queued press is dropped as well.
        if (live != null)
        {
            var gate = SkillCast.CooldownGate(live.Cooltimes, r, OwnSkillStateRuns(), now);
            if (gate != CoolGate.Cast)
            {
                GD.Print($"cast: skill {skillId} cooling, {(gate == CoolGate.Queued ? "queued" : "refused")}");
                return;
            }
        }
        // :1197-1203: the running skill state's elapsed time (StateSkill m_nElapsed, FUN_14028d280: ms to seconds)
        float elapsed = OwnSkillStateRuns() ? (Time.GetTicksMsec() - ownStateStartMs) * 0.001f : 0f;
        // StateSkill::FUN_1417ea570: the skill state and the mode 0 send
        bool sent = live?.SendCast(r, hudModel.IsFeatureEnabled(174)) ?? false;
        SkillCast.Casted(castWorld, r);
        float delay = 0f;
        Ms2.Formats.Model.KfAnimation seq = null;
        var motion = r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion] : null;
        if (r.Level.HasSequenceName)
        {
            // :1213-1216: a level with a sequence enters the skill state; the cooldown counts the old state's time off
            if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.PlaySkillSequence(motion.SequenceName, motion.SequenceSpeed))
            {
                StartOwnAttacks(me, r);
                me.Anims.Kf.TryGetValue(me.SkillSequence, out seq);
            }
            delay = elapsed;
        }
        // :1230: after the send, the cast's own cooldown (FUN_141525ff0) from the sequence's text keys
        if (sent) live.Cooltimes.StartCast(r, seq, delay, now);
        // :1232: PCBase::FUN_141675ed0 is 0 without a ridee (STILL-OPEN: rides), so the charge use counts
        live?.Cooltimes.OnRechargeSkillUsed(r.Skill, now);
        castMotions ??= new Ms2.Formats.World.SkillTable(assets.Packs);
        if (castMotions.Motion((int)r.Skill.Id, r.Level.Level, r.Motion)?.MotionEffect is { } fx) live?.PlayFieldEffect(fx, g, me.Visual?.Rotation.Y ?? 0);
        GD.Print($"cast: skill {r.Skill.Id} level {r.Level.Level} motion {r.Motion}{(sent ? " sent" : " (offline, not sent)")}");
    }

    /// <summary>StateSkill's attack count at the start of the own cast's motion: attack 0, its point's text key.</summary>
    void StartOwnAttacks(Player me, CastRecord r)
    {
        ownCast = r;
        ownAttack = 0;
        ownSequence = me.SkillSequence;
        ownStateStartMs = Time.GetTicksMsec();
        ownPointTime = OwnPointTime(me);
    }

    /// <summary>The own skill state (0x10) runs: the own cast's sequence still plays.</summary>
    bool OwnSkillStateRuns() => ownCast != null && player != null && (object)player.SkillSequence == ownSequence;

    double OwnPointTime(Player me)
    {
        var a = SkillAttackFire.AttackAt(ownCast, ownAttack);
        if (a == null || string.IsNullOrEmpty(a.Point) || me.Anims == null) return -1;
        return me.Anims.TextKey(ownSequence, a.Point);
    }

    /// <summary>
    /// Each frame: StateSkill::TextKeyEvent (stateskill.cpp:980) when the own sequence passes the current attack's point text
    /// key, then FireAttacksAtPoint (<see cref="FireOwnPoint"/>). 0 B/frame until a point fires.
    /// STILL-OPEN: the sequence's other text keys (move3, charging_attack, combo, inputcombo) are not read.
    /// </summary>
    void TickOwnAttacks()
    {
        if (ownCast == null) return;
        if (player == null || (object)player.SkillSequence != ownSequence) { ownCast = null; return; }
        if (ownPointTime < 0 || player.SkillSequencePosition < ownPointTime) return;
        FireOwnPoint(player);
    }

    /// <summary>
    /// StateSkill::FireAttacksAtPoint @0x1417f0f10: every attack in a row at the point fires (FUN_1417ecce0 through
    /// <see cref="SkillAttackFire.Fire"/>, the send through LiveSession.SendDamage), then the next attack's point.
    /// STILL-OPEN: CSkillRecord::FUN_141759ad0's z offset (the motion's m_fAC / m_fB8 and the record's cooldown and aim are not
    /// read) is 0.
    /// </summary>
    void FireOwnPoint(Player me)
    {
        var live = Net.LiveSession.Instance;
        int n = SkillAttackFire.AttacksAtPoint(ownCast, ownAttack);
        if (n > 0)
        {
            live?.HitCandidates(hitObjects);
            if (live == null) hitObjects.Clear();
            var g = me.GlobalPosition;
            var pos = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
            float rotZ = me.FacingRotZ;
            hitObjects.Add(new HitCandidate { Id = live?.SelfObjectId ?? 0, Position = pos, Body = SkillHit.PlayerBody(pos, rotZ), IsOwner = true,
                Kind = HitKind.Pc, Team = 1, Hp = 1 });
            nextSerial ??= () => serialIds.Next(Net.LiveSession.Instance?.SelfObjectId ?? 0);
            for (int i = 0; i < n; i++)
            {
                var req = SkillAttackFire.Fire(ownCast, ownAttack, pos, rotZ, 0,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects), crtRand, nextSerial, hitQueried, hitIds);
                bool sent = req != null && (live?.SendDamage(req) ?? false);
                GD.Print($"cast: skill {ownCast.Skill.Id} attack {ownAttack} targets {req?.Targets.Count ?? 0}{(sent ? " sent" : " (offline, not sent)")}");
                ownAttack++;
            }
        }
        ownPointTime = OwnPointTime(me);
    }
    Action unwatchOwnSkill;
    Func<uint, int, float> slotCooldownRemain;
    Func<uint, (float, bool)> slotCooldownTotal;
    Func<SkillData, int> slotRechargeRemain;

    /// <summary>
    /// The live session's own skill packets: the cooldowns look up the HUD's skill rows, and the own state's cancel (State::vfunc_7(1)
    /// from OnSkillCancel / OnSkillUseFailed) ends the own cast's sequence when the player is in the skill state (0x10) of the uid.
    /// STILL-OPEN: the player has no other state ids, so mode 6's cancel of a state other than 0x10 does nothing.
    /// </summary>
    void WatchOwnSkill()
    {
        unwatchOwnSkill?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        live.SkillRows = id => hudModel?.Tables.Skills.GetSkillData(id);
        Action<long, int> on = (uid, state) =>
        {
            if (state != 0x10 || ownCast == null || player == null || (object)player.SkillSequence != ownSequence) return;
            if (uid != 0 && uid != ownCast.CastId) return;
            GD.Print($"cast: skill {ownCast.Skill.Id} cancelled by the server");
            player.CancelSkillSequence();
            ownCast = null;
        };
        Action<Ms2.Net.Packets.SkillSync> sync = OwnSkillSync;
        live.OwnStateCancelled += on;
        live.OwnSkillSync += sync;
        unwatchOwnSkill = () => { live.OwnStateCancelled -= on; live.OwnSkillSync -= sync; };
    }

    /// <summary>
    /// OnSkillSync @0x141718150 (scenestategame.cpp:3378) for the own player: another cast than the own running one (no record,
    /// another uid or motion, or the sequence no longer playing) starts the packet's: its motion sequence at the packet's speed
    /// and its motion effect.
    /// STILL-OPEN: that cast has no CSkillRecord here (CSkillRecord::Init of the request), so its attacks do not fire; the running
    /// cast's update (m_kRot, vfunc_22(move), StateSkill::FUN_1417ec630's charge flags) is not ported.
    /// </summary>
    void OwnSkillSync(Ms2.Net.Packets.SkillSync s)
    {
        if (player == null) return;
        bool running = ownCast != null && (object)player.SkillSequence == ownSequence;
        if (running && ownCast.CastId == s.CastUid && ownCast.Motion == s.Motion) return;
        ownCast = null;
        castMotions ??= new Ms2.Formats.World.SkillTable(assets.Packs);
        var motion = castMotions.Motion(s.SkillId, s.Level, s.Motion);
        if (motion == null) return;
        if (!string.IsNullOrEmpty(motion.SequenceName)) player.PlaySkillSequence(motion.SequenceName, MathF.Max(s.Speed, 0.1f));
        var g = player.GlobalPosition;
        if (!string.IsNullOrEmpty(motion.MotionEffect)) Net.LiveSession.Instance?.PlayFieldEffect(motion.MotionEffect, g, player.Visual?.Rotation.Y ?? 0);
        GD.Print($"cast: skill {s.SkillId} motion {s.Motion} started by the server");
    }

    /// <summary>Each frame: CSkillSystem::Update's cooldown part at the server tick (0 B/frame).</summary>
    static void TickCooltimes()
    {
        if (Net.LiveSession.Instance is { } live) live.Cooltimes.Update((uint)Net.LiveSession.CastTick());
    }

    /// <summary>The quick slots' cooldown reads: the live session's CSkillSystem table at the server tick, idle offline.</summary>
    void SlotCooldowns(SlotWorld w)
    {
        if (Net.LiveSession.Instance is not { } live) return;
        slotCooldownRemain ??= (id, mode) => Net.LiveSession.Instance?.Cooltimes.Remain(id, mode != 0, (uint)Net.LiveSession.CastTick()) ?? 0f;
        slotCooldownTotal ??= id =>
        {
            float total = 0; bool byGroup = false;
            Net.LiveSession.Instance?.Cooltimes.Remain(id, true, (uint)Net.LiveSession.CastTick(), out _, out total, out byGroup);
            return (total, byGroup);
        };
        slotRechargeRemain ??= s => Net.LiveSession.Instance?.Cooltimes.RechargeRemain(s) ?? 0;
        w.CooldownRemain = slotCooldownRemain;
        w.CooldownTotal = slotCooldownTotal;
        w.RechargeRemain = slotRechargeRemain;
    }
}
