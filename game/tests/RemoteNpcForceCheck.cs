using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Ms2Godot.Net;
using SkillMotion = Ms2.Formats.World.SkillMotion;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_npc_force.tscn) of the npc force zones through RemoteLayer, the way a field feeds them:
/// a skill of two motions with a push cylinder (added to the layer's skill table) cast by an npc with SkillUsedEvent.
/// - The first motion (RemoteLayer.PlayCast -> NpcLayer.BeginCast with motions left) puts the npc in 0x10 and the layer fills
///   the own pc's ForceNpcs with its zone each frame; the pc's push (Player.ForcePush) is outward from the npc.
/// - The first motion holds the state 5 s past its sequence; the second (the level's last) leaves it at its end.
/// - A despawn (NpcRemovedEvent) and an add over the same object id each drop the zone the next frame; a death
///   (NpcRemovedEvent dead) too.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteNpcForceCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const int Skill = 99000001;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    RemoteLayer layer;
    NpcLayer npcs;
    Player pc;
    readonly NxController.FaceGrids faceGrids = new();

    static bool OneShot(string n) => n.Length > 0 && !n.Contains("Idle", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Walk", System.StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Run", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Dead", System.StringComparison.OrdinalIgnoreCase) && !n.Contains("Loop", System.StringComparison.OrdinalIgnoreCase);

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        layer = new RemoteLayer(field, builder, 1);
        npcs = (NpcLayer)typeof(RemoteLayer).GetField("npcs", Private).GetValue(layer);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        // by name, so the check builds and fails without the layer's own pc hook
        var hook = typeof(RemoteLayer).GetField("ForcedPc");
        Check(hook != null, "the layer has no own pc hook for its npc force zones (RemoteLayer.ForcedPc)");
        hook?.SetValue(layer, (System.Func<Player>)(() => pc));
        _ = Run(packs);
    }

    async System.Threading.Tasks.Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    float maxFrame;   // the longest frame Until saw: each end is seen up to a frame late

    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> done, float seconds)
    {
        float t0 = npcs.Now;
        while (!done() && npcs.Now - t0 < seconds)
        {
            float f0 = npcs.Now;
            await Frame();
            maxFrame = Mathf.Max(maxFrame, npcs.Now - f0);
        }
        return done();
    }

    void Add(int obj, int npcId) => layer.Tracker.Events.Enqueue(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = obj, NpcId = npcId }));

    void Cast(int obj, long uid, byte motion) => layer.Tracker.Events.Enqueue(new SkillUsedEvent(0,
        new SkillUse(uid, 0, obj, Skill, 1, motion, default, new System.Numerics.Vector3(1, 0, 0), default, 1f, false, false, 0, null)));

    /// <summary>The pc's push this frame (Player.ForcePush, the move of a 1/60 s frame), Godot axes.</summary>
    Vector3 Push()
    {
        var m = typeof(Player).GetMethod("ForcePush", Private);
        var args = new object[] { 1f / 60f, false, false };
        return m == null ? default : (Vector3)m.Invoke(pc, args);
    }

    async System.Threading.Tasks.Task Run(PackSet packs)
    {
        var table = new NpcTable(packs);
        int obj = 0, npcId = 0; string seq = null;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 21000004, 21000005, 11000001, 11000002, 11000003 })
        {
            if (table.Find(id) == null) continue;
            Add(++obj, id);
            await Frame();
            string picked = null;
            if (npcs.SequenceId(obj, n => OneShot(n) && (picked = n) != null) >= 0) { npcId = id; seq = picked; break; }
        }
        if (npcId == 0) { Done("no candidate npc with a one-shot sequence"); return; }

        // the skill: two motions of the npc's sequence, each with a push cylinder, in the table the layer reads
        // (an own empty table, so the shared SkillTable.For table of the data set is left as it is)
        var skills = new SkillTable();
        var push = new PushCylinder(300, 0, 1000, 100, 300);
        var row = new SkillLevelRow { Level = 1 };
        row.Motions.Add(new SkillMotion { SequenceName = seq, PushCylinder = push });
        row.Motions.Add(new SkillMotion { SequenceName = seq, PushCylinder = push });
        ((Dictionary<int, List<SkillLevelRow>>)typeof(SkillTable).GetField("skills", Private).GetValue(skills))[Skill] = new() { row };
        typeof(RemoteLayer).GetField("skillMotions", Private).SetValue(layer, skills);

        var at = npcs.Info(obj)?.root.GlobalPosition ?? default;
        pc.GlobalPosition = at + new Vector3(500, 0, 0);
        await Frame();
        Check(pc.ForceNpcs.Count == 0, $"an idle npc gave the pc {pc.ForceNpcs.Count} zones");

        // the first motion: motions left
        float c0 = npcs.Now;
        Cast(obj, 501, 0);
        await Frame();
        maxFrame = Mathf.Max(maxFrame, npcs.Now - c0);   // the cast's frame: its start is seen up to a frame late too
        Check(npcs.StateOf(obj) == 0x10, $"the first motion left the npc in {npcs.StateOf(obj)}, not 0x10");
        Check(pc.ForceNpcs.Count == 1 && pc.ForceNpcs[0].Cylinder == push, $"the casting npc gave the pc {pc.ForceNpcs.Count} zones");
        var p = Push();
        Check(p.X > 4.99f && p.X < 5.01f && Mathf.Abs(p.Z) < 0.01f, $"the pc 500 off the casting npc was pushed {p} in 1/60 s, not 300/s outward");
        float t0 = npcs.Now;
        bool left = await Until(() => npcs.StateOf(obj) != 0x10, 20f);
        float firstHeld = npcs.Now - t0;

        // the second motion: the level's last
        float c1 = npcs.Now;
        Cast(obj, 501, 1);
        await Frame();
        maxFrame = Mathf.Max(maxFrame, npcs.Now - c1);   // the cast's frame: its start is seen up to a frame late too
        Check(npcs.StateOf(obj) == 0x10, $"the second motion left the npc in {npcs.StateOf(obj)}, not 0x10");
        t0 = npcs.Now;
        bool lastLeft = await Until(() => npcs.StateOf(obj) != 0x10, 20f);
        float lastHeld = npcs.Now - t0;
        Check(left && lastLeft && npcs.StateOf(obj) == 1, $"the motions left the state: first {left}, last {lastLeft}, now {npcs.StateOf(obj)}");
        float slack = 2f * maxFrame + 0.05f;
        Check(Mathf.Abs(firstHeld - lastHeld - 5f) < slack,
            $"the first motion held {firstHeld:0.00} s and the last {lastHeld:0.00} s: not 5 s apart within {slack:0.00} s (the last flag)");
        await Frame();
        Check(pc.ForceNpcs.Count == 0 && Push() == Vector3.Zero, $"an npc out of the skill state left the pc {pc.ForceNpcs.Count} zones");

        // despawn
        Cast(obj, 502, 0);
        await Frame();
        Check(pc.ForceNpcs.Count == 1, $"a new cast gave the pc {pc.ForceNpcs.Count} zones");
        layer.Tracker.Events.Enqueue(new NpcRemovedEvent(0, obj, false));
        await Frame();
        Check(pc.ForceNpcs.Count == 0 && npcs.Info(obj) == null, $"a despawned npc left the pc {pc.ForceNpcs.Count} zones");

        // an add over the same object id
        Add(obj, npcId);
        await Frame();
        Cast(obj, 503, 0);
        await Frame();
        Check(pc.ForceNpcs.Count == 1, $"a cast on the added npc gave the pc {pc.ForceNpcs.Count} zones");
        Add(obj, npcId);
        await Frame();
        Check(pc.ForceNpcs.Count == 0, $"an add over the casting npc left the pc {pc.ForceNpcs.Count} zones");

        // death
        Cast(obj, 504, 0);
        await Frame();
        layer.Tracker.Events.Enqueue(new NpcRemovedEvent(0, obj, true));
        await Frame();
        Check(npcs.StateOf(obj) == 0xc && pc.ForceNpcs.Count == 0, $"a dead npc is in {npcs.StateOf(obj)} and left the pc {pc.ForceNpcs.Count} zones");
        Done($"npc {npcId} {seq}, push {p}, first motion held {firstHeld:0.00} s, last {lastHeld:0.00} s, longest frame {maxFrame:0.000} s");
    }

    void Done(string head)
    {
        GD.Print($"remote npc force: {head}");
        GD.Print(fails.Count == 0 ? "remote npc force: ok" : "remote npc force: FAIL");
        foreach (var f in fails) GD.Print("  " + f);
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
