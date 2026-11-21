using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Net;

namespace Ms2Godot.World;

/// <summary>
/// The field's region skill objects (WorldService m_3a0) and their effects, from OnRegionSkill 0x4C (@0x141720e50,
/// scenestategame.cpp:7718-7749):
/// <list type="bullet">
/// <item>mode 0: CRegionSkillObject::Init (@0x1416f5a20, regionskillobject.cpp:83-207) on the skill's level, motion 0
/// (GetLevelData @0x1407e90a0 is an exact level lookup), then AddRegionSkillObject (@0x141bc32d0);</item>
/// <item>mode 1: RemoveRegionSkillObject (@0x141bc33d0, worldservice_1.cpp:4180-4189): Reset, then the effects fade out over
/// RegionSkillFadeOutDuration;</item>
/// <item>mode 2: StopLoopEffects (@0x1416f6e40), which has nothing to stop here (see <see cref="RegionSkillObject"/>).</item>
/// </list>
/// The world tick (@0x141bcb830, worldservice_1.cpp:9661-9665) runs UpdateEffects with the server tick. The node goes with the
/// field (the field leave drops every object). Messages that came before the node (the session's log of the field) are
/// replayed on enter.
/// STILL-OPEN: CNifEffect's fade (vfunc_6, nifeffectparam.cpp:899) ramps the material alpha; here the effect is freed when the
/// fade time ends, without the ramp. The bAlways flag (EffectSkipMgr) has no port, so it does not change the spawn.
/// </summary>
public partial class RegionSkills : Node3D, IRegionSkillEffects
{
    const string EffectArchive = "Resource/Model/Effect";
    static readonly Dictionary<PackSet, SkillTable> skillTables = new();

    readonly PackSet packs;
    readonly ModelBuilder builder;
    readonly EffectTable table;
    readonly float fadeSeconds;
    readonly Dictionary<string, EffectScene> scenes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, List<Node3D>> kept = new();

    /// <summary>An effect xml's light part (CLightEffect, lighteffectparam.cpp:363-377): it adds its light at its start delay
    /// and the effect's end destroys it (FieldLights.Start / End), as CombatLayer's lights. The light entity is added at Init
    /// (lighteffectparam.cpp:382, AddEntity) and attached only after m_fStartDelay (:384, :401-411); whether the added but
    /// unattached entity already lights is STILL-OPEN, so here the light starts at the delay.</summary>
    sealed class LiveLight
    {
        public EffectLightParam Param; public Vec3 Pos; public float Born, Start, End; public FieldLight Light; public int Handle;
    }
    readonly List<LiveLight> lights = new();
    public readonly RegionSkillObjects Objects = new();
    int nextHandle = 1, seed;
    public int Spawned;

    public RegionSkills(PackSet packs, ModelBuilder builder)
    {
        this.packs = packs; this.builder = builder;
        table = new EffectTable(packs);
        fadeSeconds = RegionSkillObjects.FadeOutDuration(packs);
        Name = "RegionSkills";
    }

    SkillTable Skills => skillTables.TryGetValue(packs, out var t) ? t : skillTables[packs] = new SkillTable(packs);

    public override void _EnterTree()
    {
        if (LiveSession.Instance is not { } ls) return;
        foreach (var m in ls.RegionSkillLog) Apply(m);
        ls.RegionSkillReceived += Apply;
        foreach (var m in ls.OneTimeEffectLog) ApplyOneTime(m);
        ls.OneTimeEffectReceived += ApplyOneTime;
        ls.InvincibleChanged += OnInvincible;
        if (ls.Invincible.Active) OnInvincible(true);   // an invincible time that began before the field was built
    }

    public override void _ExitTree()
    {
        if (LiveSession.Instance is { } ls)
        {
            ls.RegionSkillReceived -= Apply;
            ls.OneTimeEffectReceived -= ApplyOneTime;
            ls.InvincibleChanged -= OnInvincible;
        }
        OneTime.Clear();
        invincibleHandle = 0;
        foreach (var l in lights) FieldLights.End(l.Light);
        lights.Clear();
    }

    public override void _Process(double delta)
    {
        if (Objects.Objects.Count > 0) Objects.Update(ServerTick, this);
        for (int i = lights.Count - 1; i >= 0; i--)   // only a start or an end touches the light data
        {
            var l = lights[i];
            l.Born += (float)delta;
            if (l.Light == null && l.Born >= l.Start && l.Born < l.End) l.Light = FieldLights.Start(l.Param, l.Pos);
            if (l.Born >= l.End) { FieldLights.End(l.Light); lights.RemoveAt(i); }
        }
    }

    static uint ServerTick => (uint)(LiveSession.Instance?.ServerTick ?? 0);

    /// <summary>CTriggerService's one-time effects of the field (OnOneTimeEffect 0xF1).</summary>
    public readonly OneTimeEffects OneTime = new();
    int invincibleHandle;

    void ApplyOneTime(OneTimeEffect m) => OneTime.Apply(m.Id, m.Add, m.Name, this);

    /// <summary>PC::FUN_141661370 of the own player (pc.cpp:3409-3428): a start spawns the common resource's
    /// Revival_Invincible on the player and keeps the handle in m_n1060 (a restart overwrites it, the old effect left to run);
    /// the end removes the kept one (FUN_1411a6060).</summary>
    void OnInvincible(bool start)
    {
        if (start)
        {
            string fx = GameTables.Shared(packs, GameOptions.StringLanguage).CommonResource("Revival_Invincible");
            invincibleHandle = SpawnOn(fx, LiveSession.Instance?.OwnPlayer);
            return;
        }
        if (invincibleHandle != 0) FadeOut(invincibleHandle, 0);
        invincibleHandle = 0;
    }

    /// <summary>One OnRegionSkill message.</summary>
    public void Apply(RegionSkillMessage m)
    {
        switch (m)
        {
            case RegionSkillMessage.Add a:
            {
                var pts = a.Points.Select(p => new Vec3(p.X, p.Y, p.Z)).ToArray();
                var o = RegionSkillObject.Init(a.Id, a.OwnerId, pts, Skills.RegionMotion(a.SkillId, a.Level), a.RotX, a.RotY,
                    (uint)a.StartTick, true, ServerTick, this);   // STILL-OPEN: m_bLocal (the optimizer's preload test), see RegionSkillObject
                if (o != null) Objects.Add(o);
                break;
            }
            case RegionSkillMessage.Remove r: Objects.Remove(r.Id, fadeSeconds, this); break;
        }
    }

    public bool Has(string effect) => table.Get(effect) != null;

    public int Spawn(string effect, Xform at, bool always, int randomIndex)
    {
        int h = nextHandle;
        var nodes = Play(effect, at, true, h);
        if (nodes == null) return 0;
        nextHandle++;
        kept[h] = nodes;
        return h;
    }

    public void Insert(string effect, Xform at, bool always, int randomIndex) => Play(effect, at, false, 0);

    /// <summary>FUN_1411a49c0 with an actor target (m_nKind 0, SetActor): a kept effect that follows the actor; its handle, or
    /// 0 when it did not spawn.
    /// STILL-OPEN: the light parts of such an effect stay where the actor was at the spawn.</summary>
    public int SpawnOn(string effect, Node3D actor)
    {
        if (actor == null || !IsInstanceValid(actor)) return 0;
        int h = nextHandle;
        var nodes = Play(effect, Identity, true, h, actor);
        if (nodes == null) return 0;
        nextHandle++;
        kept[h] = nodes;
        return h;
    }

    static readonly Xform Identity = new(new Vec3(0, 0, 0), new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 1);

    public void FadeOut(int handle, float seconds)
    {
        foreach (var l in lights)
            if (l.Handle == handle) l.End = Math.Min(l.End, l.Born + Math.Max(seconds, 0));
        if (!kept.Remove(handle, out var nodes)) return;
        foreach (var n in nodes)
        {
            if (!IsInstanceValid(n)) continue;
            if (seconds <= 0) { n.QueueFree(); continue; }
            GetTree().CreateTimer(seconds).Timeout += () => { if (IsInstanceValid(n)) n.QueueFree(); };
        }
    }

    /// <summary>The effect xml's parts at a Gamebryo world transform: nif parts through the effect scene runtime (startDelay,
    /// duration; a looping part stays until it is faded), light parts through FieldLights, sound events at the point. Null
    /// when the xml is missing.</summary>
    List<Node3D> Play(string xml, Xform at, bool keep, int handle, Node3D actor = null)
    {
        var parts = table.Get(xml);
        if (parts == null) return null;
        var nodes = new List<Node3D>();
        // an effect on an actor: the nif parts are its children (they follow it), at in the actor's own space; sounds and
        // lights take its place at the spawn
        Xform world = at;
        if (actor != null)
        {
            var g = actor.GlobalPosition;
            world = new Xform(new Vec3(g.X, -g.Z, g.Y), at.R, at.S);
        }
        foreach (var part in parts)
        {
            if (part.Type == "soundEvent" && part.SoundEvent != null)
            {
                FieldAudio.Instance?.PlayEventAt(part.SoundGroup == null ? part.SoundEvent : part.SoundGroup + "/" + part.SoundEvent,
                    FieldBuilder.ToGodotPoint(world.T), part.StartDelay);
                continue;
            }
            if (part.Type == "light" && part.Light != null)
            {
                var pos = world.Apply(new Vec3(part.Translate[0], part.Translate[1], part.Translate[2]));   // the part's place in the effect's space
                float dur = part.Loop ? 1e7f : part.Duration > 0 ? part.Duration : 0.6f;
                lights.Add(new LiveLight { Param = part.Light, Pos = pos, Start = part.StartDelay, End = part.StartDelay + dur, Handle = keep ? handle : 0 });
                continue;
            }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath) || Scene(part.FilePath) is not { } sc) continue;
            var local = new Xform(new Vec3(part.Translate[0], part.Translate[1], part.Translate[2]),
                DummyWorld.EulerXyz(part.Rotation[0], part.Rotation[1], part.Rotation[2]), part.Scale <= 0 ? 1 : part.Scale);
            float duration = part.Loop ? 1e7f : part.Duration;
            var fx = new EffectInstance(sc, builder, duration, seed++) { Name = "RegionSkill_" + seed, Delay = part.StartDelay };
            fx.FileSpace = ModelBuilder.ToGodot(at.Compose(local));
            (actor ?? (Node)this).AddChild(fx);
            if (keep) nodes.Add(fx);
            Spawned++;
        }
        return nodes;
    }

    EffectScene Scene(string nifPath)
    {
        string entry = nifPath.Replace('\\', '/').ToLowerInvariant();
        if (scenes.TryGetValue(entry, out var sc)) return sc;
        sc = null;
        try { if (packs.Exists(EffectArchive, entry)) sc = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(EffectArchive, entry))); }
        catch (Exception ex) { GD.PushWarning($"region skill effect {entry}: {ex.Message}"); }
        return scenes[entry] = sc;
    }
}
