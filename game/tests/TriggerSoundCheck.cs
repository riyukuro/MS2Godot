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
/// Headless check (res://tests/trigger_sound.tscn) of the trigger sound event (TriggerObjectSound::vfunc_2,
/// triggeractioncli.cpp:555-613) on two fields:
/// - 83000002, whose six MS2TriggerSound objects are of kind AMB: the field load gives none of them an ambient sound
///   (worldservice_1.cpp:10336-10350 skips trigger objects) while its other sound entities get theirs; an on event creates
///   and plays the object's sound, an off event stops it and an on event plays it again (CSoundEventMgr::FUN_14177ce30,
///   soundeventmgr.cpp:1412-1460); an off event for an object without a sound creates none.
/// - 02020051, whose objects 60001 and 60002 are of kind BGM with cues 172 and 166 (soundEventID): an on event plays the
///   cue and keeps it as the handle, an off event goes back to the previous cue and clears it (PlayMusic, soundeventmgr.cpp:
///   1151-1203), and the music played follows the current cue.
/// Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerSoundCheck : Node
{
    const int AmbField = 83000002, BgmField = 2020051;
    static readonly int[] AmbOn = { 11001, 13000 };
    const int AmbUntouched = 12001, BgmA = 60001, BgmB = 60002;

    PackSet packs;
    FieldAudio audio;
    FieldTriggers amb, bgm;
    readonly Dictionary<int, FieldEntity> ambObjects = new();
    readonly List<string> fails = new();
    readonly List<string> notes = new();
    int frame, step;
    bool done;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    FieldMap Map(int field)
    {
        var fd = FieldData.FindById(packs, field) ?? throw new InvalidOperationException($"no field {field}");
        return FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
    }

    void Build()
    {
        packs = PackSet.Shared(Settings.DataDir);
        audio = new FieldAudio();
        AddChild(audio);
        if (!audio.Open(Settings.DataDir, packs)) { fails.Add("no sound bank"); return; }

        var map = Map(AmbField);
        audio.Ambient = new AmbientSounds(audio.Library);
        audio.AddChild(audio.Ambient);
        audio.Ambient.Build(map);
        amb = new FieldTriggers(map.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        amb.Bind(audio);
        foreach (var e in map.Entities)
            if (TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Sound) ambObjects[(int)e.Number("TriggerObjectID")] = e;
        foreach (int id in AmbOn.Append(AmbUntouched))
            if (!ambObjects.TryGetValue(id, out var e)) fails.Add($"no trigger sound {id} on {AmbField}");
            else if (e.Get("SoundType") != "AMB") fails.Add($"trigger sound {id} is not AMB");
        int loaded = ambObjects.Values.Count(e => audio.Ambient.PlayerOf(e) != null);
        int others = map.Entities.Count(e => e.HasMixin("MS2Sound") && !e.HasMixin("MS2TriggerObject") && audio.Ambient.PlayerOf(e) != null);
        if (loaded != 0) fails.Add($"load: {loaded} of {ambObjects.Count} trigger sounds got an ambient sound");
        if (others == 0) fails.Add("load: no other sound entity got an ambient sound");
        notes.Add($"{ambObjects.Count} trigger sounds, {loaded} at load, {others} other ambient sounds");

        var bmap = Map(BgmField);
        bgm = new FieldTriggers(bmap.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        bgm.Bind(audio);
        foreach (int id in new[] { BgmA, BgmB })
            if (bgm.KindOf(id) != TriggerObjectKind.Sound) fails.Add($"no trigger sound {id} on {BgmField}");
    }

    /// <summary>A Trigger packet of type 2 with one sound event (id, on byte) per id.</summary>
    static Maple2.PacketLib.Tools.ByteReader Packet(IEnumerable<int> ids, bool on)
    {
        var list = ids.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(list.Count);
        foreach (int id in list) { w.Write(id); w.Write((byte)(on ? 1 : 0)); }
        return new Maple2.PacketLib.Tools.ByteReader(ms.ToArray());
    }

    AudioStreamPlayer3D Player(int id) => audio.Ambient.PlayerOf(ambObjects[id]);

    public override void _Process(double delta)
    {
        if (done) return;
        if (amb == null || fails.Count > 0) { Done(); return; }
        frame++;
        try
        {
            switch (step)
            {
                case 0: Step0(); step++; frame = 0; break;
                case 1:   // the clips decode on a worker thread and start deferred
                    if (AmbOn.All(id => Player(id)?.Stream != null) || frame > 600) { Step1(); step++; }
                    break;
                default: Bgm(); Done(); break;
            }
        }
        catch (Exception e) { fails.Add(e.ToString()); Done(); }
    }

    void Step0()
    {
        int applied = amb.Apply(Packet(AmbOn, true));
        if (applied != AmbOn.Length) fails.Add($"amb on: {applied} of {AmbOn.Length} events applied");
        int created = AmbOn.Count(id => Player(id) != null && audio.Ambient.SoundOn(ambObjects[id]) == true);
        if (created != AmbOn.Length) fails.Add($"amb on: {AmbOn.Length - created} of {AmbOn.Length} sounds not created on");
        amb.Apply(Packet(new[] { AmbUntouched }, false));
        if (Player(AmbUntouched) != null) fails.Add("amb off: a sound was created for an object without one");
        notes.Add($"on {applied} events, {created} created");
    }

    void Step1()
    {
        int playing = AmbOn.Count(id => Player(id) is { Playing: true });
        if (playing != AmbOn.Length) fails.Add($"amb on: {AmbOn.Length - playing} of {AmbOn.Length} sounds not playing once decoded");
        int first = AmbOn[0], second = AmbOn[1];
        amb.Apply(Packet(new[] { first }, false));
        bool stopped = !Player(first).Playing && audio.Ambient.SoundOn(ambObjects[first]) == false;
        bool kept = Player(second).Playing;
        if (!stopped) fails.Add($"amb off: {first} still playing");
        if (!kept) fails.Add($"amb off: {second}, not named, stopped");
        amb.Apply(Packet(new[] { first }, true));
        bool again = Player(first).Playing && audio.Ambient.SoundOn(ambObjects[first]) == true;
        if (!again) fails.Add($"amb on again: {first} not playing");
        notes.Add($"{playing} playing; off: stopped {stopped}, other kept {kept}; on again {again}");
    }

    void Bgm()
    {
        bgm.Apply(Packet(new[] { BgmA }, true));
        int a = audio.MusicState.CurrentCueId;
        if (a != 172 || bgm.SoundHandle(BgmA) != 172) fails.Add($"bgm on {BgmA}: cue {a}, handle {bgm.SoundHandle(BgmA)} (want 172)");
        bgm.Apply(Packet(new[] { BgmB }, true));
        int b = audio.MusicState.CurrentCueId;
        if (b != 166 || bgm.SoundHandle(BgmB) != 166) fails.Add($"bgm on {BgmB}: cue {b}, handle {bgm.SoundHandle(BgmB)} (want 166)");
        if (audio.BgmCue != 166) fails.Add($"bgm on {BgmB}: playing cue {audio.BgmCue}, not 166");
        bgm.Apply(Packet(new[] { BgmB }, false));
        int back = audio.MusicState.CurrentCueId;
        if (back != 172 || bgm.SoundHandle(BgmB) != 0) fails.Add($"bgm off {BgmB}: cue {back}, handle {bgm.SoundHandle(BgmB)} (want 172, 0)");
        if (audio.BgmCue != 172) fails.Add($"bgm off {BgmB}: playing cue {audio.BgmCue}, not 172");
        notes.Add($"bgm on {BgmA} -> {a}, on {BgmB} -> {b}, off {BgmB} -> {back} (playing {audio.BgmCue})");
    }

    void Done()
    {
        if (done) return;
        done = true;
        GD.Print($"trigger sound check: {string.Join("; ", notes)}");
        GD.Print($"trigger sound check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
