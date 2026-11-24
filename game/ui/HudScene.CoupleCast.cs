using Godot;
using Ms2.Ui.Skills;
using Ms2Godot.Movement;

namespace Ms2Godot.Ui;

/// <summary>
/// The own cast of a couple emotion: StateSkill::FUN_1417eaa50's main type 0xf branch (stateskill.cpp:1914-1990,
/// <see cref="CoupleEmotionCast.Use"/>) on the couple action dialog's selected character, with the field's physics for the
/// spot test (FUN_140b84dc0) and the client's notices.
/// </summary>
public partial class HudScene
{
    /// <summary>
    /// MyPC::vfunc_102 (mypc.cpp:1205-1233) for a couple emotion record: the branch's checks and notices, then on success
    /// the start data and BuddyEmote mode 0. As for any emotion the skill state never starts (FUN_1417eaa50 returns 0); the
    /// reuse record, FUN_141767990, the cast's cooldown when *pResult was set and the charge use follow.
    /// </summary>
    bool UseOwnCouple(Player me, Net.LiveSession live, CastRecord r, uint now)
    {
        var g = me.GlobalPosition;
        var input = new CoupleEmotionCast.Input
        {
            Jumping = me.State == Player.MoveState.Jump,
            HasDialog = coupleAction != null,
            Selected = coupleAction?.SelectedCharacter ?? 0,
            FindPeer = id => live?.CouplePeerOf(id),
            Blocked = id => live?.Buddies.IsBlocked(id) ?? false,
            ShownBoxExtra = Ms2.Ui.Hud.MessageBoxQueue.Current() is { } box ? box.Extra : null,
            InCubeMode = live?.CubeCraft.InCubeMode ?? false,
            OnWater = me.OnWater,
            StateId = me.ClientStateId,
            ObjectId = live?.Me.ObjectId ?? 0,
            Position = new System.Numerics.Vector3(g.X, -g.Z, g.Y),
            Placeable = p => CoupleEmotionCast.Placeable(new CouplePlaceProbe(me.GetWorld3D().DirectSpaceState), p),
            MaxDistance = World.FieldContents.Constants(assets.Packs).Int("CoupleEmotionMaxDistance", CoupleEmotionCast.DefaultMaxDistance),
        };
        var o = CoupleEmotionCast.Use(r.Skill, input);
        if (o.Notice != 0)
        {
            var t = hudModel.Tables;
            // ShowSystemNotice(FormatStringId(id, name), 1, 1) for 0xcef / 0xcee, FUN_141275260(id, 1, 1) for the others
            SystemNoticeText(o.NoticeName != null ? Ms2.Quest.ClientFormat.Format(t.Common(o.Notice), o.NoticeName) : t.Common(o.Notice));
        }
        bool sent = false;
        if (o.Start != null && live != null)
        {
            // vfunc_59(0x3a, data, 0x93, 1) (:1980), then BuddyEmote mode 0 (:1982-1986)
            CoupleReady(live)?.EnterCouple(o.Start, CoupleEmotionState.Waiting);
            sent = live.SendBuddyEmote(o.Send);
        }
        SkillCast.Casted(castWorld, r);
        SampleAbilityRate();
        CompactUsed(r);
        if (o.Handled && live != null) live.Cooltimes.StartCast(r, null, 0f, now);
        live?.Cooltimes.OnRechargeSkillUsed(r.Skill, now);
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"cast: couple emotion {r.Skill.Id} on {input.Selected}: {(o.Start != null ? $"asked{(sent ? " sent" : "")}" : o.Notice != 0 ? $"notice 0x{o.Notice:x}" : "nothing")} state {me.ClientStateId}");
        return true;
    }

    /// <summary>The field's physics for FUN_140b84dc0, on the default groups 0x389 (CPhysXHelper with no mask).</summary>
    sealed class CouplePlaceProbe : ICouplePlaceProbe
    {
        readonly PhysicsDirectSpaceState3D space;
        public CouplePlaceProbe(PhysicsDirectSpaceState3D space) { this.space = space; }

        static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

        Godot.Collections.Dictionary Down(System.Numerics.Vector3 from, float len)
        {
            var at = G(from);
            using var q = PhysicsRayQueryParameters3D.Create(at, at + Vector3.Down * len, MoveConfig.DownRayGroups);
            q.HitBackFaces = false;
            return space.IntersectRay(q);
        }

        /// <summary>GetGroundDistance (physxhelper.cpp:475): a down ray within the length.</summary>
        public bool GroundWithin(System.Numerics.Vector3 point, float len)
        {
            using var hit = Down(point, len);
            return hit.Count != 0;
        }

        /// <summary>CheckCapsuleOverlap: the PC capsule standing at the point.</summary>
        public bool CapsuleOverlaps(System.Numerics.Vector3 point)
        {
            using var shape = new CapsuleShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height };
            using var sq = new PhysicsShapeQueryParameters3D { Shape = shape, CollisionMask = MoveConfig.DownRayGroups,
                Transform = new Transform3D(Basis.Identity, G(point) + Vector3.Up * (MoveConfig.Height / 2)) };
            using var over = (Godot.Collections.Array)space.IntersectShape(sq, 1);
            return over.Count != 0;
        }

        /// <summary>CPhysXHelper::RayCastFromPoint(from, hit, dir, len, 0) (FindPlacement's line between the two spots): a
        /// hit on the default groups within the length.</summary>
        public bool RayHits(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float len)
        {
            var at = G(from);
            using var q = PhysicsRayQueryParameters3D.Create(at, at + G(dir) * len, MoveConfig.DownRayGroups);
            using var hit = space.IntersectRay(q);
            return hit.Count != 0;
        }

        /// <summary>RayCastFixedDirection down, unlimited: every field collider stands for an entity (kHit.m_hitEntity);
        /// another controller is none. STILL-OPEN: the entity's own refusals (its 0x15014205 tag giving type 0x40, the
        /// models 0xd3b04f05 / 0x032767dd / 0x72f49865, a model name WorldService registered, statecoupleemotion.cpp:1298-1309)
        /// are not on our colliders, so none refuses.</summary>
        public bool EntityBelow(System.Numerics.Vector3 point, out bool blocked)
        {
            blocked = false;
            using var hit = Down(point, MoveConfig.UnlimitedRay);
            if (hit.Count == 0) return false;
            var col = hit[PhysicsKeys.Collider].AsGodotObject();
            return !(col is CharacterBody3D || col is Node n && n.HasMeta(MoveConfig.ControllerMeta));
        }
    }
}
