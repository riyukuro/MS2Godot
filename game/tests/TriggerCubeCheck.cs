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
/// Headless check (res://tests/trigger_cube.tscn) of the trigger cube event on 02000328, whose MS2TriggerCube objects
/// 5001-5004 and 5100-5115 are object weapon cubes that its trigger scripts hide and show (SetCube,
/// trigger/02000328_bf/main.xml, level_01_00.xml .. level_01_15.xml). TriggerObjectCube::vfunc_2
/// (triggeractioncli.cpp:818-822) reads one bool and sets the entity's IsVisible to it: a hide leaves the cube undrawn
/// (its instances get a zero basis), a show draws it again, a cube the event does not name stays as it was, and the
/// cube's colliders are not switched (a ray down each hidden cube meets what it met before).
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerCubeCheck : Node
{
    const int FieldId = 2000328;
    static readonly int[] Hidden = { 5100, 5101, 5102, 5103, 5001, 5002 };

    FieldBuilder fb;
    FieldTriggers triggers;
    readonly Dictionary<int, FieldEntity> cubes = new();
    readonly List<string> fails = new();
    int frame;
    bool done;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, FieldId) ?? throw new InvalidOperationException($"no field {FieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        AddChild(fb.World);
        triggers = new FieldTriggers(map.Entities, fb.TriggerMeshDraws, fb.TriggerColliders, fb.TriggerFadeDraws);
        foreach (var e in map.Entities)
            if (TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Cube) cubes[(int)e.Number("TriggerObjectID")] = e;
        foreach (int id in Hidden)
            if (!cubes.ContainsKey(id)) fails.Add($"no trigger cube {id} on {fd.XBlock}");
    }

    /// <summary>A Trigger packet of type 2 with one cube event (id, bool on) per id.</summary>
    static byte[] Packet(IEnumerable<int> ids, bool on)
    {
        var list = ids.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(list.Count);
        foreach (int id in list) { w.Write(id); w.Write(on); }
        return ms.ToArray();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (triggers == null || fails.Count > 0) { Done(); return; }
        if (++frame < 3) return;                     // the bodies are in the space
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        Done();
    }

    List<(MultiMesh Mm, int Index)> Draws(int id) =>
        fb.TriggerMeshDraws.TryGetValue(cubes[id], out var d) ? d : new List<(MultiMesh, int)>();

    void Check()
    {
        var space = GetViewport().World3D.DirectSpaceState;
        var ray = new PhysicsRayQueryParameters3D { CollisionMask = MoveConfig.PcGroups };
        var world = fb.World.GlobalTransform;
        Vector3? Hit(int id)
        {
            var at = world * ModelBuilder.ToGodot(cubes[id].Transform).Origin;
            ray.From = at + new Vector3(0, 300, 0);
            ray.To = at - new Vector3(0, 300, 0);
            var hit = space.IntersectRay(ray);
            return hit.Count > 0 ? (Vector3)hit["position"] : null;
        }

        // the headless renderer keeps no MultiMesh instance transforms, so the drawn state is read as the trigger's
        // shown flag, which TriggerFade.Switch sets with the instances' basis
        int drawn = cubes.Keys.Count(id => Draws(id).Count > 0);
        if (drawn != cubes.Count) fails.Add($"{cubes.Count - drawn} of {cubes.Count} trigger cubes have no draws");
        int shownAtLoad = cubes.Keys.Count(id => triggers.MeshShown(id) == true);
        if (shownAtLoad != cubes.Count) fails.Add($"load: {cubes.Count - shownAtLoad} of {cubes.Count} cubes not bound shown");
        var before = Hidden.ToDictionary(id => id, Hit);
        if (Hidden.Any(id => before[id] == null)) fails.Add("a named cube has no collider under its origin");

        int applied = triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(Hidden, false)));
        if (applied != Hidden.Length) fails.Add($"hide: {applied} of {Hidden.Length} events applied");
        int hidden = Hidden.Count(id => triggers.MeshShown(id) == false);
        int others = cubes.Keys.Count(id => !Hidden.Contains(id) && triggers.MeshShown(id) == true);
        int kept = 0;
        foreach (int id in Hidden)
            if (Hit(id) is { } h && before[id] is { } b && h.DistanceTo(b) < 0.01f) kept++;
        if (hidden != Hidden.Length) fails.Add($"hide: {Hidden.Length - hidden} of {Hidden.Length} cubes still drawn");
        if (others != cubes.Count - Hidden.Length) fails.Add($"hide: {cubes.Count - Hidden.Length - others} unnamed cubes changed");
        if (kept != Hidden.Length) fails.Add($"hide: {Hidden.Length - kept} of {Hidden.Length} cubes lost their collider");

        triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(Hidden, true)));
        int back = Hidden.Count(id => triggers.MeshShown(id) == true);
        if (back != Hidden.Length) fails.Add($"show: {Hidden.Length - back} of {Hidden.Length} cubes not drawn again");

        GD.Print($"trigger cube check: {cubes.Count} cubes, {drawn} drawn; hide {applied} events: {hidden} hidden, {others} others unchanged, " +
                 $"{kept} colliders kept; show: {back} back");
    }

    void Done()
    {
        if (done) return;
        done = true;
        GD.Print($"trigger cube check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
