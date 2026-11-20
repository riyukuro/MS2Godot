using System;
using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The own player's casts from the keyboard: the attack key so far.
public partial class HudScene
{
    readonly CastWorld castWorld = new();          // CSkillSystem's last cast time / skill, kept across casts
    Ms2.Formats.World.SkillTable castMotions;      // the motion effects, the table the replayed casts read
    // StateSkill's record and m_nAttack for the own cast whose sequence plays, the sequence, and the next point's text key time
    CastRecord ownCast;
    int ownAttack;
    string ownSequence;
    double ownPointTime = -1;
    static readonly SerialIds serialIds = new();   // GenerateSerialId's function static
    static readonly Random crtRandom = new();
    static readonly Func<int> crtRand = () => crtRandom.Next(0x8000);
    Func<long> nextSerial;
    readonly System.Collections.Generic.List<HitCandidate> hitObjects = new();
    readonly System.Collections.Generic.List<int> hitIds = new();

    /// <summary>
    /// A key press bound to the attack (action 0x49401): CKeyTable::GetSkillIdByKey (keytable.cpp:1452) gives the skill, then the
    /// request (<see cref="CastOwn"/>). True when the key is the attack key.
    /// STILL-OPEN: the quick slot keys (actions 500001..500025) and the skill expansion keys do not cast yet.
    /// </summary>
    bool CastKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || UiFocus.IsTyping || hudModel?.Keys == null) return false;
        bool attack = false;
        foreach (int dik in hudModel.Keys.KeysOfAction(SkillCast.AttackAction))
            if (GodotKeyOfDik(dik) == k.PhysicalKeycode) { attack = true; break; }
        if (!attack) return false;
        var w = QuickSlotWorld();
        var held = w.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        // STILL-OPEN: the right-hand item's own skill (an action whose normalAttack is 0) is not read; 0 here
        uint id = SkillCast.AttackKeySkill(w, castWorld, held, weaponActions, 0);
        if (id != 0) CastOwn(w, id, held);
        return true;
    }

    /// <summary>
    /// CSkillSystem::FUN_141764e30 for the own player (<see cref="SkillCast.Request"/>) at MyPC::GetSkillLevel, then the cast:
    /// FUN_141766cc0's reuse record, the mode 0 send, and the record's motion on the own body with its motion effect.
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
        SkillCast.Casted(castWorld, r);
        var live = Net.LiveSession.Instance;
        bool sent = live?.SendCast(r, hudModel.IsFeatureEnabled(174)) ?? false;
        var motion = r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion] : null;
        if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.PlaySkillSequence(motion.SequenceName, motion.SequenceSpeed))
            StartOwnAttacks(me, r);
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
        ownPointTime = OwnPointTime(me);
    }

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
            hitObjects.Add(new HitCandidate { Id = live?.SelfObjectId ?? 0, Position = pos, Body = SkillHit.PlayerBody(pos, rotZ), IsOwner = true });
            nextSerial ??= () => serialIds.Next(Net.LiveSession.Instance?.SelfObjectId ?? 0);
            for (int i = 0; i < n; i++)
            {
                var req = SkillAttackFire.Fire(ownCast, ownAttack, pos, rotZ, 0,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects), crtRand, nextSerial, hitIds);
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
        live.OwnStateCancelled += on;
        unwatchOwnSkill = () => live.OwnStateCancelled -= on;
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
