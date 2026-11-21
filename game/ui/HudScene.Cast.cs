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
    readonly ChargeSync ownSync = new();          // StateSkill's last sent mode 2 sync
    CastRecord ownQueued;                          // StateSkill's m_spCharge: the cooling press queued behind the running skill
    ulong ownStateStartMs;                         // when the own skill state began (StateSkill's m_nElapsed counts from it)
    double ownPointTime = -1;
    readonly SkillLoop ownLoop = new();            // StateSkill's continue loop fields
    // the sequence's "loopstart" / "loopend" times (FUN_141b6fe90 at speed 1), -1 without; the sequence time the text keys
    // were read up to
    float ownLoopStartKey = -1, ownLoopEndKey = -1;
    // a charging skill's "charging_attack" / "charging_combo" times (-1 without) and m_nChargingComboTick (stateskill.cpp:769)
    float ownChargingAttackKey = -1, ownChargingComboKey = -1;
    uint ownChargingComboTick;
    double ownKeyPos = -1;
    SlotWorld ownHoldWorld;                        // the hold check's cast test world, taken once per state
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
    /// STILL-OPEN: the held item key (0x493e2) has no held item here; the skill expansion keys are not read; the item's own
    /// request fields (the item uid on the skill request) are not carried.
    /// </summary>
    bool CastKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping || hudModel?.Keys == null) return false;
        for (int dik = 1; dik < 0xd4; dik++)
        {
            if (GodotKeyOfDik(dik) != k.PhysicalKeycode) continue;
            if (CastKey(dik) is { } r) return r;
        }
        return false;
    }

    /// <summary>One key of <see cref="CastKeys"/> (MyPC::vfunc_104, mypc.cpp:1687-1701): the item the key's shown quick slot finds
    /// (<see cref="QuickSlotEdit.KeyItemSkill"/>) refuses the press (false) or gives its skill; with none the key's skill. Null when
    /// the key gives no skill, true when it went to <see cref="CastOwn"/>.</summary>
    bool? CastKey(int dik)
    {
        if (hudModel?.Keys is not { } keys) return null;
        var slots = hudModel.ServerKeys;
        castKeyWorld = QuickSlotWorld();
        castSlotItemUid ??= i => playerInfo?.SlotItemUid(i) ?? 0;
        // mypc.cpp:1689-1700: the key's quick slot item; a found item refuses or gives its skill
        int id = QuickSlotEdit.KeyItemSkill(castKeyWorld, keys.GetQuickSlotItemUidByKey(dik, playerInfo != null ? castSlotItemUid : null), out bool refused);
        if (refused) return false;
        castKeyHeld = castKeyWorld.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        // STILL-OPEN: the right-hand item's own skill (an action whose normalAttack is 0) is not read; 0 here
        castAttackSkill ??= () => (int)SkillCast.AttackKeySkill(castKeyWorld, castWorld, castKeyHeld, weaponActions, 0);
        if (id == 0) id = keys.GetSkillIdByKey(dik, castKeyWorld.HasPc, castAttackSkill, null, slots);
        if (id == 0) return null;
        CastOwn(castKeyWorld, (uint)id, castKeyHeld, dik);
        return true;
    }

    /// <summary>
    /// A clicked quick slot (SkillSlot::vfunc_13): its key, which WorldService::FUN_141bc9fd0 (worldservice_1.cpp:724) queues as
    /// pressed, goes the key's way (<see cref="CastKey"/>); a slot without a key gives FUN_14092f4e0's skill
    /// (<see cref="QuickSlotEdit.UseSkill"/>) to the cast request. STILL-OPEN: the queue itself (the press is read on the next
    /// world tick there, at once here).
    /// </summary>
    void ClickedSlotKey(int dik) => CastKey(dik);

    void ClickedSlotSkill(uint skillId)
    {
        var w = QuickSlotWorld();
        if (!QuickSlotEdit.UseSkill(w, skillId)) return;
        var held = w.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        CastOwn(w, skillId, held, 0);
    }
    SlotWorld castKeyWorld;
    WeaponAction castKeyHeld;
    Func<int> castAttackSkill;
    Func<int, long> castSlotItemUid;

    /// <summary>
    /// CSkillSystem::FUN_141764e30 for the own player (<see cref="SkillCast.Request"/>) at MyPC::GetSkillLevel, then
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1146-1234): the cooldown gate (<see cref="SkillCast.CooldownGate"/>), the mode 0
    /// send, FUN_141766cc0's reuse record, the record's motion on the own body, the cast's cooldown (FUN_141525ff0) and the
    /// charge use (OnRechargeSkillUsed), then the motion effect.
    /// STILL-OPEN, not fired here: FUN_141525580's refusals (states 0x2b / 0x2f, FUN_141527960, mypc.cpp:2436-2443) and its
    /// IsUnrideOnUse ride-off (:2447-2470); the item kind and quick slot item checks with the binding confirm (:1167-1193);
    /// NotifyUseSKill and the DPS meter (:1206, :1210); a level with a sequence while riding, and a level without one
    /// (the ridee FUN_141706650 / FUN_141524a80 and StateSkill::FireFirstAttack, :1219-1226); FUN_141767990 (:1228).
    /// <paramref name="key"/>: the DIK key of the press (kReq.m_n34, mypc.cpp:1690-1729), 0 for a clicked slot without a key;
    /// the record keeps it (m_nA4, skillrecord.cpp:192) for the charging skill's release (<see cref="TickOwnSync"/>).
    /// </summary>
    void CastOwn(SlotWorld w, uint skillId, WeaponAction held, int key)
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
            Key = key,
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
        UseOwnRecord(me, r);
    }

    /// <summary>
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1146) from the cooldown gate on, for a new request or the press queued behind the
    /// running skill (<see cref="TickOwnQueued"/>).
    /// </summary>
    void UseOwnRecord(Player me, CastRecord r)
    {
        var g = me.GlobalPosition;
        var live = Net.LiveSession.Instance;
        uint now = (uint)Net.LiveSession.CastTick();
        // MyPC::vfunc_102 (mypc.cpp:1152-1166): a skill still cooling is never sent; in the skill state it becomes the state's
        // queued press (StateSkill::SetChargeRecord @0x1417ed740, stateskill.cpp:1491, a later press replacing it), else dropped.
        // STILL-OPEN: the running record's +0x90 (= 0 here, mypc.cpp:1165; CSkillRecord::FUN_141759fc0 sets it to 1) has no
        // reader in this port.
        if (live != null)
        {
            var gate = SkillCast.CooldownGate(live.Cooltimes, r, OwnSkillStateRuns(), now);
            if (gate == CoolGate.Queued) { ownQueued = r; r.QueuedCharge = true; }
            if (gate != CoolGate.Cast)
            {
                GD.Print($"cast: skill {r.Skill.Id} cooling, {(gate == CoolGate.Queued ? "queued" : "refused")}");
                return;
            }
        }
        // :1197-1203: the running skill state's elapsed time (StateSkill m_nElapsed, FUN_14028d280: ms to seconds)
        float elapsed = OwnSkillStateRuns() ? (Time.GetTicksMsec() - ownStateStartMs) * 0.001f : 0f;
        // StateSkill::FUN_1417ea570: the skill state and the mode 0 send
        bool sent = live?.SendCast(r, hudModel.IsFeatureEnabled(174)) ?? false;
        SkillCast.Casted(castWorld, r);
        SampleAbilityRate();   // FUN_141766cc0 (skillslot.cpp:2350): DAT_143aea120, before the cooldown starts (mypc.cpp:2627)
        float delay = 0f;
        Ms2.Formats.Model.KfAnimation seq = null;
        var motion = r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion] : null;
        // FUN_141525ff0 (mypc.cpp:2557-2563) reads the motion data's sequence from the actor manager (FindSequenceID of
        // m_strSeq), whether or not the skill state plays it
        if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.Anims?.Sequence(motion.SequenceName, false) is { } seqKey)
            me.Anims.Kf.TryGetValue(seqKey, out seq);
        if (r.Level.HasSequenceName)
        {
            // :1213-1216: a level with a sequence enters the skill state; the cooldown counts the old state's time off
            if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.PlaySkillSequence(motion.SequenceName, motion.SequenceSpeed))
                StartOwnAttacks(me, r);
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

    /// <summary>
    /// Each frame while the own skill state runs: StateSkill::FUN_1417f11d0 @0x1417f11d0 (stateskill.cpp:1578, from vfunc_3
    /// :623): the queued press, once <see cref="SkillCast.QueuedReady"/>, takes the player's position (:1611), its m_bAD from
    /// its level's sequence (:1612) and goes through MyPC::vfunc_102 (<see cref="UseOwnRecord"/>); the state's enter and exit
    /// (the enter, vfunc_4 :416; the ctor :340) and the state's end here drop it. A record a continue round queued (m_n98,
    /// :1589-1607) plays the loop again instead: the round's sends (<see cref="SendOwnRound"/>) and the seek
    /// (<see cref="SeekOwnLoop"/>). 0 B/frame until it goes.
    /// STILL-OPEN: FUN_1417f1a90 (the loop's move0 / move2) is not ported.
    /// </summary>
    void TickOwnQueued()
    {
        if (ownQueued == null) return;
        if (!OwnSkillStateRuns()) { ownQueued = null; return; }
        var live = Net.LiveSession.Instance;
        if (live == null || !SkillCast.QueuedReady(live.Cooltimes, ownQueued, (uint)Net.LiveSession.CastTick())) return;
        var r = ownQueued;
        ownQueued = null;
        r.QueuedCharge = false;
        if (r.Charged)
        {
            SendOwnRound(live, r, (uint)Net.LiveSession.CastTick());
            SeekOwnLoop(player, (uint)Time.GetTicksMsec());
            return;
        }
        var g = player.GlobalPosition;
        r.Position = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        r.HasSequence = r.Level.HasSequenceName;
        UseOwnRecord(player, r);
    }

    /// <summary>StateSkill's attack count at the start of the own cast's motion: attack 0, its point's text key.</summary>
    void StartOwnAttacks(Player me, CastRecord r)
    {
        ownCast = r;
        ownQueued = null;                          // StateSkill::vfunc_4 (stateskill.cpp:416)
        ownSync.Reset(new System.Numerics.Vector3(0, 0, me.FacingRotZ));   // vfunc_4 (:484-487): the owner's rotation
        ownAttack = 0;
        ownSequence = me.SkillSequence;
        ownStateStartMs = Time.GetTicksMsec();
        ownPointTime = OwnPointTime(me);
        // vfunc_4 (stateskill.cpp:395-420) and the sequence start (FUN_1417f0940 :2226-2238): the loop fields, the repeats
        ownLoop.Reset();
        ownLoop.StartSequence(r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion].LoopCount : 0);
        ownLoopStartKey = me.Anims?.TextKey(ownSequence, "loopstart") ?? -1;
        ownLoopEndKey = me.Anims?.TextKey(ownSequence, "loopend") ?? -1;
        ownChargingAttackKey = me.Anims?.TextKey(ownSequence, "charging_attack") ?? -1;
        ownChargingComboKey = me.Anims?.TextKey(ownSequence, "charging_combo") ?? -1;
        // :769: m_nStartTick (the enter's tick, :421) plus the key's time at the state's speed in ms (FUN_14028d260)
        ownChargingComboTick = (uint)ownStateStartMs + (uint)(long)(ownChargingComboKey / SkillCast.MotionSpeedScale(r) * 1000.0f);
        ownKeyPos = -1;
        ownHoldWorld = null;
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
    /// Each frame: StateSkill::TextKeyEvent (stateskill.cpp:844-980) for the text keys the own sequence passed since the last
    /// frame (a key at a time after the last read one and not after now): "loopstart" (<see cref="SkillLoop.OnLoopStart"/>),
    /// "charging_combo" (<see cref="SkillLoop.OnChargingCombo"/>), the current attack's point (the charging skill's m_bLoop,
    /// <see cref="SkillLoop.OnAttackPoint"/>, then FireAttacksAtPoint, <see cref="FireOwnPoint"/>), "charging_attack"
    /// (<see cref="SkillLoop.OnChargingAttack"/>), then "loopend" (<see cref="OwnLoopEnd"/>), in time order
    /// (<see cref="SkillLoop.NextTextKey"/>; keys at one time in the order above); a loopend that seeks to the next round ends
    /// the frame's keys. 0 B/frame until a point fires.
    /// STILL-OPEN: the sequence's other text keys (move0-3, combo, inputcombo) are not read.
    /// </summary>
    void TickOwnAttacks()
    {
        if (ownCast == null) return;
        if (player == null || (object)player.SkillSequence != ownSequence) { ownCast = null; return; }
        double pos = player.SkillSequencePosition, prev = ownKeyPos;
        if (pos < 0) return;
        ownKeyPos = pos;
        uint now = (uint)Time.GetTicksMsec();   // GetTickCountMs2
        bool charging = ownCast.Skill.ChargingSkill;
        // the keys in time order: the earliest key after the last handled time, all kinds at that time, then on from it
        double t;
        while ((t = SkillLoop.NextTextKey(prev, pos, ownLoopStartKey, ownChargingComboKey, ownPointTime, ownChargingAttackKey,
                    ownLoopEndKey)) >= 0)
        {
            if (ownLoopStartKey == t) ownLoop.OnLoopStart(ownLoopStartKey, now);
            if (ownChargingComboKey == t) ownLoop.OnChargingCombo(charging, CastKeyDown(ownCast.Key), now, ownChargingComboTick);
            while (ownPointTime == t)
            {
                int attack = ownAttack;
                ownLoop.OnAttackPoint(charging);
                FireOwnPoint(player);
                if (ownCast == null) return;
                if (ownPointTime == t && ownAttack == attack) break;
            }
            if (ownChargingAttackKey == t) ownLoop.OnChargingAttack(charging);
            if (ownLoopEndKey == t)
            {
                OwnLoopEnd(player, now);
                if (ownKeyPos != pos) return;   // the next round's seek: the keys read on from there next frame
            }
            prev = t;
        }
    }

    /// <summary>
    /// TextKeyEvent "loopend" (stateskill.cpp:869-944) for the own player: <see cref="SkillLoop.OnLoopEnd"/>; for a next round
    /// m_nAttack 0 and m_bSentCharging off, the record marked (m_n98), then a skill still cooling without a charge use (and not a
    /// charging skill) queues the running record (SetChargeRecord), else the round goes (<see cref="SendOwnRound"/>); an
    /// unqueued round plays the loop again (<see cref="SeekOwnLoop"/>).
    /// STILL-OPEN: the record's loop effect stop (m_n88), the re-aim (vfunc_100) and the turn to the record's rotation
    /// (SetTargetYaw / vfunc_21, :883-896), and the weapon PC's sequence (FUN_141663be0).
    /// </summary>
    void OwnLoopEnd(Player me, uint now)
    {
        // fEventTime: the key's own time here (only its being non-zero is read, by the hold check)
        if (!ownLoop.OnLoopEnd(ownLoopEndKey, ownLoopEndKey, SkillCast.MotionSpeedScale(ownCast), now)) return;
        ownAttack = 0;
        ownSync.ClearCharging();
        ownCast.Charged = true;
        if (Net.LiveSession.Instance is { } live)
        {
            uint tick = (uint)Net.LiveSession.CastTick();
            bool ready = live.Cooltimes.CanUseRechargeSkill(ownCast.Skill, tick);
            float cool = live.Cooltimes.Remain(ownCast.Skill.Id, false, tick);
            if (!ready && cool > 0f && !ownCast.Skill.ChargingSkill) { ownQueued = ownCast; ownCast.QueuedCharge = true; }
            else SendOwnRound(live, ownCast, tick);
        }
        if (!ownCast.QueuedCharge) SeekOwnLoop(me, now);
        else ownPointTime = OwnPointTime(me);
    }

    /// <summary>
    /// A continue round's sends (loopend :921-924, FUN_1417f11d0 :1591-1595): the mode 3 (CSkillRecord::SendSkill), the round's
    /// cooldown (FUN_1417f16f0 @0x1417f16f0: a continue skill, not a charging skill's marked record; the loop's length at the
    /// state's speed less the charge tick, FUN_1417664e0 from the send tick with the combo flag) and the charge use
    /// (OnRechargeSkillUsed of the running record).
    /// STILL-OPEN: FUN_141767990 (the skill's level use) is not ported.
    /// </summary>
    void SendOwnRound(Net.LiveSession live, CastRecord r, uint now)
    {
        live.SendRound(r);
        if (r.Skill.Continue && !(r.Skill.ChargingSkill && r.Charged))
            live.Cooltimes.Start((uint)r.SendTick, r.Skill.Id,
                SkillLoop.RoundCooldown(ownLoopStartKey, ownLoopEndKey, ownLoop.Speed, ownLoop.ChargeTick), 0f, true, now);
        live.Cooltimes.OnRechargeSkillUsed(r.Skill, now);
        GD.Print($"cast: skill {r.Skill.Id} round {r.SendCount}");
    }

    /// <summary>
    /// The next round's start (loopend :930-942, FUN_1417f11d0 :1596-1607): the loop sequence again at loopstart plus the charge
    /// tick. The text keys read on from there; the loopstart key is taken as passed at the seek (an assumption: the client
    /// restarts the sequence with SetTargetAnimation and sets its offset at or after the key; loopstart's m_fChargeTime = 0 is
    /// what lets the hold check run for the next round).
    /// STILL-OPEN: m_nStartTick (the combo ticks) and FUN_1417f1a90 (move0 / move2) are not ported.
    /// </summary>
    void SeekOwnLoop(Player me, uint now)
    {
        double t = ownLoop.LoopStart + ownLoop.ChargeTick * 0.001;
        me.SeekSkillSequence(t);
        ownKeyPos = t;
        // the loopstart event (:844-868) only for a sequence with the key
        if (ownLoopStartKey >= 0) ownLoop.OnLoopStart(ownLoopStartKey, now);
        ownPointTime = OwnPointTime(me);
    }

    /// <summary>
    /// Each frame while the own skill state runs: StateSkill::SendChargeState @0x1417ec2f0 (stateskill.cpp:1271, from vfunc_8
    /// :724), the mode 2 sync once the state starts and whenever the owner moves (<see cref="SkillCast.SyncIfChanged"/>).
    /// Before it the continue skill's hold check (vfunc_8 :649-694, <see cref="SkillLoop.Hold"/>): a key bound to the skill's
    /// origin id held (<see cref="OwnSkillKeyHeld"/>), the loop keys, the charging skill flag (m_b111 = the skill's
    /// chargingSkill, :766) and FUN_141765a70 (<see cref="CastGate.CanCast"/>, only asked while a key is held).
    /// 0 B/frame while nothing changed and no key is held.
    /// Then the charging skill's release (:698-722, <see cref="SkillLoop.Release"/>): its cast key (the record's m_nA4,
    /// <see cref="CastKeyDown"/>) up outside m_bLoop skips the sequence to "charging_attack" (at 1/frequency, the kf's time
    /// here) and returns before the sync that frame.
    /// STILL-OPEN: the skill system's queued key (FUN_1417674a0(1), the Lapen input) does not count as held; mouse and pad
    /// bindings are not read; the record's rotation does not turn during the state (FUN_1417edc20).
    /// </summary>
    void TickOwnSync()
    {
        if (!OwnSkillStateRuns()) return;
        if (ownLoop.HoldCheckRuns(ownCast.Skill))
        {
            bool held = OwnSkillKeyHeld((int)ownCast.Skill.OriginId), charging = ownCast.Skill.ChargingSkill;
            bool castable = held && !charging && CastGate.CanCast(OwnHoldWorld(), ownCast.Skill.Id);
            ownLoop.Hold(held, ownLoopStartKey >= 0 && ownLoopEndKey >= 0, charging, castable);
        }
        // FUN_141b6fe90 without the key: the sequence's length
        float chargingAttack = ownChargingAttackKey >= 0 ? ownChargingAttackKey : (float)(player.Anim?.CurrentAnimationLength ?? 0);
        if (ownLoop.Release(ownCast.Skill.ChargingSkill, CastKeyDown(ownCast.Key), ownCast.Skill.DisableChargingAttackSkipFrame,
                chargingAttack, out bool seek))
        {
            if (seek) SeekOwnRelease(chargingAttack);
            return;
        }
        var live = Net.LiveSession.Instance;
        if (live == null) return;
        var g = player.GlobalPosition;
        var req = SkillCast.SyncIfChanged(ownSync, ownCast, new System.Numerics.Vector3(g.X, -g.Z, g.Y), ownLoop.Charging, ownLoop.Loop);
        if (req != null) live.SendSync(req);
    }

    /// <summary>
    /// The release's skip (vfunc_8 :708-714): the sequence moved to the charging_attack key. The keys at that time are read
    /// as not yet passed (an assumption: the client's SetOffset then Update lets the attack whose point is the key fire).
    /// </summary>
    void SeekOwnRelease(float t)
    {
        player.SeekSkillSequence(t);
        ownKeyPos = Math.BitDecrement((double)t);
    }

    /// <summary>IsKeyDown(m_nA4) (stateskill.cpp:701, :949): the cast's DIK key is down; false for 0 (a clicked slot) and while
    /// typing.</summary>
    bool CastKeyDown(int dik) =>
        dik != 0 && !UiFocus.IsTyping && GodotKeyOfDik(dik) is { } g && Input.IsPhysicalKeyPressed(g);

    /// <summary>vfunc_8 :651-660: a keyboard key CKeyTable::FUN_14142c6c0 lists for the skill (<see cref="KeyTable.IsKeyOfSkill"/>)
    /// is down (IsKeyDown). Not while typing.</summary>
    bool OwnSkillKeyHeld(int originId)
    {
        if (UiFocus.IsTyping || hudModel?.Keys is not { } keys) return false;
        var slots = hudModel.ServerKeys;
        for (int dik = 1; dik < 0xd4; dik++)
            if (keys.IsKeyOfSkill(dik, originId, slots) && GodotKeyOfDik(dik) is { } g && Input.IsPhysicalKeyPressed(g)) return true;
        return false;
    }

    /// <summary>The hold check's world: the quick slots' one, taken once per state, with the player's level, hp, sp, ep and
    /// meso read again each call (the cost check, FUN_14176a380).</summary>
    SlotWorld OwnHoldWorld()
    {
        if (ownHoldWorld == null) return ownHoldWorld = QuickSlotWorld();
        var p = hudModel.Player;
        ownHoldWorld.Level = p.Level; ownHoldWorld.Hp = p.HpCur; ownHoldWorld.Sp = p.SpCur; ownHoldWorld.Ep = p.EpCur;
        ownHoldWorld.Meso = p.Mesos;
        return ownHoldWorld;
    }

    /// <summary>
    /// StateSkill::FireAttacksAtPoint @0x1417f0f10: every attack in a row at the point fires (FUN_1417ecce0 through
    /// <see cref="SkillAttackFire.Fire"/>, the send through LiveSession.SendDamage), then the next attack's point.
    /// STILL-OPEN: CSkillRecord::FUN_141759ad0's z offset (the motion's m_fAC / m_fB8 and the record's cooldown and aim are not
    /// read) is 0.
    /// </summary>
    void FireOwnPoint(Player me)
    {
        if (ownCast.QueuedCharge) return;   // FireAttacksAtPoint :2276: a queued record fires nothing
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
                Kind = HitKind.Pc, Team = 1, Hp = 1, PartyId = live?.Party?.PartyId ?? 0,
                PvpOnOff = ownBuffs?.IsPvpOnOff ?? false,
                ForceApplyTarget = ownBuffs?.ForceApplyTarget ?? 0, Hidden = ownBuffs?.IsHidden ?? false });
            nextSerial ??= () => serialIds.Next(Net.LiveSession.Instance?.SelfObjectId ?? 0);
            for (int i = 0; i < n; i++)
            {
                var req = SkillAttackFire.Fire(ownCast, ownAttack, pos, rotZ, 0,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects), crtRand, nextSerial, hitQueried, hitIds,
                    (slotField?.PkMode ?? 0) != 0);
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
