using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;

namespace Ms2Godot.World;

/// <summary>
/// Entities with the EffectMesh mixin (the spawn and exit portals, effect models of props) are effect NIFs: translucent, additive,
/// alpha-animated, spinning meshes. Drawn as static meshes they show as an opaque white box, so each plays through the effect scene
/// runtime (EffectInstance) at the entity's position, Z rotation and scale, looping.
/// </summary>
public static class FieldEffects
{
    public static Node3D Build(PackSet packs, ModelBuilder builder, List<(FieldEntity entity, string archive, string entry)> list)
    {
        var root = new Node3D { Name = "FieldEffects" };
        var scenes = new Dictionary<string, EffectScene>();
        int n = 0;
        foreach (var (e, archive, entry) in list)
        {
            try
            {
                if (!scenes.TryGetValue(entry, out var scene))
                    scenes[entry] = scene = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(archive, entry)));
                float s = (float)e.Scale;
                var basis = new Basis(Vector3.Back, Mathf.DegToRad((float)e.RotationDeg.Z)).Scaled(new Vector3(s, s, s));
                var fx = new EffectInstance(scene, builder, 1e7f, n) { Name = "Effect_" + n, Position = FieldBuilder.ToGodotPoint(e.Position), FileSpace = new Transform3D(basis, Vector3.Zero) };
                root.AddChild(fx);
                n++;
            }
            catch (Exception ex) { GD.PushWarning($"effect {entry}: {ex.Message}"); }
        }
        GD.Print($"field effects: {n} of {list.Count}: " + string.Join(", ", list.GroupBy(x => x.entity.ModelName).Select(g => g.Key + " x" + g.Count())));
        return root;
    }
}
