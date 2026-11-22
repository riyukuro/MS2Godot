using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_showfx.tscn) of the show and hide effects of the trigger mesh and rope / ladder
/// events (TriggerObjectMesh::vfunc_2, triggeractioncli.cpp:687-701; TriggerObjectRope::vfunc_2, :747-761): once the
/// entity's fade starts and the event is not a type 2 one, its showEffectXml or hideEffectXml plays once on the entity.
/// - 02010052's mesh 6204 has both, and they differ: a type 2 hide plays none; a type 3 show plays the show effect at the
///   entity's placement; a show of a shown mesh starts no fade and plays none; a type 3 hide plays the hide effect.
/// - 02000338's ladder 124 has a show effect only, and a ladder picks by its second bool: a hide with the second bool set
///   plays the show effect; a show with it clear plays the empty hide effect, so none.
/// The meshes are given one stand-in instance each, so the switch is the one without a fade. Needs MS2_DATA.
/// Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerShowFxCheck : Node
{
    const int MeshField = 2010052, MeshId = 6204, LadderField = 2000338, LadderId = 124;

    readonly List<string> fails = new();
    readonly List<MultiMesh> stand = new();

    /// <summary>The effect service the triggers call: the one-shot effects they insert.</summary>
    sealed class Recorder : IRegionSkillEffects
    {
        public readonly List<(string Effect, Xform At)> Inserted = new();
        public bool Has(string effect) => true;
        public int Spawn(string effect, Xform at, bool always, int randomIndex) => 0;
        public void Insert(string effect, Xform at, bool always, int randomIndex) => Inserted.Add((effect, at));
        public void FadeOut(int handle, float seconds) { }
    }

    public override void _Ready()
    {
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"trigger showfx check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    (FieldTriggers, FieldEntity, Recorder) Field(PackSet packs, int field, int id)
    {
        var fd = FieldData.FindById(packs, field) ?? throw new InvalidOperationException($"no field {field}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var e = map.Entities.FirstOrDefault(x => TriggerObjects.KindOf(x.HasMixin) != null && (int)x.Number("TriggerObjectID") == id)
                ?? throw new InvalidOperationException($"no trigger object {id} on {fd.XBlock}");
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 1 };
        mm.SetInstanceTransform(0, Transform3D.Identity);
        stand.Add(mm);
        var draws = new Dictionary<FieldEntity, List<(MultiMesh, int)>> { [e] = new() { (mm, 0) } };
        var t = new FieldTriggers(map.Entities, draws);
        var rec = new Recorder();
        t.Bind(rec);
        return (t, e, rec);
    }

    static Maple2.PacketLib.Tools.ByteReader Packet(byte type, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);
        if (type == TriggerObjects.Load) w.Write(1);
        body(w);
        return new Maple2.PacketLib.Tools.ByteReader(ms.ToArray());
    }

    static Maple2.PacketLib.Tools.ByteReader Mesh(byte type, bool show) => Packet(type, w =>
    {
        w.Write(MeshId); w.Write(show); w.Write((byte)0); w.Write(5);
        w.Write((short)0); w.Write(0f);                 // no model name, skin scale
    });

    static Maple2.PacketLib.Tools.ByteReader Ladder(bool show, bool second) => Packet(TriggerObjects.Update, w =>
    {
        w.Write(LadderId); w.Write(show); w.Write(second); w.Write(5);
    });

    void Expect(string what, Recorder rec, string effect, FieldEntity e)
    {
        var got = rec.Inserted.ToList();
        rec.Inserted.Clear();
        if (effect == null)
        {
            if (got.Count != 0) fails.Add($"{what}: {got.Count} effects inserted, want none");
            return;
        }
        if (got.Count != 1 || got[0].Effect != effect) { fails.Add($"{what}: inserted [{string.Join(", ", got.Select(g => g.Effect))}], want [{effect}]"); return; }
        var want = e.Transform;
        if (got[0].At.T.X != want.T.X || got[0].At.T.Y != want.T.Y || got[0].At.T.Z != want.T.Z || got[0].At.S != want.S
            || !got[0].At.R.SequenceEqual(want.R))
            fails.Add($"{what}: not at the entity's placement");
    }

    void Check()
    {
        var packs = PackSet.Shared(Settings.DataDir);

        var (mt, me, mrec) = Field(packs, MeshField, MeshId);
        string show = me.Get("showEffectXml") ?? "", hide = me.Get("hideEffectXml") ?? "";
        if (show.Length == 0 || hide.Length == 0 || show == hide) fails.Add($"mesh {MeshId}: want two different show and hide effects");
        mt.Apply(Mesh(TriggerObjects.Load, false));
        if (mt.MeshShown(MeshId) != false) fails.Add("type 2 hide: mesh still shown");
        Expect("type 2 hide", mrec, null, me);
        mt.Apply(Mesh(TriggerObjects.Update, true));
        if (mt.MeshShown(MeshId) != true) fails.Add("show: mesh not shown");
        Expect("show", mrec, show, me);
        mt.Apply(Mesh(TriggerObjects.Update, true));
        Expect("show of a shown mesh", mrec, null, me);
        mt.Apply(Mesh(TriggerObjects.Update, false));
        Expect("hide", mrec, hide, me);

        var (lt, le, lrec) = Field(packs, LadderField, LadderId);
        string lshow = le.Get("showEffectXml") ?? "";
        if (lt.KindOf(LadderId) != TriggerObjectKind.Rope) fails.Add($"{LadderId} is not a ladder");
        if (lshow.Length == 0 || (le.Get("hideEffectXml") ?? "").Length != 0) fails.Add($"ladder {LadderId}: want a show effect only");
        lt.Apply(Ladder(false, true));
        Expect("ladder hide, second bool set", lrec, lshow, le);
        lt.Apply(Ladder(true, false));
        Expect("ladder show, second bool clear", lrec, null, le);

        GD.Print($"trigger showfx check: mesh {MeshId} and ladder {LadderId} checked");
    }
}
