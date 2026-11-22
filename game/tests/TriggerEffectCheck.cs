using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_effect.tscn) of the effect trigger objects, on 02000252 and the effects its server
/// trigger scripts set: 8031-8034 (trigger/02000252_bf/door_01.xml and door_02.xml: on in the first state, off when the
/// door opens) and 8903 (trigger/02000252_bf/open_door.xml: off at first, on with the guide line, off at the end).
/// The packets are the recorded ones: a KMS2 recording of the field has the field entry's type 2 load with 8031-8034 as
/// 01 00 03000000 and 8903 as 00 00 03000000, then type 3 events of 00 00 03000000 for 8031-8034 and 01 00 03000000, then
/// 00 00 03000000, for 8903. The client's reader takes that as bool on, u8, i32 (TriggerObjectEffect::vfunc_2 @0x140de7a00,
/// triggeractioncli.cpp:787-789). Its apply step removes the effect it started before (:790-791) and, when on, starts the
/// entity's XmlFilePath effect at the entity's position, scale and z rotation (:792-807).
/// What the player should see, per effect: on, the effect plays at the entity; off, it is gone.
/// Events only: nothing runs per frame. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerEffectCheck : Node
{
    const int FieldId = 2000252;
    static readonly int[] Doors = { 8031, 8032, 8033, 8034 };
    const int Guide = 8903;

    readonly List<string> fails = new();

    /// <summary>The effect service the triggers call, over the field's: which handles live, and what each started with.</summary>
    sealed class Recorder : IRegionSkillEffects
    {
        public readonly RegionSkills Inner;
        public readonly Dictionary<int, (string Effect, Xform At)> Live = new();
        public Recorder(RegionSkills inner) => Inner = inner;
        public bool Has(string effect) => Inner.Has(effect);
        public int Spawn(string effect, Xform at, bool always, int randomIndex)
        {
            int h = Inner.Spawn(effect, at, always, randomIndex);
            if (h != 0) Live[h] = (effect, at);
            return h;
        }
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Inner.Insert(effect, at, always, randomIndex);
        public void FadeOut(int handle, float seconds) { Live.Remove(handle); Inner.FadeOut(handle, seconds); }
    }

    public override void _Ready()
    {
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"trigger effect check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static void Write(BinaryWriter w, int id, bool on) { w.Write(id); w.Write(on); w.Write((byte)0); w.Write(3); }

    /// <summary>The field entry's type 2 load of the five effects, as recorded.</summary>
    static byte[] Load()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(Doors.Length + 1);
        foreach (int id in Doors) Write(w, id, true);
        Write(w, Guide, false);
        return ms.ToArray();
    }

    static byte[] Update(int id, bool on)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Update);
        Write(w, id, on);
        return ms.ToArray();
    }

    void Check()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, FieldId) ?? throw new InvalidOperationException($"no field {FieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var triggers = new FieldTriggers(map.Entities, new Dictionary<FieldEntity, List<(MultiMesh, int)>>());
        var service = new RegionSkills(packs, builder);
        AddChild(service);
        var fx = new Recorder(service);
        triggers.Bind(fx);

        // the scripts' ids are effect objects of the field, each naming an effect the client has
        var entity = new Dictionary<int, FieldEntity>();
        foreach (var e in map.Entities)
            if (TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Effect) entity[(int)e.Number("TriggerObjectID")] = e;
        var ids = new List<int>(Doors) { Guide };
        foreach (int id in ids)
        {
            if (triggers.KindOf(id) != TriggerObjectKind.Effect) fails.Add($"{id}: kind {triggers.KindOf(id)?.ToString() ?? "none"}, not an effect");
            else if (!fx.Has(entity[id].Get("XmlFilePath") ?? "")) fails.Add($"{id}: no effect {entity[id].Get("XmlFilePath")}");
        }
        if (fails.Count > 0) return;

        bool Plays(int id) => triggers.EffectHandle(id) is var h and not 0 && fx.Live.ContainsKey(h);

        // the field entry's load: the door effects play where their entities are, the guide's does not
        int n = triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Load()));
        if (n != ids.Count) fails.Add($"load applied {n} of {ids.Count}");
        int spawned = service.Spawned, rotated = 0;
        foreach (int id in Doors)
        {
            if (!Plays(id)) { fails.Add($"{id}: not playing after the load"); continue; }
            var (name, at) = fx.Live[triggers.EffectHandle(id)];
            var e = entity[id];
            if (name != e.Get("XmlFilePath")) fails.Add($"{id}: started {name}, not its XmlFilePath");
            var p = e.Position;
            if (Math.Abs(at.T.X - p.X) > 1e-3 || Math.Abs(at.T.Y - p.Y) > 1e-3 || Math.Abs(at.T.Z - p.Z) > 1e-3) fails.Add($"{id}: at {at.T}, not {p}");
            if (Math.Abs(at.S - e.Number("Scale", 1)) > 1e-6) fails.Add($"{id}: scale {at.S}");
            // MakeZRotation of the raw Rotation.z, taken as radians as the client passes it (degrees, unconverted)
            double z = (float)e.RotationDeg.Z, c = Math.Cos(z), sn = Math.Sin(z);
            double[] want = { c, sn, 0, -sn, c, 0, 0, 0, 1 };
            if (z != 0) rotated++;
            for (int i = 0; i < 9; i++)
                if (Math.Abs(at.R[i] - want[i]) > 1e-5) { fails.Add($"{id}: rotation z {z}: [{string.Join(", ", at.R)}], want [{string.Join(", ", want)}]"); break; }
        }
        if (rotated == 0) fails.Add("no door effect with a nonzero Rotation.z");
        if (Plays(Guide)) fails.Add($"{Guide}: playing after its off load");
        if (spawned == 0) fails.Add("the door effects drew no nif part");

        // the guide line: 8903 on, then off
        triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Update(Guide, true)));
        bool guideOn = Plays(Guide);
        int guideHandle = triggers.EffectHandle(Guide);
        triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Update(Guide, false)));
        bool guideOff = !Plays(Guide);

        // the doors open: their effects go
        int off = 0;
        foreach (int id in Doors)
        {
            triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Update(id, false)));
            if (!Plays(id)) off++;
        }
        GD.Print($"trigger effect check: {Doors.Length} door effects playing after the load ({rotated} turned), {spawned} nif parts; " +
                 $"guide on {guideOn} (handle {guideHandle}), off {guideOff}; after the doors open {off} of {Doors.Length} gone; " +
                 $"{fx.Live.Count} still live");
        if (!guideOn) fails.Add($"{Guide}: not playing after its on event");
        if (!guideOff) fails.Add($"{Guide}: still playing after its off event");
        if (off != Doors.Length) fails.Add($"after the doors open {Doors.Length - off} still play");
        if (fx.Live.Count != 0) fails.Add($"{fx.Live.Count} effects left live");
    }
}
