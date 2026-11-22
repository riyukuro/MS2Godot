using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// What combat shows in the replay: skill effects (effect xml -> nif models from the Effect pack, played where the caster or the target stands),
/// floating damage numbers, hp bars over npcs. Effects are drawn from the NiMesh geometry the NIF reader supports, as static
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
uniform vec4 full_color = vec4(0.85, 0.15, 0.15, 1.0);   // gamma values: the colour buffer holds gamma values (field_lit.gdshader)
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}
void fragment() {
    bool edge = UV.x < 0.02 || UV.x > 0.98 || UV.y < 0.08 || UV.y > 0.92;
    vec3 c = edge ? vec3(0.0) : (UV.x < fill ? full_color.rgb : vec3(0.537));
    ALBEDO = c;
}",
        };
    }

    static Godot.Vector3 G(float x, float y, float z) => new(x, z, -y);

    /// <summary>A bar over the owner; returns the material to update.</summary>
    public ShaderMaterial HpBar(Node3D owner, float height)
    {
        var mat = new ShaderMaterial { Shader = barShader };
        mat.SetShaderParameter(ShaderParam.Fill, 1.0f);
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
            Modulate = color.LinearToSrgb(), OutlineSize = 14, OutlineModulate = new Color(0, 0, 0, 0.95f), Position = at + new Godot.Vector3((float)GD.RandRange(-25, 25), 0, (float)GD.RandRange(-25, 25)) };
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

    /// <summary>Plays an effect xml at a place: the nif parts show for their duration; sound events play at the place; soundMusic parts follow CSoundMusicEffect's caster-kind gate (m_unk_18 &lt;= 1, default rejected for unknown/remote casters).</summary>
    public void PlayEffect(string xmlPath, Godot.Vector3 at, float yawRadians, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => PlayEffect(xmlPath, at, new Basis(Godot.Vector3.Up, yawRadians), casterKind);

    /// <summary>The same turned by a full rotation (godot space), as an effect's NiTransform turns it.</summary>
    public void PlayEffect(string xmlPath, Godot.Vector3 at, Basis rotation, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => Play(xmlPath, at, rotation, casterKind, null);

    /// <summary>
    /// An effect on an actor (CEffect SetActor, no transform of its own); nothing when the actor is gone. Each nif part goes
    /// where CNifEffect::vfunc_3 (nifeffectparam.cpp:667) puts it by its attach type (<see cref="EffectPart.AttachKind"/>):
    /// - type 0 (AttachToOwnerNode, :1044): under the world effect root, at the actor's parent node's world transform taken
    ///   once at the start (<see cref="WorldPlace"/>); it does not follow the actor and plays to its own end after the
    ///   actor is removed;
    /// - the others (type 1 Node :1079, 2 Mesh :1118, 3/4 weapon): under the actor, so they follow it and go with it at once.
    /// Sound and light parts play at its place now.
    /// STILL-OPEN: types 1-4 hang on the actor's root, not on the named node or mesh parent.
    /// </summary>
    public void PlayEffectOn(string xmlPath, Node3D actor, int casterKind)
    {
        if (actor == null || !GodotObject.IsInstanceValid(actor)) return;
        Play(xmlPath, actor.GlobalPosition, Basis.Identity, casterKind, actor);
        EffectsOnActors++;
    }

    /// <summary>Effects played on an actor (<see cref="PlayEffectOn"/>).</summary>
    public int EffectsOnActors;

    void Play(string xmlPath, Godot.Vector3 at, Basis rotation, int casterKind, Node3D actor)
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
            if (part.Type == "light" && part.Light != null) { QueueLight(part, at, rotation); continue; }
            if (string.Equals(part.Type, "soundMusic", StringComparison.OrdinalIgnoreCase))
            {
                FieldAudio.Instance?.PlayEffectSoundMusic(part, casterKind);
                EffectSounds++;
                continue;
            }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath)) { EffectsNotNif++; continue; }
            bool onActor = actor != null && part.AttachKind != 0;
            var place = actor == null ? new Transform3D(rotation, at) : onActor ? Transform3D.Identity : WorldPlace(part, actor);
            var host = onActor ? actor : parent;
            if (Particles && TryScene(part, place, host)) continue;
            var e = Effect(part.FilePath);
            if (e == null) { EffectsNoNif++; MissingEffects.Add(part.FilePath); continue; }
            if (e.Value.particles) { EffectsWithParticles++; ParticleEffects.Add(part.FilePath); }
            if (e.Value.md.Meshes.Count == 0) continue;
            var root = mb.Build(e.Value.md, "Effect");
            float s = part.Scale <= 0 ? 1 : part.Scale;
            root.Scale = new Godot.Vector3(s, s, s);
            var holder = new Node3D { Transform = place, Visible = part.StartDelay <= 0 };
            root.Position = G(part.Translate[0], part.Translate[1], part.Translate[2]);
            holder.AddChild(root);
            host.AddChild(holder);
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

    /// <summary>How long <see cref="PlayEffect(string, Godot.Vector3, Basis, int)"/> keeps an effect xml's nif parts: the
    /// latest start delay plus duration (0.6 s without one, at most 3 s), 0 for no xml, hidden effects or no nif part.</summary>
    public float EffectLength(string xmlPath)
    {
        if (!ShowEffects || string.IsNullOrEmpty(xmlPath) || effects.Get(xmlPath) is not { } parts) return 0;
        float end = 0;
        foreach (var part in parts)
            if (part.Type == "nif" && !string.IsNullOrEmpty(part.FilePath))
                end = Mathf.Max(end, part.StartDelay + Mathf.Min(part.Duration > 0 ? part.Duration : 0.6f, 3f));
        return end;
    }

    /// <summary>An effect light waiting for its start delay or running (CLightEffect: it adds its light when it starts and the
    /// effect's end destroys it, FieldLightSet.StartEffect / EndEffect). The light entity is added at Init
    /// (lighteffectparam.cpp:382, AddEntity) and attached only after m_fStartDelay (:384, :401-411); whether the added but
    /// unattached entity already lights is STILL-OPEN, so here the light starts at the delay.</summary>
    sealed class LiveLight
    {
        public EffectLightParam Param; public Ms2.Formats.Model.Vec3 Pos; public float Born, Start, End; public FieldLight Light;
    }
    readonly List<LiveLight> lights = new();
    public int EffectLightsStarted;

    void QueueLight(EffectPart part, Godot.Vector3 at, Basis rotation)
    {
        // the part's translate is in the effect's own space (Z up, turned by the effect's rotation), the light's place in client space
        var off = rotation * new Godot.Vector3(part.Translate[0], part.Translate[2], -part.Translate[1]);
        var g = at + off;
        float dur = part.Duration > 0 ? part.Duration : 0.6f;
        lights.Add(new LiveLight { Param = part.Light, Pos = new Ms2.Formats.Model.Vec3(g.X, -g.Z, g.Y), Start = part.StartDelay, End = part.StartDelay + Mathf.Min(dur, 3f) });
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

    /// <summary>
    /// Where a type 0 part on <paramref name="actor"/> starts, local to the field node (AttachToOwnerNode, nifeffectparam.cpp:1044):
    /// with applyNodeTransform, the world transform of the node named parentNode under the actor (else the actor's own),
    /// keeping only the translate / rotation / scale its applyNodeTransform bits allow (:1058-1072). Without it the part takes
    /// no transform from the actor at all: the remain's request has no transform (magicdata.cpp:2872), so it plays at the
    /// world origin. That is a client quirk, kept as is.
    /// </summary>
    Transform3D WorldPlace(EffectPart part, Node3D actor)
    {
        if (!part.ApplyNodeTransform) return Transform3D.Identity;
        var w = NodeWorld(actor, part.ParentNode);
        if (!(part.ApplyTranslate && part.ApplyRotation && part.ApplyScale))
        {
            float s = part.ApplyScale ? w.Basis.Scale.X : 1f;
            var rot = part.ApplyRotation ? new Basis(w.Basis.GetRotationQuaternion()) : Basis.Identity;
            w = new Transform3D(rot.Scaled(new Godot.Vector3(s, s, s)), part.ApplyTranslate ? w.Origin : Godot.Vector3.Zero);
        }
        return parent.GlobalTransform.AffineInverse() * w;
    }

    /// <summary>The world transform of the node <paramref name="name"/> under <paramref name="actor"/> (GetObjectByName on its
    /// scene node: a node, else a skeleton bone), else the actor's own (the fallback to the actor node, :1053).</summary>
    static Transform3D NodeWorld(Node3D actor, string name)
    {
        if (string.IsNullOrEmpty(name)) return actor.GlobalTransform;
        if (actor.FindChild(name, true, false) is Node3D n) return n.GlobalTransform;
        foreach (var sk in actor.FindChildren("*", "Skeleton3D", true, false))
            if (sk is Skeleton3D s && s.FindBone(name) is int b and >= 0) return s.GlobalTransform * s.GetBoneGlobalPose(b);
        return actor.GlobalTransform;
    }

    /// <summary>Plays an effect part through the scene runtime (particles and controllers) at <paramref name="place"/> under
    /// <paramref name="host"/>; false when the nif could not be read as a scene.</summary>
    bool TryScene(EffectPart part, Transform3D place, Node3D host)
    {
        var sc = Scene(part.FilePath);
        if (sc == null) return false;
        float dur = part.Duration > 0 ? part.Duration : 0.6f;
        var inst = new EffectInstance(sc, mb, dur, (int)(GD.Randi() & 0x7fffffff)) { Transform = place, Delay = part.StartDelay };
        float s = part.Scale <= 0 ? 1 : part.Scale;
        var q = new Godot.Quaternion(Godot.Vector3.Back, Mathf.DegToRad(part.Rotation[2])) * new Godot.Quaternion(Godot.Vector3.Up, Mathf.DegToRad(part.Rotation[1])) * new Godot.Quaternion(Godot.Vector3.Right, Mathf.DegToRad(part.Rotation[0]));
        inst.FileSpace = new Transform3D(new Basis(q).Scaled(new Godot.Vector3(s, s, s)), new Godot.Vector3(part.Translate[0], part.Translate[1], part.Translate[2]));
        host.AddChild(inst);
        SceneEffects++; EffectsSpawned++;
        if (sc.Systems.Count > 0) { ParticleSystemsRun += sc.Systems.Count; EffectsWithParticles++; ParticleEffects.Add(part.FilePath); }
        return true;
    }

    public void Process(float dt)
    {
        for (int i = lights.Count - 1; i >= 0; i--)   // only a start or an end touches the light data
        {
            var l = lights[i];
            l.Born += dt;
            if (l.Light == null && l.Born >= l.Start && l.Born < l.End) { l.Light = FieldLights.Start(l.Param, l.Pos); if (l.Light != null) EffectLightsStarted++; }
            if (l.Born >= l.End) { FieldLights.End(l.Light); lights.RemoveAt(i); }
        }
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
            if (!GodotObject.IsInstanceValid(n)) { live.RemoveAt(i); continue; }   // its actor went and took it
            born += dt;
            if (born >= start) n.Visible = true;
            float a = Mathf.Clamp(1f - (born - start) / Mathf.Max(end - start, 0.05f), 0f, 1f) * 0.85f;
            foreach (var m in mats) m.AlbedoColor = new Color(m.AlbedoColor, a);
            if (born >= end) { n.QueueFree(); live.RemoveAt(i); } else live[i] = (n, born, start, end, mats);
        }
    }
}
