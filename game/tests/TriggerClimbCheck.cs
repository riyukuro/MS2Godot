using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Movement;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_climb.tscn) of the rope / ladder trigger objects, on 02000290 and the ladders its
/// server trigger scripts set: 511-520 (trigger/02000290_bf/ladder.xml) and 531-536 (trigger/02000290_bf/main_2.xml).
/// In both scripts the first state sets each ladder to show 0, climbable 0, and a later state sets it to show 1,
/// climbable 1, with a guide line saying the ladder has appeared. The packets are the recorded ones: a KMS2 recording
/// of the field has the field entry's type 2 load with these ladders as 00 00 03000000 and a type 3 event per ladder of
/// 01 01 03000000 when they appear. The client's reader takes that as bool show, bool flag, i32 fade
/// (TriggerObjectRope::vfunc_2 @0x140de7670, triggeractioncli.cpp:740-742). Its apply step fades the entity by the show bool
/// (:744), sets its Climbable flag (0x4df0c0de, :745) to the flag and switches its colliders (:746).
/// What the player should see, per ladder: after the load, the ladder is hidden and the climb probe at its base
/// (PC::FUN_141659b80) does not take it; after the event, it is drawn and the probe finds a ladder there.
/// The fade runs on the field's fader; the check ticks it past the 0.3 s fade. Nothing else runs per frame. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerClimbCheck : Node
{
    const int FieldId = 2000290;
    static readonly int[] Ladders = { 511, 512, 513, 514, 515, 516, 517, 518, 519, 520, 531, 532, 533, 534, 535, 536 };

    readonly List<string> fails = new();

    public override void _Ready()
    {
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"trigger climb check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>A type 2 load of the given ladders, each with the recorded body show, climbable, fade 3.</summary>
    static byte[] Load(IReadOnlyList<int> ids, bool show, bool climbable)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(ids.Count);
        foreach (int id in ids) { w.Write(id); w.Write(show); w.Write(climbable); w.Write(3); }
        return ms.ToArray();
    }

    /// <summary>A type 3 event of one ladder.</summary>
    static byte[] Update(int id, bool show, bool climbable)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Update);
        w.Write(id); w.Write(show); w.Write(climbable); w.Write(3);
        return ms.ToArray();
    }

    void Check()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, FieldId) ?? throw new InvalidOperationException($"no field {FieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        AddChild(fb.World);
        // as Field.LoadField builds them, then binds the player's climbables
        var triggers = new FieldTriggers(map.Entities, fb.TriggerMeshDraws, fb.TriggerColliders, fb.TriggerFadeDraws);
        var climbs = Climbables.From(map);
        triggers.Bind(climbs);

        // the scripts' ids are rope / ladder objects of the field, each with a climbable entry
        var byId = new Dictionary<int, Climbables.Climb>();
        foreach (var c in climbs.All)
            if (c.Entity != null && TriggerObjects.KindOf(c.Entity.HasMixin) == TriggerObjectKind.Rope)
                byId[(int)c.Entity.Number("TriggerObjectID")] = c;
        foreach (int id in Ladders)
        {
            if (triggers.KindOf(id) != TriggerObjectKind.Rope) fails.Add($"{id}: kind {triggers.KindOf(id)?.ToString() ?? "none"}, not a rope / ladder");
            else if (!byId.ContainsKey(id)) fails.Add($"{id}: no climbable entry");
        }
        if (fails.Count > 0) return;
        // the ladders with colliders of their own (FieldBuilder.TriggerColliders), switched by the show bool (:746)
        var bodies = new Dictionary<int, FieldBuilder.TriggerCollider>();
        foreach (var (e, tc) in fb.TriggerColliders)
            if (TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Rope && Ladders.Contains((int)e.Number("TriggerObjectID")))
                bodies[(int)e.Number("TriggerObjectID")] = tc;
        uint hiddenLayer = 1u << FieldTriggers.HiddenGroup;

        // not in the tree: Step ticks the fader past the packets' 0.3 s fade, as the frames after them would
        void Step() => triggers.Fader?.Call(Node.MethodName._Process, 1.0);
        Climbables.Climb Probe(Climbables.Climb c) => c.IsRope ? climbs.FindRope(c.Base, 1) : climbs.Find(c.Base, 1);

        // the field entry's load: every ladder hidden and not climbable
        int n = triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Load(Ladders, false, false)));
        if (n != Ladders.Length) fails.Add($"load applied {n} of {Ladders.Length}");
        Step();
        int off = 0, hid = 0, drawn = 0, bodyOff = 0;
        foreach (int id in Ladders)
        {
            var c = byId[id];
            if (bodies.TryGetValue(id, out var tc) && tc.Body.CollisionLayer == hiddenLayer) bodyOff++;
            if (!c.Usable && Probe(c) == null) off++;
            if (triggers.MeshShown(id) is { } h) { drawn++; if (!h) hid++; }
        }
        // the ladders appear one event at a time
        int on = 0, shown = 0, bodyOn = 0;
        foreach (int id in Ladders)
        {
            triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Update(id, true, true)));
            Step();
            var c = byId[id];
            if (bodies.TryGetValue(id, out var tc) && tc.Body.CollisionLayer == 1u << tc.Group && tc.Group != FieldTriggers.HiddenGroup) bodyOn++;
            if (c.Usable && Probe(c) != null) on++;
            if (triggers.MeshShown(id) == true) shown++;
        }
        GD.Print($"trigger climb check: {Ladders.Length} ladders; after the load {off} not climbable, {hid} of {drawn} drawn hidden; " +
                 $"after their events {on} climbable, {shown} shown; {bodies.Count} with colliders, {bodyOff} off after the load, {bodyOn} back after the events");
        if (off != Ladders.Length) fails.Add($"after the load {Ladders.Length - off} of {Ladders.Length} still climbable");
        if (on != Ladders.Length) fails.Add($"after the events {Ladders.Length - on} of {Ladders.Length} not climbable");
        if (drawn == 0) fails.Add("no ladder has drawn instances");
        if (hid != drawn || shown != drawn) fails.Add($"drawn {drawn}: hidden {hid}, shown {shown}");
        if (bodies.Count == 0) fails.Add("no climbable object has a collider: the collider switch is not checked");
        if (bodyOff != bodies.Count || bodyOn != bodies.Count) fails.Add($"{bodies.Count} with colliders: {bodyOff} in the hidden group after the load, {bodyOn} back after the events");
    }
}
