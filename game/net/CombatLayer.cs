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
    /// <summary>The nif parts not made because they were out of the camera actor's view (<see cref="Play"/>).</summary>
    public int PartsOutOfView;
    /// <summary>The parts one play makes (<see cref="Ms2.Formats.World.EffectRandom.PlayList"/>), kept between plays.</summary>
    readonly List<Ms2.Formats.World.EffectPart> playParts = new();

    /// <summary>The view distances of the graphic quality (Table/effectOption.xml, read once per data set).</summary>
    Ms2.Formats.World.EffectSkipTable Skip => skip ??= FieldAudio.EffectSkip(packs);
    Ms2.Formats.World.EffectSkipTable skip;
    public readonly HashSet<string> MissingEffects = new();
    public readonly HashSet<string> ParticleEffects = new();
    public bool ShowEffects = true;
    static readonly bool FxTrace = System.Environment.GetEnvironmentVariable("MS2_LIVE_FX_TRACE") == "1";

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
            m = (ModelBuilder.Read(nif, EffectArchive + "/" + entry), particles);
        }
        catch (Exception ex) { GD.PushWarning($"effect {entry}: {ex.Message}"); EffectsFailed++; models[entry] = default; return null; }
        models[entry] = m;
        return m;
    }

    /// <summary>Plays an effect xml at a place: the nif parts show for their duration; sound events play at the place; soundMusic parts follow CSoundMusicEffect's caster-kind gate (m_unk_18 &lt;= 1, default rejected for unknown/remote casters).
    /// A nif part out of the camera actor's view for <paramref name="casterKind"/> is not made (CNifEffect::vfunc_3), nor a
    /// sound event (CreateEffect, FieldAudio's test).</summary>
    public void PlayEffect(string xmlPath, Godot.Vector3 at, float yawRadians, int casterKind)
        => PlayEffect(xmlPath, at, new Basis(Godot.Vector3.Up, yawRadians), casterKind);

    /// <summary>The same turned by a full rotation (godot space), as an effect's NiTransform turns it.</summary>
    public void PlayEffect(string xmlPath, Godot.Vector3 at, Basis rotation, int casterKind)
        => Play(xmlPath, at, rotation, casterKind, null);

    /// <summary>
    /// An effect on an actor (CEffect SetActor, no transform of its own); nothing when the actor is gone. Each nif part goes
    /// where CNifEffect::vfunc_3's attach switch (nifeffectparam.cpp:717) puts it by its attach type (<see cref="EffectPart.AttachKind"/>):
    /// - type 0 (AttachToOwnerNode, :1044): under the world effect root, at the actor's parent node's world transform taken
    ///   once at the start (<see cref="WorldPlace"/>); it does not follow the actor and plays to its own end after the
    ///   actor is removed;
    /// - the others (type 1 Node :1079, 2 Mesh :1118, 3/4 weapon :1142): on the actor's named bone, worn mesh's parent or
    ///   weapon's Point01 node, through a SeparateNode for partial update bits (<see cref="EffectAttach"/>), so they follow
    ///   that node and go with the actor at once; one the client makes no target for is not made.
    /// Sound and light parts play at its place now.
    /// STILL-OPEN: vfunc_11 (:946) is skipped: on a stop, a type 1 part still inside its duration moves to the world root and
    /// plays on (unless the param's +0x30 flag is set). onGround is not applied to type 0 parts (EffectNode::UpdateWorldData,
    /// effectservice.cpp:971).
    /// </summary>
    public void PlayEffectOn(string xmlPath, Node3D actor, int casterKind)
    {
        if (actor == null || !GodotObject.IsInstanceValid(actor)) return;
        Play(xmlPath, actor.GlobalPosition, Basis.Identity, casterKind, actor);
        EffectsOnActors++;
    }

    /// <summary>Effects played on an actor (<see cref="PlayEffectOn"/>).</summary>
    public int EffectsOnActors;
    /// <summary>Attach type 1-4 nif parts the client makes no target for (<see cref="EffectAttach.Hang"/>), so not made.</summary>
    public int PartsNotHung;

    void Play(string xmlPath, Godot.Vector3 at, Basis rotation, int casterKind, Node3D actor)
    {
        if (!ShowEffects || string.IsNullOrEmpty(xmlPath)) return;
        var parts = effects.Get(xmlPath);
        if (parts == null) { EffectsNoXml++; MissingEffects.Add(xmlPath); return; }
        var owner = EffectOwners.Of(casterKind, actor);
        // the camera's actor for the nif parts' view test; kind 0 is never out of view (IsOutOfView, effectskipmgr.cpp:136)
        var eye = casterKind != 0 && Skip != null ? EffectView.Viewer : null;
        // a random part makes one child, picked once for this play (CRandomEffect::vfunc_3, randomeffectparam.cpp:143-178)
        foreach (var part in Ms2.Formats.World.EffectRandom.PlayList(parts, 0, Ms2.Formats.World.EffectRandom.Tick, playParts, owner))
        {
            if (part.SkippedFor(owner)) continue;   // CEffectParam::vfunc_7 (effectservice.cpp:245-259)
            if (FxTrace && part.RandomParent >= 0)
                GD.Print($"fx: random {xmlPath} part {part.RandomParent} -> {part.FilePath ?? part.SoundEvent ?? part.Type} (tick {Ms2.Formats.World.EffectRandom.Tick}) at {Time.GetTicksMsec()}");
            // a random part's child: the random part's own test at the child's place (effectservice.cpp:337-345)
            if (eye != null && part.RandomParent >= 0 && part.TypeId == Ms2.Formats.World.EffectSkipTable.TypeSoundEvent
                && Skip.RandomOutOfView(parts[part.RandomParent], casterKind, EffectView.Client(at), eye)) { PartsOutOfView++; continue; }
            if (part.Type == "soundEvent" && part.SoundEvent != null)
            {
                FieldAudio.Instance?.PlayEffectSound(part, owner, at, actor);   // its view test is FieldAudio's (SoundEventOutOfView)
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
            if (part.TypeId == Ms2.Formats.World.EffectSkipTable.TypeSwoosh)
            {
                if (actor == null || Swoosh(parts, part, actor, casterKind, eye)) EffectsNotNif++;
                continue;
            }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath)) { EffectsNotNif++; continue; }
            Transform3D place; Node3D host; bool own = true;
            if (actor == null) { place = new Transform3D(rotation, at); host = parent; }
            else if (part.AttachKind == 0) { place = WorldPlace(part, actor); host = parent; }
            else if (EffectAttach.Hang(part, actor, out var hung))
            {
                place = hung.Place; host = hung.Host; own = hung.OwnTransform;
                if (FxTrace) GD.Print($"fx: attach {part.AttachKind} {part.FilePath} on {hung.On} [{hung.Node}]{(host is Ui.SeparateNode3D ? " separate" : "")} at {Time.GetTicksMsec()}");
            }
            else { PartsNotHung++; continue; }
            if (FxTrace && actor != null)
                GD.Print($"fx: part {part.AttachKind} {part.FilePath} [{part.ParentNode}] starts at {actor.GlobalTransform.AffineInverse() * (host.GlobalTransform * place).Origin} on the actor");
            var ownAt = own ? G(part.Translate[0], part.Translate[1], part.Translate[2]) : Godot.Vector3.Zero;
            // CNifEffect::vfunc_3 (nifeffectparam.cpp:757-766): the part's root at its world place, row 1 at the part's
            // viewDistance for the effect's kind; out of view, the part is not made. No caller here sets the target's
            // no-cull flag (m_b30: AdditionalCli's keep effects and the kind 0 area effects only).
            if (eye != null)
            {
                var spot = EffectView.Client(host.GlobalTransform * place * ownAt);
                if (Skip.PartOutOfView(part, casterKind, spot, eye)
                    || part.RandomParent >= 0 && Skip.RandomOutOfView(parts[part.RandomParent], casterKind, spot, eye))
                { PartsOutOfView++; continue; }
            }
            if (Particles && TryScene(part, place, host, own)) continue;
            var e = Effect(part.FilePath);
            if (e == null) { EffectsNoNif++; MissingEffects.Add(part.FilePath); continue; }
            if (e.Value.particles) { EffectsWithParticles++; ParticleEffects.Add(part.FilePath); }
            if (e.Value.md.Meshes.Count == 0) continue;
            var root = mb.Build(e.Value.md, "Effect");
            float s = !own || part.Scale <= 0 ? 1 : part.Scale;
            root.Scale = new Godot.Vector3(s, s, s);
            var holder = new Node3D { Transform = place, Visible = part.StartDelay <= 0 };
            root.Position = ownAt;
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

    /// <summary>The swooshes made on actors (<see cref="SwooshEffect"/>).</summary>
    public int Swooshes;

    /// <summary>
    /// A swoosh part on <paramref name="actor"/> (CSwooshEffect; an effect at a place has no target node and makes none,
    /// swoosheffectparam.cpp:293-295). Once made, CreateEffect tests its place (vfunc_8: the actor's world point) on the
    /// swoosh row for the caster unless offSkip (effectservice.cpp:335-343). False when it was made.
    /// - <paramref name="casterKind"/> is the effect's kind (the target's m_nKind, copied to the effect's m_unk_18), the
    ///   kind both the view test and the frame-rate step drop read (vfunc_3, :303).
    /// - A loop swoosh lives until its actor goes: the hit path plays effects with no handle (magicdata.cpp:2770-2829), in
    ///   the client as here. The data puts none there (EffectStripSweepTests.NoLoopSwooshPlaysOnTheHitPath).
    /// </summary>
    internal bool Swoosh(IReadOnlyList<EffectPart> parts, EffectPart part, Node3D actor, int casterKind, System.Numerics.Vector3? eye)
    {
        if (eye != null && !part.OffSkip && Skip.IsOutOfView(EffectSkipTable.TypeSwoosh, casterKind, EffectView.Client(actor.GlobalPosition), eye))
        { PartsOutOfView++; return true; }
        if (SwooshEffect.Make(parts, part, actor, casterKind, mb, Scene) is not { } sw) return true;
        actor.AddChild(sw);
        Swooshes++; EffectsSpawned++;
        return false;
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
    internal Transform3D WorldPlace(EffectPart part, Node3D actor) => parent.GlobalTransform.AffineInverse() * ActorPlace(part, actor);

    /// <summary>The world transform an attach type 0 part of an effect on <paramref name="actor"/> is put at (<see cref="WorldPlace"/>).</summary>
    internal static Transform3D ActorPlace(EffectPart part, Node3D actor)
    {
        if (!part.ApplyNodeTransform) return Transform3D.Identity;
        var w = NodeWorld(actor, part.ParentNode);
        if (!(part.ApplyTranslate && part.ApplyRotation && part.ApplyScale))
        {
            float s = part.ApplyScale ? w.Basis.Scale.X : 1f;
            var rot = part.ApplyRotation ? new Basis(w.Basis.GetRotationQuaternion()) : Basis.Identity;
            w = new Transform3D(rot.Scaled(new Godot.Vector3(s, s, s)), part.ApplyTranslate ? w.Origin : Godot.Vector3.Zero);
        }
        return w;
    }

    /// <summary>The world transform (Y up) of the node <paramref name="name"/> under <paramref name="actor"/>'s model
    /// (GetObjectByName on m_hNode, :1048-1051: a bone or an effect dummy such as Eff_Body_World), else the model root's
    /// (:1052-1053); the actor's own when it has no model. Those nodes are in the nif's Z-up frame, so each takes
    /// <see cref="EffectAttach.YUp"/>, as the holder under them does.</summary>
    static Transform3D NodeWorld(Node3D actor, string name) =>
        EffectAttach.NodeOf(actor, name) is { } n ? n.GlobalTransform * EffectAttach.YUp : actor.GlobalTransform;

    /// <summary>Plays an effect part through the scene runtime (particles and controllers) at <paramref name="place"/> under
    /// <paramref name="host"/>; false when the nif could not be read as a scene.</summary>
    bool TryScene(EffectPart part, Transform3D place, Node3D host, bool own)
    {
        var sc = Scene(part.FilePath);
        if (sc == null) return false;
        float dur = part.Duration > 0 ? part.Duration : 0.6f;
        var inst = new EffectInstance(sc, mb, dur, (int)(GD.Randi() & 0x7fffffff)) { Transform = place, Delay = part.StartDelay };
        float s = part.Scale <= 0 ? 1 : part.Scale;
        var q = new Godot.Quaternion(Godot.Vector3.Back, Mathf.DegToRad(part.Rotation[2])) * new Godot.Quaternion(Godot.Vector3.Up, Mathf.DegToRad(part.Rotation[1])) * new Godot.Quaternion(Godot.Vector3.Right, Mathf.DegToRad(part.Rotation[0]));
        inst.FileSpace = own ? new Transform3D(new Basis(q).Scaled(new Godot.Vector3(s, s, s)), new Godot.Vector3(part.Translate[0], part.Translate[1], part.Translate[2])) : Transform3D.Identity;
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
