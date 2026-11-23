using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using SkillMotion = Ms2.Formats.World.SkillMotion;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_force.tscn) of the npc force zones WorldService::FUN_141bc8420 reads: an npc counts while it
/// is in StateSkill (0x10) with a cast record whose motion has a push cylinder (NpcLayer.ForceSources).
/// - a sync block enters its state only on a new serial (CNpc::ApplySyncBlock m_nDAC), and not for 0x12 or 0;
/// - a cast with a sequence enters 0x10 (StateSkill::PlayCastStartEffect, PCBase::FUN_141673ce0); the same uid and motion again
///   does not, a new one does;
/// - the zone is centred at the npc and pushes a point near it outward (ForceZone.Step);
/// - the last motion's sequence end leaves the state for StateIdle; a motion with more to come holds it 5 s after its end
///   (StateSkill::FUN_1417ee120);
/// - a dead npc is in 0xc, has no zone and takes no cast.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcForceCheck : Node3D
{
    NpcLayer npcs;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static bool OneShot(string n) => n.Length > 0 && !n.StartsWith("Idle", System.StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Walk", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Run", System.StringComparison.OrdinalIgnoreCase) && !n.Contains("Idle", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Dead", System.StringComparison.OrdinalIgnoreCase) && !n.Contains("Loop", System.StringComparison.OrdinalIgnoreCase);

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new NpcTable(packs);
        npcs = new NpcLayer(packs, builder, table, this);
        _ = Run(table);
    }

    public override void _Process(double delta) => npcs?.Process((float)delta);

    void Block(int obj, byte state, ushort serial) =>
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Position = new Vector3S(100, 0, 0), State = state, Serial = serial, Sequence = -1, AnimSpeed = 1 }));

    /// <summary>Waits frames until <paramref name="done"/> or <paramref name="seconds"/> of the layer's time.</summary>
    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> done, float seconds)
    {
        float t0 = npcs.Now;
        while (!done() && npcs.Now - t0 < seconds) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return done();
    }

    async System.Threading.Tasks.Task Run(NpcTable table)
    {
        int npcId = 0; string seq = null;
        int obj = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 21000004, 21000005, 11000001, 11000002, 11000003 })
        {
            if (table.Find(id) == null) continue;
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = ++obj, NpcId = id }));
            string picked = null;
            if (npcs.SequenceId(obj, n => OneShot(n) && (picked = n) != null) >= 0) { npcId = id; seq = picked; break; }
        }
        if (npcId == 0) { Done("no candidate npc with a one-shot sequence"); return; }
        var zones = new List<ForceZone.Source>();
        var push = new PushCylinder(300, 0, 1000, 100, 300);
        var motion = new SkillMotion { SequenceName = seq, PushCylinder = push };
        var none = new SkillMotion { SequenceName = seq };

        Block(obj, 1, 1);
        Check(npcs.StateOf(obj) == 1, $"a block of state 1 left the npc in {npcs.StateOf(obj)}");
        npcs.ForceSources(zones);
        Check(zones.Count == 0, $"an idle npc gave {zones.Count} zones");

        string anim = npcs.PlaySequence(obj, seq);
        Check(anim != null, $"npc {npcId} did not play {seq}");
        Check(npcs.BeginCast(obj, 77, 0, true, motion, anim), "the first cast was not taken");
        Check(npcs.StateOf(obj) == 0x10, $"a cast left the npc in {npcs.StateOf(obj)}, not 0x10");
        npcs.ForceSources(zones);
        Check(zones.Count == 1 && zones[0].Cylinder == push, $"a casting npc gave {zones.Count} zones");
        var at = npcs.PoseOf(obj)?.Pos ?? default;
        if (zones.Count == 1) Check((zones[0].Centre - at).Length() < 0.01f, $"the zone centre {zones[0].Centre} is not the npc at {at}");
        var feet = at + new System.Numerics.Vector3(500, 0, 0);
        bool pushed = ForceZone.Step(feet, false, 1f, null, zones, out var vel, out _);
        Check(pushed && vel.X > 299f && System.MathF.Abs(vel.Y) < 0.01f, $"a point 500 off was pushed {pushed} by {vel}, not 300 outward");

        Check(!npcs.BeginCast(obj, 77, 0, true, motion, anim), "the same uid and motion entered the skill state again");
        Block(obj, 2, 1);
        Check(npcs.StateOf(obj) == 0x10, $"a block of the last serial changed the state to {npcs.StateOf(obj)}");
        Block(obj, 0x12, 2);
        Check(npcs.StateOf(obj) == 0x10, $"a 0x12 block changed the state to {npcs.StateOf(obj)}");
        Block(obj, 0, 3);
        Check(npcs.StateOf(obj) == 0x10, $"a gosNone block changed the state to {npcs.StateOf(obj)}");
        Block(obj, 1, 4);
        Check(npcs.StateOf(obj) == 1, $"a block of state 1 and a new serial left the npc in {npcs.StateOf(obj)}");
        npcs.ForceSources(zones);
        Check(zones.Count == 0, $"an npc out of the skill state gave {zones.Count} zones");
        Check(!npcs.BeginCast(obj, 77, 0, true, motion, anim), "the record already held entered the skill state");
        Check(npcs.StateOf(obj) == 1, $"the held record changed the state to {npcs.StateOf(obj)}");

        // a cast without a push cylinder: in the state, no zone
        Check(npcs.BeginCast(obj, 78, 0, true, none, npcs.PlaySequence(obj, seq)), "a new cast uid was not taken");
        npcs.ForceSources(zones);
        Check(zones.Count == 0, $"a cast without a push cylinder gave {zones.Count} zones");

        // the last motion's end: StateIdle
        bool ended = await Until(() => npcs.StateOf(obj) != 0x10, 15f);
        Check(ended && npcs.StateOf(obj) == 1, $"the last motion's end left the npc in {npcs.StateOf(obj)}, not 1");
        // a motion with more to come: held after its end, then idle 5 s later
        string anim2 = npcs.PlaySequence(obj, seq);
        Check(npcs.BeginCast(obj, 79, 0, false, motion, anim2), "a cast with motions left was not taken");
        bool over = await Until(() => npcs.CurrentAnimation(obj) != anim2, 15f);
        float end = npcs.Now;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(over && npcs.StateOf(obj) == 0x10, $"a motion with more to come: sequence over {over}, state {npcs.StateOf(obj)} at its end, not held in 0x10");
        bool left = await Until(() => npcs.StateOf(obj) != 0x10, 10f);
        float held = npcs.Now - end;
        Check(left && npcs.StateOf(obj) == 1, $"a motion with more to come left the npc in {npcs.StateOf(obj)}");
        Check(held > 4.9f && held < 5.3f, $"a motion with more to come left the state {held:0.00} s after its end, not 5 s");

        Check(npcs.BeginCast(obj, 80, 0, true, motion, npcs.PlaySequence(obj, seq)), "a cast before the death was not taken");
        npcs.Die(obj, false);
        Check(npcs.StateOf(obj) == 0xc, $"a dead npc is in {npcs.StateOf(obj)}, not 0xc");
        npcs.ForceSources(zones);
        Check(zones.Count == 0, $"a dead npc gave {zones.Count} zones");
        Check(!npcs.BeginCast(obj, 81, 0, true, motion, seq), "a dead npc took a cast");
        Block(obj, 1, 9);
        Check(npcs.StateOf(obj) == 0xc, $"a block moved the dead npc to {npcs.StateOf(obj)}");
        Done($"npc {npcId} {seq}, push {vel}, held {held:0.00} s");
    }

    void Done(string head)
    {
        GD.Print($"npc force: {head}");
        GD.Print(fails.Count == 0 ? "npc force: ok" : "npc force: FAIL");
        foreach (var f in fails) GD.Print("  " + f);
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
