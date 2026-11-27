using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_face_emotion.tscn) of the trigger's Ui sub 9 on npcs in NpcLayer (CTriggerService::FUN_1418641e0,
/// triggerservice.cpp:1052-1068; the npc's group CNpc::vfunc_69/vfunc_70, unit_14108e1e0_npcactorandvibrate.cpp:663, :1427):
/// for npcs the trigger data plays faces on, each on its built body:
/// - the face shows nothing before an emotion plays (PCBase::vfunc_10 starts no timer);
/// - a name from the npc's group waits for its textures (CTextureAnimation state 1), then shows its first frame on the
///   emotion's target mesh: the frame's base texture on a copy of the mesh's character-shader material;
/// - an empty name rests the face;
/// - a name the group lacks changes nothing, and an object id with no npc is not taken;
/// - a target the body lacks (FindTargetNode finds no node) shows nothing and the face rests on the next tick; a NIF target "FA:0" is the node Godot names FA_0.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcFaceEmotionCheck : Node3D
{
    // npc, emotion: rows the trigger data plays on (52000121_qd's FA:0 targets, 02000313_bf's FA and a body-named target)
    static readonly (int Npc, string Name)[] Uses =
    {
        (11003364, "Angry"), (11003365, "Cry"), (11003407, "Trigger_Proud"), (11003387, "Trigger_Sad"), (11003393, "Trigger_Dead"),
    };

    readonly List<string> fails = new();
    NpcLayer npcs;
    ModelBuilder builder;
    EmotionTable table;
    NpcTable rows;
    int frames;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        rows = new NpcTable(packs);
        table = new EmotionTable(packs, EmotionTable.Npc);
        npcs = new NpcLayer(packs, builder, rows, this);
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Process(double delta)
    {
        if (++frames != 3) return;
        try { Run(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"npc face emotion: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static MeshInstance3D Target(Node3D body, string name) => body.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
        .FirstOrDefault(m => m.Visible && m.Name.ToString() == name.Replace(':', '_'));

    void Run()
    {
        Expect(!npcs.TriggerFace(99, "Angry"), "an object id with no npc took the face");
        int oid = 0, shown = 0;
        foreach (var (npc, name) in Uses)
        {
            if (rows.Find(npc) is not { } row) { GD.Print($"npc face emotion: {npc} not in this data"); continue; }
            var data = table.Group((uint)row.EmotionGroupId)?.Find(name);
            if (data?.Texture == null) { GD.Print($"npc face emotion: {npc} '{name}' has no texture emotion here"); continue; }
            string at = $"{npc} '{name}' (group {row.EmotionGroupId}, target {data.Texture.Target})";
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = ++oid, NpcId = npc }));
            var body = npcs.ModelOf(oid)?.Body;
            var fe = npcs.Face(oid);
            Expect(body != null && fe != null && npcs.ModelOf(oid)?.Placeholder == false, $"{at}: no built body with a face");
            if (body == null || fe == null) continue;
            var mesh = Target(body, data.Texture.Target);
            if (mesh == null)
            {
                // the client's FindTargetNode finds no node of that name on this body: the texture animation does not start,
                // so the next tick's Update fails and the face rests (PCBase::FUN_14166f940, pcbase.cpp:1574-1584)
                Expect(npcs.TriggerFace(oid, name), $"{at}: not taken");
                Expect(fe.Current == name && fe.Frame == -1, $"{at}: a target the body lacks showed frame {fe.Frame}");
                npcs.Process(1f / 60);
                Expect(fe.Current == fe.Rest && fe.TextKeys, $"{at}: the face did not rest ('{fe.Current}')");
                GD.Print($"npc face emotion: {at}: the body has no such mesh, nothing shown, then the rest face {fe.Current} (as the client)");
                npcs.TriggerFace(oid, "");
                continue;
            }
            var own = mesh.GetSurfaceOverrideMaterial(0);
            npcs.Process(0.1f);
            Expect(fe.Frame == -1 && fe.Current == "", $"{at}: a face before any emotion: '{fe.Current}' frame {fe.Frame}");

            Expect(npcs.TriggerFace(oid, name), $"{at}: not taken");
            npcs.Process(1f / 60);
            Expect(fe.Current == name && fe.Frame == -1, $"{at}: shown before its textures (frame {fe.Frame})");
            for (int k = 0; k < 64 && fe.Frame < 0; k++) npcs.Process(1f / 60);
            var frame0 = builder.TextureEntry(EmotionTextureFrame.Archive, data.Texture.Frames[0].BaseEntry(table.Group((uint)row.EmotionGroupId).ItemCode));
            var layer = mesh.GetSurfaceOverrideMaterial(0) as ShaderMaterial;
            Expect(fe.Frame >= 0 && layer != null && layer != own && frame0 != null,
                $"{at}: no frame after the wait (frame {fe.Frame}, layer {layer != null}, texture {frame0 != null})");
            if (fe.Frame == 0 && layer != null)
                Expect(layer.GetShaderParameter(ShaderParam.BaseTex).As<Texture2D>() == frame0, $"{at}: the first frame is not the base texture");
            if (fe.Frame >= 0) shown++;
            GD.Print($"npc face emotion: {at}: frame {fe.Frame} on {mesh.Name}");

            string before = fe.Current;
            npcs.TriggerFace(oid, "NoSuchEmotion_Check");
            Expect(fe.Current == before, $"{at}: a name the group lacks changed the face to '{fe.Current}'");
            npcs.TriggerFace(oid, "");
            Expect(fe.TextKeys && fe.Current != name, $"{at}: an empty name did not rest the face ('{fe.Current}')");
        }
        Expect(shown >= 2, $"only {shown} npc faces shown");
    }
}
