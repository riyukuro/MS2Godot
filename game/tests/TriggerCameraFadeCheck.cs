using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_camera_fade.tscn) of a trigger mesh the camera fade holds (world/FieldFade) on
/// 02000335_bf, whose trigger meshes are also Transparency targets. A Trigger packet's hide (TriggerObjectMesh::vfunc_2,
/// triggeractioncli.cpp:650-651, through world/TriggerFade) sets the mesh's instances itself, so the camera fade's copy of
/// each goes: a hidden mesh keeps no faded copy, and a show draws the mesh once, from its own instances. Both are checked
/// with the hide and show at once (Fade 0) and with a timed hide. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0
/// when it holds, else 1.
/// </summary>
public partial class TriggerCameraFadeCheck : Node
{
    const string XBlock = "02000335_bf";

    FieldTriggers triggers;
    FieldFade fade;
    Node3D world;
    readonly List<(int Id, FieldEntity Entity)> meshes = new();
    readonly List<string> fails = new();
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
        var map = FieldMap.Load(packs, new FlatLibrary(packs), XBlock);
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        world = fb.World;
        AddChild(world);
        triggers = new FieldTriggers(map.Entities, fb.TriggerMeshDraws, fb.TriggerColliders, fb.TriggerFadeDraws);
        // in the tree, as Field has it (its switch subscription); no camera, so its own test never runs
        fade = new FieldFade(fb.FadeDraws, () => null, () => null);
        AddChild(fade);
        foreach (var e in map.Entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) != TriggerObjectKind.Mesh || !fb.FadeDraws.ContainsKey(e)) continue;
            int id = (int)e.Number("TriggerObjectID");
            if (triggers.MeshShown(id) == true && meshes.All(m => m.Id != id)) meshes.Add((id, e));
        }
        GD.Print($"trigger camera fade check: {XBlock} {meshes.Count} shown trigger meshes the camera fades");
        if (meshes.Count == 0) fails.Add($"no shown trigger mesh the camera fades on {XBlock}");
    }

    static byte[] Packet(int id, bool show, int fade)
    {
        // TriggerObjects.Read: type 3 (one event), the mesh's id, show, flag, fade (tenths), model name (empty), skin scale
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Update);
        w.Write(id);
        w.Write(show);
        w.Write((byte)0);
        w.Write(fade);
        w.Write((short)0);
        w.Write(1f);
        return ms.ToArray();
    }

    void Send(int id, bool show, int fadeTenths) => triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(id, show, fadeTenths)));

    /// <summary>Runs the trigger fades to their end (TriggerFade ticked by hand, as TriggerFadeCheck does).</summary>
    void Finish()
    {
        if (triggers.Fader != null) for (int i = 0; i < 40; i++) triggers.Fader.Call(Node.MethodName._Process, 0.1);
    }

    /// <summary>The camera fade's live copies (FieldFade names them "fade"; a trigger fade's are TriggerFade.CopyName).</summary>
    int CameraCopies()
    {
        int n = 0;
        void Walk(Node node)
        {
            for (int i = 0; i < node.GetChildCount(); i++)
            {
                var c = node.GetChild(i);
                if (c is MultiMeshInstance3D mi && !mi.IsQueuedForDeletion() && mi.Name.ToString().Contains("fade")
                    && !mi.Name.ToString().Contains(TriggerFade.CopyName)) n++;
                Walk(c);
            }
        }
        Walk(world);
        return n;
    }

    void Run()
    {
        foreach (var (id, e) in meshes)
        {
            // at once: a hide, then a show
            fade.FadeOutNow(e);
            if (fade.Fading(e).Count == 0) { fails.Add($"{id}: the camera fade did not take it"); continue; }
            Send(id, false, 0);
            Finish();
            if (triggers.MeshShown(id) != false) fails.Add($"{id}: the hide at once left it shown");
            if (fade.Fading(e).Count > 0) fails.Add($"{id}: hidden at once, the camera fade still holds {fade.Fading(e).Count} faded copies (a ghost)");
            Send(id, true, 0);
            Finish();
            if (triggers.MeshShown(id) != true) fails.Add($"{id}: the show at once left it hidden");
            if (fade.Fading(e).Count > 0) fails.Add($"{id}: shown again, the camera fade still holds {fade.Fading(e).Count} copies beside its own instances (drawn twice)");
            // a timed hide of a faded mesh: the trigger fade's copy draws it, not the camera fade's too
            fade.FadeOutNow(e);
            Send(id, false, 10);
            if (fade.Fading(e).Count > 0) fails.Add($"{id}: a timed hide started and the camera fade still holds {fade.Fading(e).Count} copies (drawn twice while it fades)");
            Finish();
            Send(id, true, 0);
            Finish();
        }
        int left = CameraCopies();
        if (left > 0) fails.Add($"{left} camera fade copies are left in the field after every mesh was shown again");
    }

    public override void _Process(double delta)
    {
        if (done) return;
        done = true;
        if (fails.Count == 0)
            try { Run(); }
            catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"trigger camera fade check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
