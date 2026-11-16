using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// What combat shows in the replay: skill effects (effect xml -> nif models from the Effect pack, played where the caster or the target stands),
/// floating damage numbers, hp bars over npcs and name tags. Effects are drawn from the NiMesh geometry the NIF reader supports, as static
/// meshes for the effect's duration; particle systems (NiPS*), texture/alpha controllers and flip animations are not played, they are counted.
/// </summary>
public sealed class CombatLayer
{
    public const string EffectArchive = "Resource/Model/Effect";

    readonly ModelBuilder mb; readonly PackSet packs; readonly EffectTable effects; readonly Node3D parent;
    readonly Dictionary<string, (ModelData md, bool particles)> models = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Label3D label, float age)> numbers = new();
    readonly List<(Node3D node, float born, float start, float end, List<StandardMaterial3D> mats)> live = new();
    readonly Shader barShader;
    readonly Dictionary<string, EffectScene> scenes = new(StringComparer.OrdinalIgnoreCase);
    public bool Particles = true;
    public int SceneEffects, ParticleSystemsRun, SceneFallbacks;
    public readonly HashSet<string> Unsupported = new();
    public int EffectsSpawned, EffectsNoXml, EffectsNoNif, EffectsNotNif, EffectsWithParticles, EffectsFailed, NumbersShown, EffectSounds;
    public readonly HashSet<string> MissingEffects = new();
    public readonly HashSet<string> ParticleEffects = new();
    public bool ShowEffects = true;

    public CombatLayer(PackSet packs, ModelBuilder mb, EffectTable effects, Node3D parent)
    {
        this.packs = packs; this.mb = mb; this.effects = effects; this.parent = parent;
        if (!mb.TextureArchives.Contains(EffectArchive)) mb.TextureArchives = mb.TextureArchives.Append(EffectArchive).ToArray();
        barShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, depth_test_disabled, cull_disabled;
uniform float fill = 1.0;
uniform vec4 full_color : source_color = vec4(0.85, 0.15, 0.15, 1.0);
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}
void fragment() {
    bool edge = UV.x < 0.02 || UV.x > 0.98 || UV.y < 0.08 || UV.y > 0.92;
    vec3 c = edge ? vec3(0.0) : (UV.x < fill ? full_color.rgb : vec3(0.25));
    ALBEDO = c;
}",
        };
    }

    static Godot.Vector3 G(float x, float y, float z) => new(x, z, -y);

    // ---- tags ----

    public Label3D NameTag(Node3D owner, string text, float height, Color color)
    {
        var l = new Label3D { Text = text, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 44, PixelSize = 0.55f, Modulate = color,
            OutlineSize = 10, OutlineModulate = new Color(0, 0, 0, 0.9f), Position = new Godot.Vector3(0, height + 15, 0), Name = "NameTag" };
        owner.AddChild(l);
        return l;
    }

    /// <summary>A bar over the owner; returns the material to update.</summary>
    public ShaderMaterial HpBar(Node3D owner, float height)
    {
        var mat = new ShaderMaterial { Shader = barShader };
        mat.SetShaderParameter("fill", 1.0f);
        var mi = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(90, 9) }, MaterialOverride = mat, Position = new Godot.Vector3(0, height + 55, 0), Name = "HpBar" };
        owner.AddChild(mi);
        return mat;
    }

    // ---- damage numbers ----

    public void Number(Godot.Vector3 at, long damage, byte flag)
    {
        // flags: 0 normal; others are drawn in other colors (the kinds are not decoded: crit, miss, ... )
        var color = flag == 0 ? Colors.White : flag == 1 ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 0.9f, 0.2f);
        var l = new Label3D { Text = damage.ToString("N0"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 64, PixelSize = 0.7f,
            Modulate = color, OutlineSize = 14, OutlineModulate = new Color(0, 0, 0, 0.95f), Position = at + new Godot.Vector3((float)GD.RandRange(-25, 25), 0, (float)GD.RandRange(-25, 25)) };
        parent.AddChild(l);
        numbers.Add((l, 0));
        NumbersShown++;
    }

    // ---- effects ----

    (ModelData md, bool particles)? Effect(string nifPath)
    {
        string entry = nifPath.Replace('\\', '/').ToLowerInvariant();
        if (models.TryGetValue(entry, out var m)) return m.md == null ? null : m;
        if (!packs.Exists(EffectArchive, entry)) { models[entry] = default; return null; }
        try
        {
            var nif = NifFile.Load(mb.Spec, packs.Read(EffectArchive, entry));
            bool particles = nif.Blocks.Any(b => b.Type.StartsWith("NiPS", StringComparison.Ordinal));
            m = (ModelData.Load(nif), particles);
        }
        catch (Exception ex) { GD.PushWarning($"effect {entry}: {ex.Message}"); EffectsFailed++; models[entry] = default; return null; }
        models[entry] = m;
        return m;
    }

    /// <summary>Plays an effect xml at a place: the nif parts show for their duration; sound events play at the place; other part kinds are ignored.</summary>
    public void PlayEffect(string xmlPath, Godot.Vector3 at, float yawRadians)
    {
        if (!ShowEffects || string.IsNullOrEmpty(xmlPath)) return;
        var parts = effects.Get(xmlPath);
        if (parts == null) { EffectsNoXml++; MissingEffects.Add(xmlPath); return; }
        foreach (var part in parts)
        {
            if (part.Type == "soundEvent" && part.SoundEvent != null)
            {
                FieldAudio.Instance?.PlayEventAt(part.SoundGroup == null ? part.SoundEvent : part.SoundGroup + "/" + part.SoundEvent, at, part.StartDelay);
                EffectSounds++;
                continue;
            }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath)) { EffectsNotNif++; continue; }
            if (Particles && TryScene(part, at, yawRadians)) continue;
            var e = Effect(part.FilePath);
            if (e == null) { EffectsNoNif++; MissingEffects.Add(part.FilePath); continue; }
            if (e.Value.particles) { EffectsWithParticles++; ParticleEffects.Add(part.FilePath); }
            if (e.Value.md.Meshes.Count == 0) continue;
            var root = mb.Build(e.Value.md, "Effect");
            float s = part.Scale <= 0 ? 1 : part.Scale;
            root.Scale = new Godot.Vector3(s, s, s);
            var holder = new Node3D { Position = at, Rotation = new Godot.Vector3(0, yawRadians, 0), Visible = part.StartDelay <= 0 };
            root.Position = G(part.Translate[0], part.Translate[1], part.Translate[2]);
            holder.AddChild(root);
            parent.AddChild(holder);
            float dur = part.Duration > 0 ? part.Duration : 0.6f;
            // Without the nif's alpha and texture controllers the meshes would sit solid; draw them additive and unlit and fade them out
            // over the duration, so dark or hidden parts vanish and light parts glow.
            var mats = new List<StandardMaterial3D>();
            foreach (var mi in root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
                if (mi.GetActiveMaterial(0) is StandardMaterial3D sm)
                {
                    var c = (StandardMaterial3D)sm.Duplicate();
                    c.BlendMode = BaseMaterial3D.BlendModeEnum.Add; c.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                    c.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; c.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    mi.SetSurfaceOverrideMaterial(0, c); mats.Add(c);
                }
            live.Add((holder, 0f, part.StartDelay, part.StartDelay + Mathf.Min(dur, 3f), mats));
            EffectsSpawned++;
        }
    }

    EffectScene Scene(string nifPath)
    {
        string entry = nifPath.Replace('\\', '/').ToLowerInvariant();
        if (scenes.TryGetValue(entry, out var sc)) return sc;
        sc = null;
        if (packs.Exists(EffectArchive, entry))
        {
            try { sc = EffectScene.Load(NifFile.Load(mb.Spec, packs.Read(EffectArchive, entry))); foreach (var u in sc.Unsupported) Unsupported.Add(u); }
            catch (Exception ex) { GD.PushWarning($"effect scene {entry}: {ex.Message}"); SceneFallbacks++; }
        }
        return scenes[entry] = sc;
    }

    /// <summary>Plays an effect part through the scene runtime (particles and controllers); false when the nif could not be read as a scene.</summary>
    bool TryScene(EffectPart part, Godot.Vector3 at, float yawRadians)
    {
        var sc = Scene(part.FilePath);
        if (sc == null) return false;
        float dur = part.Duration > 0 ? part.Duration : 0.6f;
        var inst = new EffectInstance(sc, mb, dur, (int)(GD.Randi() & 0x7fffffff)) { Position = at, Rotation = new Godot.Vector3(0, yawRadians, 0), Delay = part.StartDelay };
        float s = part.Scale <= 0 ? 1 : part.Scale;
        var q = new Godot.Quaternion(Godot.Vector3.Back, Mathf.DegToRad(part.Rotation[2])) * new Godot.Quaternion(Godot.Vector3.Up, Mathf.DegToRad(part.Rotation[1])) * new Godot.Quaternion(Godot.Vector3.Right, Mathf.DegToRad(part.Rotation[0]));
        inst.FileSpace = new Transform3D(new Basis(q).Scaled(new Godot.Vector3(s, s, s)), new Godot.Vector3(part.Translate[0], part.Translate[1], part.Translate[2]));
        parent.AddChild(inst);
        SceneEffects++; EffectsSpawned++;
        if (sc.Systems.Count > 0) { ParticleSystemsRun += sc.Systems.Count; EffectsWithParticles++; ParticleEffects.Add(part.FilePath); }
        return true;
    }

    public void Process(float dt)
    {
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            var (l, age) = numbers[i];
            age += dt;
            l.Position += new Godot.Vector3(0, 90 * dt, 0);
            l.Modulate = new Color(l.Modulate, Mathf.Clamp(1.4f - age, 0, 1));
            if (age > 1.4f) { l.QueueFree(); numbers.RemoveAt(i); } else numbers[i] = (l, age);
        }
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var (n, born, start, end, mats) = live[i];
            born += dt;
            if (born >= start) n.Visible = true;
            float a = Mathf.Clamp(1f - (born - start) / Mathf.Max(end - start, 0.05f), 0f, 1f) * 0.85f;
            foreach (var m in mats) m.AlbedoColor = new Color(m.AlbedoColor, a);
            if (born >= end) { n.QueueFree(); live.RemoveAt(i); } else live[i] = (n, born, start, end, mats);
        }
    }
}
