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
/// Headless check (res://tests/trigger_actor.tscn) of the actor trigger objects, on three fields and the actors their server
/// trigger scripts set:
/// - 02000241, the quest doors 501-512 (trigger/02000241_bf/trigger_01_02.xml, trigger_01_03.xml, trigger_01_05.xml: shown
///   with Closed in the first state, Opened when their button is pressed). A KMS2 recording of the field has the field
///   entry's type 2 load with each door as 01 "Closed" and a type 3 event per door of 01 "Opened".
/// - 02000290, the doors 3000 / 3010 (trigger/02000290_bf/main.xml) and 3110 / 3120 (trigger/02000290_bf/main_2.xml):
///   shown Closed at first, then shown Opened, then hidden with Opened. The recording has the load with each as 01 "Closed",
///   then type 3 events of 01 "Opened" and 00 "Opened".
/// - 02020100, the actors 1401-1404, placed with IsVisible unset: drawn, hidden, until a packet shows them.
/// The client's reader takes the body as bool, string (TriggerObjectActor::vfunc_2 @0x140de74b0, triggeractioncli.cpp:718-719);
/// its apply step sets the entity's IsVisible flag to the bool (:720) and plays the named sequence (:721-723), which a name
/// the kfm lacks leaves as it was (Unk_AnimSvc::PlayAnimation, animationmanager.cpp:396-398).
/// What the player should see, per door: after the load, the door is drawn closed; after its event, it opens; after a
/// hide event, it is gone.
/// Events only: nothing runs per frame. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerActorCheck : Node
{
    static readonly int[] Doors241 = { 501, 502, 503, 504, 505, 506, 507, 508, 509, 510, 511, 512 };
    static readonly int[] Doors290 = { 3000, 3010, 3110, 3120 };
    static readonly int[] Hidden2020100 = { 1401, 1402, 1403, 1404 };

    readonly List<string> fails = new();

    public override void _Ready()
    {
        try
        {
            Check(2000241, Doors241, hides: false);
            Check(2000290, Doors290, hides: true);
            Placed(2020100, Hidden2020100);
        }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"trigger actor check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static void Write(BinaryWriter w, int id, bool on, string seq)
    {
        w.Write(id); w.Write(on);
        w.Write((ushort)seq.Length);
        foreach (char ch in seq) w.Write((ushort)ch);
    }

    static byte[] Load(IReadOnlyList<int> ids, bool on, string seq)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(ids.Count);
        foreach (int id in ids) Write(w, id, on, seq);
        return ms.ToArray();
    }

    static byte[] Update(int id, bool on, string seq)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Update);
        Write(w, id, on, seq);
        return ms.ToArray();
    }

    static int Apply(FieldTriggers t, byte[] body) => t.Apply(new Maple2.PacketLib.Tools.ByteReader(body));

    /// <summary>A field whose actor objects have IsVisible unset: each is drawn, hidden, before any packet.</summary>
    void Placed(int fieldId, int[] hidden)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, fieldId) ?? throw new InvalidOperationException($"no field {fieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var people = new Node3D { Name = $"Characters{fieldId}" };
        AddChild(people);
        var fnpcs = new FieldNpcs(packs, AssetIndex.For(packs), builder);
        fnpcs.PlaceActors(people, map);
        string tag = $"{fieldId:D8}";
        int n = 0;
        foreach (int id in hidden)
        {
            var a = fnpcs.TriggerActors.FirstOrDefault(x => (int)x.Entity.Number("TriggerObjectID") == id);
            if (a == null) fails.Add($"{tag} {id}: not drawn");
            else if (a.Entity.Bool("IsVisible", true)) fails.Add($"{tag} {id}: its IsVisible is set");
            else if (a.Root.Visible) fails.Add($"{tag} {id}: IsVisible unset, drawn shown before any packet");
            else n++;
        }
        GD.Print($"trigger actor check: {tag} {n} of {hidden.Length} actors with IsVisible unset drawn hidden");
    }

    void Check(int fieldId, int[] doors, bool hides)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, fieldId) ?? throw new InvalidOperationException($"no field {fieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var people = new Node3D { Name = $"Characters{fieldId}" };
        AddChild(people);
        var fnpcs = new FieldNpcs(packs, AssetIndex.For(packs), builder);
        fnpcs.PlaceActors(people, map);
        var triggers = new FieldTriggers(map.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        triggers.Bind(fnpcs.TriggerActors);
        string tag = $"{fieldId:D8}";

        // the scripts' ids are actor objects of the field, each drawn with a kfm that has both sequences
        foreach (int id in doors)
        {
            if (triggers.KindOf(id) != TriggerObjectKind.Actor) fails.Add($"{tag} {id}: kind {triggers.KindOf(id)?.ToString() ?? "none"}, not an actor");
            else if (triggers.Actor(id) == null) fails.Add($"{tag} {id}: not drawn");
        }
        if (fails.Count > 0) return;

        var player = new Dictionary<int, AnimationPlayer>();
        foreach (int id in doors)
            player[id] = ((FieldNpcs.TriggerActor)triggers.Actor(id)).Root.GetNode<AnimationPlayer>("Body/Model/AnimationPlayer");
        bool Is(int id, bool visible, string seq)
        {
            var a = (FieldNpcs.TriggerActor)triggers.Actor(id);
            return a.Root.Visible == visible && a.Sequence == seq && player[id].IsPlaying()
                   && player[id].CurrentAnimation == seq;
        }

        // before any packet: every trigger actor of the field is drawn as its entity's IsVisible flag says (unset is drawn,
        // hidden)
        int asPlaced = 0, placedHidden = 0;
        foreach (var a in fnpcs.TriggerActors)
        {
            bool want = a.Entity.Bool("IsVisible", true);
            if (!want) placedHidden++;
            if (a.Root.Visible == want) asPlaced++;
            else fails.Add($"{tag} {a.Entity.Number("TriggerObjectID")}: drawn {a.Root.Visible} before any packet, its IsVisible is {want}");
        }

        // the field entry's load: every door shown, closed
        int n = Apply(triggers, Load(doors, true, "Closed"));
        if (n != doors.Length) fails.Add($"{tag}: load applied {n} of {doors.Length}");
        int closed = doors.Count(id => Is(id, true, "Closed"));

        // a name the kfm lacks: the door keeps its sequence, and the bool still applies
        Apply(triggers, Update(doors[0], true, "NoSuchSequence"));
        bool kept = Is(doors[0], true, "Closed");

        // the doors open one event at a time, then (02000290) hide
        int opened = 0, hidden = 0;
        foreach (int id in doors)
        {
            Apply(triggers, Update(id, true, "Opened"));
            if (Is(id, true, "Opened")) opened++;
            if (!hides) continue;
            Apply(triggers, Update(id, false, "Opened"));
            var a = (FieldNpcs.TriggerActor)triggers.Actor(id);
            if (!a.Root.Visible && a.Sequence == "Opened") hidden++;
        }

        // the same name again (show, hide, show "Opened") once it has finished: not started over; a clamp sequence stays
        // stopped at its end, a looping one where it was
        int stayed = 0;
        foreach (int id in doors)
        {
            var anim = player[id];
            bool loops = anim.GetAnimation("Opened").LoopMode != Animation.LoopModeEnum.None;
            anim.Advance(anim.CurrentAnimationLength + 1);
            double at = loops ? anim.CurrentAnimationPosition : -1;
            Apply(triggers, Update(id, false, "Opened"));
            Apply(triggers, Update(id, true, "Opened"));
            var a = (FieldNpcs.TriggerActor)triggers.Actor(id);
            bool still = loops ? anim.IsPlaying() && Math.Abs(anim.CurrentAnimationPosition - at) < 1e-6 : !anim.IsPlaying();
            if (a.Root.Visible && a.Sequence == "Opened" && anim.AssignedAnimation == "Opened" && still) stayed++;
            else fails.Add($"{tag} {id}: \"Opened\" again (loops {loops}) playing {anim.IsPlaying()} at " +
                           $"{(anim.IsPlaying() ? anim.CurrentAnimationPosition : -1):0.###}, was {at:0.###}");
        }
        GD.Print($"trigger actor check: {tag} {doors.Length} doors; after the load {closed} shown closed; unknown name kept {kept}; " +
                 $"after their events {opened} opened{(hides ? $", {hidden} hidden" : "")}; {asPlaced} of {fnpcs.TriggerActors.Count} actors drawn as placed, {placedHidden} hidden; {stayed} kept at the end on the same name");
        if (closed != doors.Length) fails.Add($"{tag}: after the load {doors.Length - closed} not shown closed");
        if (!kept) fails.Add($"{tag} {doors[0]}: an unknown sequence name changed it");
        if (opened != doors.Length) fails.Add($"{tag}: after the events {doors.Length - opened} not shown opened");
        if (hides && hidden != doors.Length) fails.Add($"{tag}: after the hide events {doors.Length - hidden} still drawn");
    }
}
