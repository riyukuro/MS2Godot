using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_face_emotion.tscn) of the own pc's face emotion on its built model
/// (PlayerFace.cs, Look.FaceTexture):
/// - the face model on, the first step rests the face on "default": the FA mesh shows the face item's idle frame (base
///   texture and control map from its item code's folder), and the blink frame follows at its time;
/// - the trigger's Ui sub 9 with a name shows that face's frame; with an empty name the rest face comes back;
/// - the body's Emotion_Angry_A throws its emo_angry key: the angry face shows, but not while a trigger's face holds;
/// - an emotion with no texture animation puts the mesh's own material back.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerFaceEmotionCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    Player pc;
    ModelBuilder builder;
    MeshInstance3D fa;
    Material own;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        pc = Player.Create(visual, anim, faceGrids);
        pc.Gender = bodyDir == "male" ? 0 : 1;
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.SetFace(Ms2Godot.Look.FaceTexture.For(visual, builder, packs, pc.Gender));
        fa = visual.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .FirstOrDefault(m => m.Visible && m.HasMeta("mesh") && (string)m.GetMeta("mesh") == "FA");
        own = fa?.GetSurfaceOverrideMaterial(0);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
        GD.Print($"player face emotion: face item {(visual.HasMeta(Ms2Godot.Look.FaceTexture.FaceItemMeta) ? (int)visual.GetMeta(Ms2Godot.Look.FaceTexture.FaceItemMeta) : 0)}, group {pc.FaceEmotion?.Group?.ItemCode ?? "none"}, FA mesh {(fa != null ? "found" : "missing")}");
    }

    void Steps(int n) { for (int i = 0; i < n; i++) step.Invoke(pc, new object[] { (double)Dt }); }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    Texture2D Entry(string e) => builder.TextureEntry(EmotionTextureFrame.Archive, e);

    Texture2D Shown(string param) => fa.GetSurfaceOverrideMaterial(0) is ShaderMaterial sm ? sm.GetShaderParameter(param).As<Texture2D>() : null;

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        try { Run(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"player face emotion: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run()
    {
        var fe = pc.FaceEmotion;
        Expect(fe?.Group != null, "no emotion group for the face item");
        Expect(fa != null, "no shown FA mesh");
        if (fe?.Group == null || fa == null) return;
        string code = fe.Group.ItemCode;

        // the frames the group names, with the face item's code as their folder
        Texture2D Frame(string name, int n) => Entry(fe.Group.Find(name).Texture.Frames[n].BaseEntry(code));
        Steps(1);
        Expect(fe.Current == "default" && fe.Frame == 0, $"rest: {fe.Current} frame {fe.Frame}");
        var idle = Frame("default", 0);
        Expect(idle != null && Shown("base_tex") == idle, "the rest face's first frame is not the base texture");
        var idleCtrl = fe.Group.Find("default").Texture.Frames[0].ControlEntry(code);
        Expect(idleCtrl.Length == 0 || Shown("ctrl_tex") == Entry(idleCtrl), "the first frame's control map is not shown");
        Steps(50);      // 0.85 s
        int at = fe.Group.Find("default").Texture.FrameAt(50 * 1000 / 60);
        Expect(fe.Frame == at && fe.Frame > 0 && Shown("base_tex") == Frame("default", at), $"rest after 0.85 s: frame {fe.Frame}, the data's {at}");
        GD.Print($"player face emotion: rest {fe.Current}, frame {fe.Frame} after 0.85 s");

        pc.TriggerFace("Trigger_panic");
        Steps(1);
        var panic = Frame("Trigger_panic", 0);
        Expect(fe.Current == "Trigger_panic" && panic != null && Shown("base_tex") == panic, $"sub 9: {fe.Current}");
        pc.TriggerFace("");
        Steps(1);
        Expect(fe.Current == "default" && Shown("base_tex") == idle, $"sub 9 empty: {fe.Current}");
        GD.Print("player face emotion: sub 9 Trigger_panic, then rest");

        // the body's emo_ key: Emotion_Angry_A throws emo_angry at its start; the face rests again first
        var angry = pc.Anims.Sequence("Emotion_Angry_A", false);
        Expect(angry != null, "no Emotion_Angry_A sequence");
        if (angry != null)
        {
            pc.PlaySequence(angry, 1, true);
            Steps(1);
            Expect(string.Equals(fe.Current, "angry", System.StringComparison.OrdinalIgnoreCase) && Shown("base_tex") == Frame(fe.Current, 0),
                $"emo_angry: {fe.Current}");
            pc.TriggerFace("Trigger_panic");
            pc.PlaySequence(angry, 1, true);
            Steps(1);
            Expect(fe.Current == "Trigger_panic", $"emo_angry while a trigger's face holds the keys off: {fe.Current}");
            pc.TriggerFace("");
            Steps(1);
            GD.Print("player face emotion: Emotion_Angry_A's emo_angry key, ignored under a trigger's face");
        }

        var plain = fe.Group.Names.FirstOrDefault(n => fe.Group.Find(n).Texture == null);
        if (plain != null)
        {
            pc.TriggerFace(plain);
            Expect(fe.Current == plain && fa.GetSurfaceOverrideMaterial(0) == own, $"{plain}: the own material is not back");
            GD.Print($"player face emotion: {plain} (no texture animation) puts the own material back");
        }
    }
}
