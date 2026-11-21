using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/reactor_layer.tscn, game data) of the field's reactor models (CReactor, reactor.cpp):
/// - a reactor whose state has a start animation is added: its kfm model from the effect pack stands at the reactor's
///   place (client (x, y, z) at Godot (x, z, -y)) and plays the state's idle animation;
/// - a state update queues start then idle: the start plays, its end pops it and the idle plays; the kf's sequence cycle
///   type (NiSequenceData Cycle Type) is the animation's loop mode, so a clamped start plays once;
/// - a frame with no change allocates 0 B;
/// - a reactor with a connected effect keeps it while the own character is in its player set, drops it when the set
///   loses the character, and drops it when the reactor goes; the removed reactor's model goes.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class ReactorLayerCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const long Me = 777;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static bool HasMesh(Node n)
    {
        if (n is MeshInstance3D) return true;
        for (int i = 0; i < n.GetChildCount(); i++)
            if (HasMesh(n.GetChild(i))) return true;
        return false;
    }

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var fx = new RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new ReactorLayer(packs, builder, () => Me);
        field.AddChild(layer);
        var kept = (IDictionary)typeof(RegionSkills).GetField("kept", Private).GetValue(fx);

        var table = new ReactorTable(packs);
        var rows = (IDictionary)typeof(ReactorTable).GetField("reactors", Private).GetValue(table);
        // a reactor with a model whose some state has a start and an idle animation in its kfm; one with a connected effect
        ReactorTable.Reactor anim = null, effect = null; int animState = -1;
        foreach (ReactorTable.Reactor r in rows.Values)
        {
            if (effect == null && r.ConnectedEffect != "" && fx.Has(r.ConnectedEffect) && r.States.Count > 0) effect = r;
            if (anim != null || r.States.Count == 0) continue;
            string kfmEntry = packs.FindByFileName(ReactorLayer.EffectArchive, r.States[0].KfmName.ToLowerInvariant() + ".kfm");
            if (kfmEntry == null) continue;
            var kfm = KfmFile.Load(packs.Read(ReactorLayer.EffectArchive, kfmEntry));
            for (int s = 0; s < r.States.Count; s++)
                if (r.States[s].StartAni != "" && kfm.ByName(r.States[s].StartAni) != null && kfm.ByName(r.States[s].IdleAni) != null
                    && r.States[s].KfmName == r.States[0].KfmName && r.States[s].StartAni != r.States[s].IdleAni)
                { anim = r; animState = s; break; }
        }
        Check(anim != null, "no reactor with a start and an idle animation in the data");
        Check(effect != null, "no reactor with a connected effect in the data");
        var all = new FieldReactors(table);

        if (anim != null)
        {
            var st = anim.States[animState];
            var pos = new System.Numerics.Vector3(100, 200, 300);
            var add = new ReactorAdd(11, "anim", anim.Id, pos, new System.Numerics.Vector3(0, 0, 90), new ReactorState(animState, 0, new List<long>(), 0));
            var r = all.Add(add, out _);
            layer.Add(r);
            var root = layer.RootOf(11);
            Check(root != null && HasMesh(root), $"reactor {anim.Id} ('{st.KfmName}') has no model");
            Check(root != null && root.Position.IsEqualApprox(new Vector3(100, 300, -200)), $"the model stands at {root?.Position}, want (100, 300, -200)");
            // EulerXyz(0, 0, 90) turns client +x to -y: Godot (0, 0, 1)
            Check(root != null && root.Basis * Vector3.Right is var tx && tx.IsEqualApprox(new Vector3(0, 0, 1)), $"the model's x axis is {root?.Basis * Vector3.Right}, want (0, 0, 1)");
            Check(layer.PlayingOf(11) == st.IdleAni, $"the added reactor plays '{layer.PlayingOf(11)}', want the idle '{st.IdleAni}'");
            var player = layer.PlayerOf(11);
            Check(player != null && player.IsPlaying(), "the idle animation is not playing");

            all.Update(11, new ReactorState(animState, 0, new List<long>(), 0));
            layer._Process(0);
            Check(layer.PlayingOf(11) == st.StartAni && player != null && player.IsPlaying(), $"the update plays '{layer.PlayingOf(11)}', want the start '{st.StartAni}'");
            // the start's loop mode is its kf's sequence cycle type
            var kfmEntry = packs.FindByFileName(ReactorLayer.EffectArchive, st.KfmName.ToLowerInvariant() + ".kfm");
            var kfm = KfmFile.Load(packs.Read(ReactorLayer.EffectArchive, kfmEntry));
            var seq = kfm.ByName(st.StartAni);
            string kfEntry = kfmEntry[..kfmEntry.LastIndexOf('/')] + "/" + System.IO.Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant();
            var kf = KfAnimation.Load(NifFile.Load(builder.Spec, packs.Read(ReactorLayer.EffectArchive, kfEntry)));
            var want = kf.Cycle switch { 1 => Animation.LoopModeEnum.Pingpong, 2 => Animation.LoopModeEnum.None, _ => Animation.LoopModeEnum.Linear };
            var playing = player?.GetAnimation(player.CurrentAnimation);
            Check(playing != null && playing.LoopMode == want, $"the start's loop mode {playing?.LoopMode}, want {want} (cycle {kf.Cycle})");
            // the sequence block's own field, read apart from KfAnimation: a clamped start plays once
            var raw = NifFile.Load(builder.Spec, packs.Read(ReactorLayer.EffectArchive, kfEntry)).Blocks.Find(b => b.Type == "NiSequenceData");
            Check(raw != null && raw.IntOr("Cycle Type", -1) == kf.Cycle && (kf.Cycle != 2 || playing?.LoopMode == Animation.LoopModeEnum.None),
                $"the start's cycle {kf.Cycle} is not its sequence's Cycle Type {raw?.IntOr("Cycle Type", -1)}, or a clamped start loops");
            GD.Print($"reactor {anim.Id} state {animState}: '{st.KfmName}' start '{st.StartAni}' cycle {kf.Cycle}, idle '{st.IdleAni}'");

            // the start's end pops it; the idle plays next
            player?.EmitSignal(AnimationMixer.SignalName.AnimationFinished, player.CurrentAnimation);
            Check(r.Head == st.IdleAni, $"the start's end left the head '{r.Head}'");
            layer._Process(0);
            Check(layer.PlayingOf(11) == st.IdleAni, $"after the start the reactor plays '{layer.PlayingOf(11)}', want '{st.IdleAni}'");

            // frames with no change: 0 B
            layer._Process(0);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) layer._Process(0);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, $"10 reactor frames allocated {bytes} B");

            all.Remove(11);
            layer.Remove(r);
            Check(layer.RootOf(11) == null && root != null && root.IsQueuedForDeletion(), "the removed reactor's model stayed");
        }

        if (effect != null)
        {
            var with = new ReactorState(0, 0, new List<long> { Me }, 0);
            var r = all.Add(new ReactorAdd(12, "fx", effect.Id, new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(0, 0, 0), with), out _);
            layer.Add(r);
            int h = layer.EffectOf(12);
            Check(h != 0 && kept.Contains(h), $"the own character in the set kept no '{effect.ConnectedEffect}' (handle {h})");
            all.Update(12, new ReactorState(0, 0, new List<long> { Me + 1 }, 0));
            layer._Process(0);
            Check(layer.EffectOf(12) == 0 && !kept.Contains(h), "the set without the own character left the effect");
            all.Update(12, with);
            layer._Process(0);
            int h2 = layer.EffectOf(12);
            Check(h2 != 0 && kept.Contains(h2), "the own character back in the set kept no effect");
            layer.Remove(r);
            Check(!kept.Contains(h2), "the removed reactor's effect stayed");
        }

        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "reactor layer: ok" : $"reactor layer: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
