using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The own PC's reaction to the hits that land on it (PC::vfunc_33, pc.cpp:885): its push and its StateHit.
public partial class LiveSession
{
    /// <summary>The own special ability 0x27 total (CEquipment::GetAbilityTotal, the push resistance level); set by the HUD
    /// that keeps the worn part and the own buffs.</summary>
    public Func<int> OwnPushResistLevel = () => 0;
    /// <summary>The own buffs' super armor (AdditionalCliList::GetSuperArmor's buff part) on a field of map type 9 or not.</summary>
    public Func<bool, uint> OwnBuffSuperArmor = _ => 0;

    /// <summary>The objects' hit effect throttle (the GameObject's +0x11c state FUN_14146c990 reads): one for the own and the
    /// other players' magics and the damage's hit effect, emptied with each field and an object's entry when it leaves.</summary>
    public readonly Ms2.Ui.Hud.HitEffectThrottle HitEffects = new();

    readonly StateHitData ownHitData = new();
    int hitMapId = -1, hitMapType;

    /// <summary>The current field's map type (CFieldData), read once per field.</summary>
    int HitMapType()
    {
        int id = FieldId ?? 0;
        if (id != hitMapId)
        {
            hitMapId = id;
            hitMapType = FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id)?.MapType ?? 0;
        }
        return hitMapType;
    }

    /// <summary>
    /// OnSkillDamage mode 1 (FUN_14171cdf0, scenestategame.cpp:5607) for the records on the own PC: each record goes to the
    /// magic service and on to PC::vfunc_33 (<see cref="HitReaction.OnSkillHit"/>). The record's position is the packet's,
    /// its direction the packet's direction only for an arrow record (FUN_14175acc0, SkillAttackData.ArrowRecord; otherwise FUN_14145e1a0 aims a
    /// local copy and the record's stays zero) and its hit facing is the packet's direction short (Init's ptHit, :5624).
    /// The ground of GetPush is a map type of 9 or 3; the buffs' super armor counts as on map type 9 for type 9.
    /// STILL-OPEN: the magic service's delivery time (FUN_14146b440) is taken as at once; an npc caster with property tag
    /// 0x11 moves the record's position to the aimed one (needs FUN_14145e1a0); the cast's own super armor
    /// (CSkillRecord::GetSuperArmor) is 0 here; modes 5 and 6 (vfunc_35) and mode 3 (vfunc_34) do not react yet.
    /// </summary>
    void ReactToHits(SkillDamage d)
    {
        if (d.Mode != 1 || player == null || remote == null) return;
        SkillAttackDamage attack = null;
        bool looked = false, arrowRecord = false;
        foreach (var t in d.Targets)
        {
            if (t.TargetId != selfObject) continue;
            if (!looked)
            {
                attack = remote.AttackDamage(d.SkillId, d.Level, d.Motion, d.AttackPoint);
                arrowRecord = remote.AttackData(d.SkillId, d.Level, d.Motion, d.AttackPoint)?.ArrowRecord ?? false;
                looked = true;
            }
            if (attack == null) return;
            long total = 0;
            foreach (var h in t.Hits) total += h.Damage;
            int mapType = HitMapType();
            bool ground = mapType is 9 or 3;
            uint armor = HitReaction.SuperArmor(player.ClientStateId, 0, OwnBuffSuperArmor(mapType == 9));
            var recordDir = arrowRecord ? d.DirectionShort.ToVector3() : System.Numerics.Vector3.Zero;
            (System.Numerics.Vector3, System.Numerics.Vector3)? attacker = null;
            if (remote.PoseOf(d.OwnerId) is { } pose)
            {
                SkillHit.Axes(pose.RotZ, out var forward, out _);
                attacker = (pose.Pos, forward);
            }
            ownHitData.Clear();
            var change = HitReaction.OnSkillHit(player, attack, total, armor, Invincible.Active, ground, OwnPushResistLevel(),
                d.Position.ToVector3(), recordDir, d.DirectionShort.Z, attacker, ownHitData);
            if (change == HitReaction.Change.Hit) player.EnterHit(ownHitData, ownHitData.Duration > 0f);   // vfunc_59(0xd, data, 0, duration > 0), pc.cpp:943
            else if (change == HitReaction.Change.Idle) player.ReactIdle();
            else if (total < 0)
            {
                // pc.cpp:952: the state kept takes vfunc_6; StateSummon, StateRecall and StateTreeWatering enter StateIdle
                int st = ownStateExit.State;
                if (st != 0 && Me.State == st && ownStateExit.Hit() && Me.LeaveServerState(st))
                {
                    if (LiveCheck) Godot.GD.Print($"live: server state 0x{st:x} left by a hit");
                    SyncOwnStateCast();
                    player.ReactIdle();
                }
                EndTalkOfOwnState();
            }
        }
    }
}
