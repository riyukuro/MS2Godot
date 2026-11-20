using System;
using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The own player's casts from the keyboard: the attack key so far.
public partial class HudScene
{
    readonly CastWorld castWorld = new();          // CSkillSystem's last cast time / skill, kept across casts
    Ms2.Formats.World.SkillTable castMotions;      // the motion effects, the table the replayed casts read

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
        if (motion != null && !string.IsNullOrEmpty(motion.SequenceName)) me.PlaySkillSequence(motion.SequenceName, motion.SequenceSpeed);
        castMotions ??= new Ms2.Formats.World.SkillTable(assets.Packs);
        if (castMotions.Motion((int)r.Skill.Id, r.Level.Level, r.Motion)?.MotionEffect is { } fx) live?.PlayFieldEffect(fx, g, me.Visual?.Rotation.Y ?? 0);
        GD.Print($"cast: skill {r.Skill.Id} level {r.Level.Level} motion {r.Motion}{(sent ? " sent" : " (offline, not sent)")}");
    }
}
