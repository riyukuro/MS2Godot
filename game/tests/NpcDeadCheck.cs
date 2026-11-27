using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_dead.tscn) of an npc's dead flag in NpcLayer (PCBase::IsDead, set by CNpc::vfunc_125):
/// - an npc added without the add packet's dead flag is alive;
/// - one added with it is dead at once (OnFieldAddNpc's vfunc_125(0));
/// - FieldDeadNpc makes the first dead (vfunc_125(1)), the body stays, a second one changes nothing;
/// - FieldRemoveNpc drops it.
/// - FieldAddNpc for an object id already present raises the old npc's Removed before the new one replaces it.
/// - the hold key's search (WorldService::FindNearestActor) finds a live npc's Hold_PointNN node and skips a dead one.
/// - A hit record (CNpc::vfunc_33/34/35) kills a living npc whose current hp stat is 0, whatever the total, the owner or the
///   hit kind; one with hp left stays alive.
/// - A cast's hit below 0 by the own player adds the npc's colour boost (vfunc_33's AddColorBoost): the next frame writes
///   1 + the npc's damagedColorScale to the color_boost of its body's character materials, and 1 once the boost has run out;
///   another's hit adds none.
/// - FUN_141098fb0: a cast's or a splash's hit below 0 plays the row's damageEffect, a dot's or one of 0 none; a death with
///   its sound plays the deadEffect, the add packet's dead flag none.
/// - The hit sound (vfunc_33 :1874-1880, vfunc_35 :1979-1982): a cast's hit below 0 not by the own player and a splash's below
///   0 play the attack's material on the row's defence material at the relation's volume; the own cast, a cast of 0, a dot
///   and an attack whose material cannot be told play none.
/// RemoteLayer's PlayCast returns for a dead npc on this flag. Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcDeadCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        var npcs = new NpcLayer(packs, builder, table, this);
        int npcId = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        Check(npcId != 0, "no npc row");
        FieldAddNpc Npc(int obj, bool hidden) => new FieldAddNpc { ObjectId = obj, NpcId = npcId, Hidden = hidden };
        npcs.Add(new NpcAddedEvent(0, Npc(1, false)));
        npcs.Add(new NpcAddedEvent(0, Npc(2, true)));
        Check(npcs.Count == 2, $"count {npcs.Count} after the adds");
        Check(!npcs.IsDead(1), "npc without the dead flag is dead");
        Check(npcs.IsDead(2), "npc with the dead flag is alive");
        Check(npcs.Deaths == 1, $"deaths {npcs.Deaths} after the adds");
        HoldNodes(npcs, Check);
        npcs.Remove(1, true);
        Check(npcs.IsDead(1) && npcs.Count == 2, "FieldDeadNpc did not kill and keep the npc");
        npcs.Remove(1, true);
        npcs.SetHp(2, 0, 100);
        Check(npcs.Deaths == 2, $"deaths {npcs.Deaths} after a second dead packet and hp 0 on a dead npc");
        npcs.Remove(1, false);
        Check(npcs.Count == 1 && !npcs.IsDead(1), "FieldRemoveNpc did not drop the npc");
        // FieldAddNpc for a present object id replaces the npc: the old one's NotifyRemoveNpc goes out first, as when it
        // is removed, so a marker keyed by the id goes with it
        var removed = new System.Collections.Generic.List<int>();
        npcs.Removed = removed.Add;
        npcs.Add(new NpcAddedEvent(0, Npc(2, false)));
        Check(removed.Count == 1 && removed[0] == 2, $"the replaced npc's removal was not raised ({removed.Count})");
        Check(npcs.Count == 1 && !npcs.IsDead(2), "the replacing add did not keep one live npc");
        // the hit's hp 0 check (vfunc_33 :1890, vfunc_34, vfunc_35): stat row 2 (current) is FieldAddNpc's Stats[2]
        FieldAddNpc WithHp(int obj, long cur) { var n = Npc(obj, false); n.Stats[0].Health = 100; n.Stats[2].Health = cur; return n; }
        npcs.Add(new NpcAddedEvent(0, WithHp(10, 40)));
        npcs.Add(new NpcAddedEvent(0, WithHp(11, 0)));
        npcs.Add(new NpcAddedEvent(0, WithHp(12, 0)));
        int deaths0 = npcs.Deaths;
        npcs.Hit(10, NpcHitKind.Cast, -60, true, HitOwner.Pc, null);
        Check(!npcs.IsDead(10), "a hit on an npc with hp left killed it");
        npcs.Hit(11, NpcHitKind.Cast, 0, false, HitOwner.Pc, null);
        Check(npcs.IsDead(11), "a cast's hit on an npc at hp 0 did not kill it");
        npcs.Hit(12, NpcHitKind.Dot, -5, false, HitOwner.Pc, null);
        Check(npcs.IsDead(12), "a dot's or splash's hit on an npc at hp 0 did not kill it");
        npcs.Hit(12, NpcHitKind.Dot, -5, false, HitOwner.Pc, null);
        Check(npcs.Deaths == deaths0 + 2, $"deaths {npcs.Deaths - deaths0} from the hits, not 2");
        // the colour boost: 1 + scale on its first frame (remain 1), back to 1 after its 0.1 s
        npcs.Add(new NpcAddedEvent(0, WithHp(20, 100)));
        npcs.Add(new NpcAddedEvent(0, WithHp(21, 100)));
        float scale = table.Find(npcId).DamagedColorScale;
        npcs.Hit(20, NpcHitKind.Cast, -10, true, HitOwner.Pc, null);
        npcs.Hit(21, NpcHitKind.Cast, -10, false, HitOwner.Pc, null);
        npcs.Process(0);
        Check(npcs.ColorBoostOf(20) == 1 + scale, $"boost {npcs.ColorBoostOf(20)} after the own hit, not {1 + scale}");
        Check(npcs.BoostMaterialsOf(20)?.Count > 0, "the boost found no character material on the npc's body");
        Check(BodyBoost(20) == 1 + scale, $"body color_boost {BodyBoost(20)} after the own hit");
        Check(npcs.ColorBoostOf(21) == 1, $"another's hit boosted the npc ({npcs.ColorBoostOf(21)})");
        npcs.Process(0.2f);
        Check(npcs.ColorBoostOf(20) == 1 && BodyBoost(20) == 1, $"boost {npcs.ColorBoostOf(20)}, body {BodyBoost(20)} after it ran out");
        // the damage and dead effects on the first row with a dead effect (a body without a model plays them too); the data
        // gives no npc a damageEffect, so this table's row takes one
        var played = new System.Collections.Generic.List<(int, string)>();
        npcs.EffectPlayed = (n, e) => played.Add((n, e));
        var deadRow = table.Rows.Where(r => !string.IsNullOrEmpty(r.DeadEffect)).MinBy(r => r.Id);
        var dmgRow = deadRow;
        Check(deadRow != null, "no row with a dead effect");
        if (deadRow != null)
        {
            if (string.IsNullOrEmpty(dmgRow.DamageEffect)) dmgRow.DamageEffect = dmgRow.DeadEffect + "#damage";
            FieldAddNpc Of(int obj, int id, bool dead) { var n = new FieldAddNpc { ObjectId = obj, NpcId = id, Hidden = dead }; n.Stats[0].Health = 100; n.Stats[2].Health = 100; return n; }
            npcs.Add(new NpcAddedEvent(0, Of(30, dmgRow.Id, false)));
            npcs.Hit(30, NpcHitKind.Cast, -10, false, HitOwner.Pc, null);
            npcs.Hit(30, NpcHitKind.Dot, -10, true, HitOwner.Pc, null);
            npcs.Hit(30, NpcHitKind.Cast, 0, true, HitOwner.Pc, null);
            npcs.Hit(30, NpcHitKind.Splash, -10, false, HitOwner.Pc, null);
            Check(played.Count == 2 && played.All(p => p == (dmgRow.Id, dmgRow.DamageEffect)),
                $"damage effects {played.Count} from a cast, a dot, a cast of 0 and a splash, not the cast's and the splash's");
            played.Clear();
            npcs.Add(new NpcAddedEvent(0, Of(31, deadRow.Id, true)));
            Check(played.Count == 0, "the add packet's dead flag played the dead effect");
            npcs.Add(new NpcAddedEvent(0, Of(32, deadRow.Id, false)));
            npcs.Remove(32, true);
            Check(played.Count == 1 && played[0] == (deadRow.Id, deadRow.DeadEffect), $"dead effects {played.Count} from FieldDeadNpc, not its row's one");
        }
        // the hit sound (vfunc_33 :1874-1880, vfunc_35 :1979-1982): a cast's below 0 not by the own player, a splash's below 0
        // by anyone; the attack's material by the owner (1 for none, an npc's attack's, nothing when it cannot be told), the
        // row's defence material, the relation's volume (the data's VolumePcToNpc, read with atof, for none and for another
        // (0.3 on these tables, 0.5 only without the key), before FieldAudio's VolumeOtherPc; VolumeMyPcToNpc for the own player)
        var sounds = new System.Collections.Generic.List<(int npc, int atk, int def, float vol)>();
        npcs.HitSoundPlayed = (n, a, d, v) => sounds.Add((n, a, d, v));
        var soundRow = table.Find(npcId);
        if (soundRow != null)
        {
            npcs.Add(new NpcAddedEvent(0, WithHp(40, 100)));
            npcs.Hit(40, NpcHitKind.Cast, -10, true, HitOwner.None, null);
            npcs.Hit(40, NpcHitKind.Cast, -10, false, HitOwner.Npc, SkillAttackDamage.None);
            npcs.Hit(40, NpcHitKind.Cast, 0, false, HitOwner.Npc, SkillAttackDamage.None);
            npcs.Hit(40, NpcHitKind.Cast, -10, false, HitOwner.Pc, null);
            npcs.Hit(40, NpcHitKind.Dot, -10, false, HitOwner.None, null);
            npcs.Hit(40, NpcHitKind.Splash, -10, false, HitOwner.None, null);
            npcs.Hit(40, NpcHitKind.Splash, -10, true, HitOwner.Pc, SkillAttackDamage.None);
            int def = soundRow.DefenceMaterial;
            var constants = Ms2Godot.World.FieldContents.Constants(packs);
            float npcVol = NpcHitSound.Volume(CombatVoice.ObjectKind(HitOwner.Npc, false, false), constants);
            float noneVol = NpcHitSound.Volume(CombatVoice.ObjectKind(HitOwner.None, false, false), constants);
            Check(sounds.Count == 2 && sounds[0].npc == soundRow.Id && sounds[0].atk == 0 && sounds[0].def == def
                    && Mathf.IsEqualApprox(sounds[0].vol, npcVol) && sounds[1].atk == 1 && sounds[1].def == def
                    && Mathf.IsEqualApprox(sounds[1].vol, noneVol),
                $"hit sounds [{string.Join(", ", sounds)}], not an npc owner's cast at {npcVol} and an ownerless splash at {noneVol}");
        }
        GD.Print($"npc dead: npc {npcId}, deaths {npcs.Deaths}, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);

        float? BodyBoost(int id)
        {
            // every character material on the npc's body carries the written scale, the nif's own value replaced
            var mats = npcs.BoostMaterialsOf(id);
            if (mats == null || mats.Count == 0) return null;
            float v = mats[0].GetShaderParameter(ShaderParam.ColorBoost).AsSingle();
            foreach (var m in mats) if (m.GetShaderParameter(ShaderParam.ColorBoost).AsSingle() != v) return float.NaN;
            return v;
        }
    }

    /// <summary>The hold key's nearest-actor search over the npcs: a live npc's Hold_PointNN bone is found within range,
    /// the dead one is skipped, a range below the distance or a nearer pick finds none, and the poll allocates nothing.</summary>
    static void HoldNodes(NpcLayer npcs, System.Action<bool, string> check)
    {
        Vector3 at = default;
        foreach (int id in new[] { 1, 2 })
        {
            if (!npcs.HoldInfo(id, out _, out _, out var visual) || visual == null) { check(false, $"npc {id}: no model for the hold check"); return; }
            var sk = visual.GetNodeOrNull<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
            if (sk == null) { check(false, $"npc {id}: no skeleton"); return; }
            int b = sk.AddBone("Hold_Point00");
            sk.SetBonePosePosition(b, new Vector3(0, 100, 0));
            at = sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin;
        }
        var from = at + new Vector3(10, 0, 0);
        float best = float.MaxValue;
        int got = 0;
        string node = null;
        npcs.NearestHold(from, 230, ref best, ref got, ref node);
        check(got == 1 && node == "Hold_Point00" && Mathf.Abs(best - 10) < 0.01f, $"npc hold: {got} '{node}' at {best} (want the live npc 1 at 10)");
        float d1 = best;
        best = float.MaxValue; int none = 0; string n0 = null;
        check(!npcs.NearestHold(from, 5, ref best, ref none, ref n0) && none == 0, "npc hold: found beyond the range");
        best = 5;
        check(!npcs.NearestHold(from, 230, ref best, ref none, ref n0) && best == 5, "npc hold: replaced a nearer pick");
        // FindNearestActor's tie: of equal distances the lowest object id wins
        best = d1; int tie = 5; string tn = "x";
        check(npcs.NearestHold(from, 230, ref best, ref tie, ref tn) && tie == 1, $"npc hold tie: npc 1 did not beat id 5 ({tie})");
        best = d1; tie = 1; tn = "x";
        check(!npcs.NearestHold(from, 230, ref best, ref tie, ref tn) && tn == "x", "npc hold tie: an equal pick was replaced");
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { best = float.MaxValue; none = 0; npcs.NearestHold(from, 230, ref best, ref none, ref n0); }
        long alloc = System.GC.GetAllocatedBytesForCurrentThread() - a0;
        check(alloc == 0, $"npc hold poll allocates {alloc} B per 100");
        GD.Print($"npc dead: hold node of npc {got} '{node}' at {best:0.##}, {alloc} B per 100 lookups");
    }
}
