using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/action_anims.tscn): ActionAnims.Load on both player bodies of the data, male and female, with
/// the companion .xml and table/pcactiondata.xml read through PackXml. KMS2_DEV's female/female.xml starts with a UTF-8
/// byte-order mark, which stopped a field load ("Data at the root level is invalid") for a female character. Each body must
/// give an idle, a run and a hit sequence (HIT, action 53: PC::vfunc_123(0x35), the plain hit). The remote layer loads one
/// ActionAnims per body on its first PC and shares it: with that PC's body freed, a second body on the shared library still
/// resolves a sequence not asked for before (a walk) without touching the freed AnimationPlayer. Prints "action anims: ok",
/// quits 0, or 1.
/// </summary>
public partial class ActionAnimsCheck : Node
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var fails = new List<string>();
        foreach (var dir in new[] { "male", "female" })
        {
            var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
            builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
            try
            {
                var md = builder.LoadModel($"{dir}/{(dir == "male" ? "m_body.nif" : "f_body.nif")}");
                var visual = builder.Build(md, dir == "male" ? "Male" : "Female");
                var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
                var aa = ActionAnims.Load(packs, builder, md, anim, dir);
                foreach (var act in new[] { ActionAnims.Act.Idle, ActionAnims.Act.Run, ActionAnims.Act.Hit })
                    if (aa.Name(act) == null) fails.Add($"{dir}: no sequence for {act}");
                GD.Print($"action anims: {dir} idle '{aa.Name(ActionAnims.Act.Idle)}' run '{aa.Name(ActionAnims.Act.Run)}' hit '{aa.Name(ActionAnims.Act.Hit)}'");
                // the remote layer's sharing: a second body on the first one's library, the first body gone
                var lib = anim.GetAnimationLibrary("");
                var second = builder.Build(md, dir == "male" ? "Male" : "Female");
                var anim2 = second.GetNode<AnimationPlayer>("Model/AnimationPlayer");
                anim2.RemoveAnimationLibrary("");
                anim2.AddAnimationLibrary("", lib);
                visual.Free();
                string walk = aa.Name(ActionAnims.Act.Walk);
                if (walk == null || !anim2.HasAnimation(walk)) fails.Add($"{dir}: walk '{walk}' not on the shared library after the first body was freed");
                second.Free();
            }
            catch (System.Exception ex) { fails.Add($"{dir}: {ex.GetType().Name}: {ex.Message}"); }
        }
        GD.Print($"action anims: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
