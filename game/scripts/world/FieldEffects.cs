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
/// runtime (EffectInstance) at the entity's position, rotation and scale, looping.
/// </summary>
public static class FieldEffects
{
    /// <param name="portals">Filled with each portal entity's effect (Ms2.Ui.PortalDraws shows and hides it). A portal hidden
    /// at the load starts hidden and not processing.</param>
    public static Node3D Build(PackSet packs, ModelBuilder builder, List<(FieldEntity entity, string archive, string entry)> list,
        Dictionary<FieldEntity, Node3D> portals = null)
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
                // the entity's full rotation R = Rx * Ry * Rz and scale (FieldEntity.EffectSpace, XBlock.cs), as the field props
                // take it; the position goes on the node
                var fx = new EffectInstance(scene, builder, 1e7f, n) { Name = "Effect_" + n, Position = FieldBuilder.ToGodotPoint(e.Position), FileSpace = ModelBuilder.ToGodot(e.EffectSpace) };
                if (e.HasMixin("Portal"))
                {
                    portals?.TryAdd(e, fx);
                    Show(fx, e.Bool("IsVisible", true));
                }
                root.AddChild(fx);
                n++;
            }
            catch (Exception ex) { GD.PushWarning($"effect {entry}: {ex.Message}"); }
        }
        GD.Print($"field effects: {n} of {list.Count}: " + string.Join(", ", list.GroupBy(x => x.entity.ModelName).Select(g => g.Key + " x" + g.Count())));
        return root;
    }

    /// <summary>An effect shown or hidden: a hidden one is not drawn and not run.</summary>
    public static void Show(Node3D fx, bool shown)
    {
        if (!GodotObject.IsInstanceValid(fx)) return;
        fx.Visible = shown;
        fx.ProcessMode = shown ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
    }

    /// <summary>
    /// The particle systems of dynamic field models (FieldBuilder.ParticleEntities). A dynamic entity is updated every tick
    /// on the game clock (FieldBuilder.IsStatic), and its NiPSParticleSystems with it, so each plays as a looping effect at the
    /// entity's position, rotation and scale, with the model's meshes left to the field draw.
    /// </summary>
    public static Node3D BuildParticles(ModelBuilder builder, List<(FieldEntity entity, EffectScene particles)> list, List<string> staticModels)
    {
        var root = new Node3D { Name = "FieldParticles" };
        int n = 0;
        foreach (var (e, scene) in list)
        {
            try
            {
                root.AddChild(new EffectInstance(scene, builder, 1e7f, n) { Name = "Particles_" + n, Position = FieldBuilder.ToGodotPoint(e.Position), FileSpace = ModelBuilder.ToGodot(e.EffectSpace) });
                n++;
            }
            catch (Exception ex) { GD.PushWarning($"particles {e.ModelName}: {ex.Message}"); }
        }
        if (staticModels.Count > 0) GD.Print($"field particles not run, static entities: " + string.Join(", ", staticModels.GroupBy(x => x).Select(g => g.Key + " x" + g.Count())));
        if (n > 0) GD.Print($"field particles: {n} entities, {list.Select(x => x.particles).Distinct().Sum(s => s.Systems.Count)} systems in {list.Select(x => x.particles).Distinct().Count()} models: " + string.Join(", ", list.GroupBy(x => x.entity.ModelName).Select(g => g.Key + " x" + g.Count())));
        return root;
    }
}
