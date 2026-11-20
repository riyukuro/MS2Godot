using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_fade.tscn) of a trigger mesh's timed fade on Dancing Sands (02000347), meshes
/// 6001-6010. TriggerObjectMesh::vfunc_2 (triggeractioncli.cpp:650-651) calls WorldService::FadeEntity with the event's
/// Fade times 0.1 seconds: a hide fades the mesh from its alpha to 0 (CEntityHider) and leaves it hidden, a show from 0
/// to 1 (CEntityShower), and a fade the other way starts from the running one's alpha (FUN_141bd8860 / FUN_141bd8ae0,
/// worldservice_1.cpp:8299-8388; TLinearInterpolator, alphaanimation.h:108-118). The fade node is ticked by hand with
/// fixed steps. The field's FieldTriggers hid and showed at once. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits
/// with 0 when it holds, else 1.
/// </summary>
public partial class TriggerFadeCheck : Node
{
    const int FieldId = 2000347;
    const int FirstId = 6001, LastId = 6010;
    const int Fade = 10;            // tenths: one second

    FieldTriggers triggers;
    Node fader;
    Node3D world;
    readonly List<int> ids = new();
    readonly List<string> fails = new();
    int frame;

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
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        world = fb.World;
        AddChild(world);
        // the field's construction (Field.LoadField), by reflection so the check also builds against the older FieldTriggers
        var ctor = typeof(FieldTriggers).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = new object[ctor.GetParameters().Length];
        args[0] = map.Entities;
        args[1] = fb.TriggerMeshDraws;
        if (args.Length > 2) args[2] = typeof(FieldBuilder).GetField("TriggerColliders")?.GetValue(fb);
        if (args.Length > 3) args[3] = typeof(FieldBuilder).GetField("TriggerFadeDraws")?.GetValue(fb);
        triggers = (FieldTriggers)ctor.Invoke(args);
        // not in the tree: the steps below tick it
        fader = typeof(FieldTriggers).GetField("Fader")?.GetValue(triggers) as Node;

        foreach (var e in map.Entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) != TriggerObjectKind.Mesh) continue;
            int id = (int)e.Number("TriggerObjectID");
            if (id >= FirstId && id <= LastId && triggers.MeshShown(id) == true && !ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count == 0) fails.Add($"no drawn trigger mesh {FirstId}-{LastId} on {fd.XBlock}");
    }

    static byte[] Packet(int id, bool show, int fade)
    {
        // TriggerObjects.Read: type 3 (a change, one event), then the mesh object's id, show, flag, fade, model name
        // (empty), skin scale
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

    void Send(bool show, int fade)
    {
        foreach (int id in ids) triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(id, show, fade)));
    }

    void Step(float seconds)
    {
        if (fader == null) return;
        ((Node)fader).Call(Node.MethodName._Process, (double)seconds);
    }

    /// <summary>The fading copies' alphas (the live ones).</summary>
    List<float> Alphas()
    {
        var list = new List<float>();
        void Walk(Node n)
        {
            for (int i = 0; i < n.GetChildCount(); i++)
            {
                var c = n.GetChild(i);
                if (c is MultiMeshInstance3D mi && mi.Name.ToString().StartsWith("trigger_fade") && !mi.IsQueuedForDeletion()
                    && mi.MaterialOverride is ShaderMaterial sm)
                    list.Add((float)sm.GetShaderParameter(ShaderParam.FadeAlpha));
                Walk(c);
            }
        }
        Walk(world);
        return list;
    }

    int Shown() => ids.Count(id => triggers.MeshShown(id) == true);

    public override void _Process(double delta)
    {
        if (triggers == null || fails.Count > 0) { Done(); return; }
        try
        {
            switch (frame++)
            {
                case 0: HideHalf(); break;
                case 1: HideEnd(); break;
                case 2: ShowAndReverse(); break;
                case 3: ReverseEnd(); break;
                default: Done(); return;
            }
        }
        catch (Exception e) { fails.Add(e.ToString()); Done(); }
    }

    float base1;   // the meshes' own material alpha, the hide's start

    void HideHalf()
    {
        Send(false, Fade);
        var start = Alphas();
        if (start.Count == 0) { fails.Add($"hide with fade {Fade}: no fading copy ({Shown()} of {ids.Count} meshes still shown)"); return; }
        base1 = start.Max();
        if (start.Any(a => Mathf.Abs(a - base1) > 1e-4f)) fails.Add("hide start: the copies' alphas differ");
        Step(0.5f);
        var half = Alphas();
        GD.Print($"trigger fade check: {ids.Count} meshes, {start.Count} copies; hide start {base1:F3}, after 0.5 s {(half.Count > 0 ? half[0] : -1):F3}");
        if (half.Count != start.Count || half.Any(a => Mathf.Abs(a - 0.5f * base1) > 0.01f)) fails.Add("hide after 0.5 s: alpha not half");
    }

    void HideEnd()
    {
        Step(0.6f);
        int left = Alphas().Count, shown = Shown();
        GD.Print($"trigger fade check: hide after 1.1 s: {left} copies, {shown} shown");
        if (left != 0 || shown != 0) fails.Add($"hide end: {left} copies left, {shown} meshes shown");
    }

    void ShowAndReverse()
    {
        Send(true, Fade);
        var start = Alphas();
        if (start.Count == 0 || start.Any(a => Mathf.Abs(a) > 1e-4f)) { fails.Add("show start: no copy at alpha 0"); return; }
        Step(0.3f);
        var mid = Alphas();
        if (mid.Count == 0 || mid.Any(a => Mathf.Abs(a - 0.3f * base1) > 0.01f)) fails.Add("show after 0.3 s: alpha not 0.3");
        Send(false, Fade);         // the other way: from the shower's alpha
        var turned = Alphas();
        Step(0.1f);
        var after = Alphas();
        GD.Print($"trigger fade check: show after 0.3 s {(mid.Count > 0 ? mid[0] : -1):F3}, hidden again: {(turned.Count > 0 ? turned[0] : -1):F3} then {(after.Count > 0 ? after[0] : -1):F3}");
        if (turned.Count != mid.Count || after.Count != mid.Count) fails.Add("reverse: copies lost");
        else if (after.Any(a => Mathf.Abs(a - 0.3f * 0.9f * base1) > 0.01f)) fails.Add("reverse after 0.1 s: alpha not 0.27");
    }

    void ReverseEnd()
    {
        Step(1.2f);
        int hidLeft = Alphas().Count, hidShown = Shown();
        Send(true, 0);
        Step(0.01f);
        int showLeft = Alphas().Count, shown = Shown();
        GD.Print($"trigger fade check: reverse end {hidLeft} copies, {hidShown} shown; show with fade 0: {showLeft} copies, {shown} shown");
        if (hidLeft != 0 || hidShown != 0) fails.Add($"reverse end: {hidLeft} copies left, {hidShown} meshes shown");
        if (showLeft != 0 || shown != ids.Count) fails.Add($"show with fade 0: {showLeft} copies left, {shown} of {ids.Count} shown");
    }

    void Done()
    {
        if (frame < 0) return;
        frame = -1000;
        GD.Print($"trigger fade check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
