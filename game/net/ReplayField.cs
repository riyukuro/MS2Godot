using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Replay;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// Offline replay of a recorded session: loads the field the recording enters (the field scene and FieldBuilder), then plays the
/// recording through the real receive path (ReplayStream -> MapleChannel -> ClientSession -> FieldTracker, no sockets) and shows
/// the players: spawned from FieldAddUser, moved by UserSync / RideSync (and the recording's own movement packets for the local
/// player), removed by FieldRemoveUser, at the recorded pace. Every player is a stand-in body (male or female by the packet's gender).
/// Run: godot --path game res://net/Replay.tscn -- --recording FILE.msb [--speed 1] [--start SEC]
///      [--field XBLOCK] [--out DIR --shots 10,30,60] [--follow self|NAME|center|npcs]
/// </summary>
public partial class ReplayField : Field
{
    sealed class Actor
    {
        public Node3D Root, Visual; public AnimationPlayer Anim;
        public Vector3 Target; public float TargetYaw; public int Moves; public string Name; public bool IsSelf;
        public string Current = "";
        public byte Gender;
        public Vector3 LastPos; public float Speed; public int Life;
    }

    string recordingPath, outDir, followName = "self";
    double speed = 1, startOffset;
    readonly List<double> shotTimes = new();
    readonly List<string> shotFollow = new();
    int nextShot;
    MsbFile file;
    ReplaySession replay;
    ModelBuilder modelBuilder;
    EquipApplier equip;
    NpcLayer npcs;
    CombatLayer combat;
    LiveNameTags tags;
    SkillTable skills;
    readonly Dictionary<(byte, string), Godot.Animation> skillAnims = new();
    public int SkillUses, SkillNoRow, SkillNoKf, DamageNumbers, SkillAnimsPlayed;
    Ms2.Formats.World.NpcTable npcTable;
    Dictionary<int, List<List<System.Numerics.Vector3>>> npcTruth;
    bool applyEquips = true;
    readonly Dictionary<int, Actor> actors = new();
    readonly Dictionary<byte, (ModelData md, Godot.Animation idle, Godot.Animation walk, Godot.Animation run, string model)> bodies = new();
    int settle = -1, pendingShot = -1;
    Vector3 camFocus; bool camInit;
    float yaw = Mathf.DegToRad(200), pitch = Mathf.DegToRad(38), dist = 1000;
    // ground truth: per object id, the position after each move, from the recording's own packets
    Dictionary<int, List<List<System.Numerics.Vector3>>> truth;   // per object id, one list per life (an id can be added again after a remove)
    readonly Dictionary<int, int> lives = new();
    int checkedActors, mismatches;

    void ParseArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string v = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--recording": recordingPath = v; break;
                case "--speed": speed = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--start": startOffset = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--out": outDir = v; break;
                case "--follow": followName = v; break;
                case "--shots":     // SECONDS[:FOLLOW],... the recording time of each shot and whom the camera follows then
                    foreach (var t in v.Split(','))
                    {
                        var parts = t.Split(':', 2);
                        shotTimes.Add(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture));
                        shotFollow.Add(parts.Length > 1 ? parts[1] : null);
                    }
                    break;
                case "--no-equips": applyEquips = false; break;
                case "--dist": dist = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
            }
        }
    }

    protected override bool SpawnsLocalPlayer => false;

    protected override string FieldNameFor(string defaultName)
    {
        ParseArgs();
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--field") return args[i + 1];
        if (recordingPath == null) { GD.PushError("--recording FILE.msb is required"); return defaultName; }
        file = MsbFile.Read(recordingPath);
        int mapId = 0;
        foreach (var p in file.Packets)
            if (p.ServerToClient && p.Opcode == (ushort)Inbound.RequestFieldEnter)
            {
                var e = RequestFieldEnter.Read(new Maple2.PacketLib.Tools.ByteReader(p.Data));
                if (e.Error == 0) { mapId = e.MapId; break; }
            }
        if (mapId == 0) return defaultName;
        using var ps = new PackSet(Settings.DataDir);
        string prefix = $"mapxblock/{mapId:D8}_";
        foreach (var n in ps.List("Xml"))
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return n.Substring("mapxblock/".Length, n.Length - "mapxblock/".Length - 4);
        GD.PushWarning($"no xblock for map {mapId}, using {defaultName}");
        return defaultName;
    }

    protected override void OnFieldBuilt(ModelBuilder builder, FieldMap map, string fieldName)
    {
        if (file == null) return;
        modelBuilder = builder;
        // the field build searched map textures only; the character bodies need the character pack's own textures too
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        equip = new EquipApplier(packs, builder) { Palettes = new Look.ColorPalettes(packs) };
        npcTable = new Ms2.Formats.World.NpcTable(packs);
        npcs = new NpcLayer(packs, builder, npcTable, this) { SpawnAnimation = new Ms2.Formats.World.FieldSpawnPoints(map.Entities).Find };
        skills = SkillTable.For(packs);
        combat = new CombatLayer(packs, builder, new EffectTable(packs), this);
        if (System.Environment.GetEnvironmentVariable("MS2_NOPARTICLES") != null) combat.Particles = false;
        npcs.Combat = combat;
        AddChild(tags = new LiveNameTags(packs));
        npcs.Tags = tags;
        truth = BuildTruth(file);
        npcTruth = BuildNpcTruth(file, NpcLayoutOf);
        replay = new ReplaySession(file, speed, startOffset, NpcLayoutOf);
        GD.Print($"replay {System.IO.Path.GetFileName(file.Path)}: {file.Packets.Count} packets, {replay.Stream.Duration:F0}s, field {fieldName}, speed x{speed}");
        replay.Start();
    }

    NpcLayout NpcLayoutOf(int id) { var r = npcTable.Find(id); return r == null ? default : new NpcLayout(r.IsBoss, r.HasDamagedHpBarTag); }

    static Dictionary<int, List<List<System.Numerics.Vector3>>> BuildNpcTruth(MsbFile f, Func<int, NpcLayout> layout)
    {
        var t = new Dictionary<int, List<List<System.Numerics.Vector3>>>(); var npcOf = new Dictionary<int, int>();
        foreach (var p in f.Packets.Where(p => p.ServerToClient))
        {
            var r = new Maple2.PacketLib.Tools.ByteReader(p.Data);
            switch ((Inbound)p.Opcode)
            {
                case Inbound.RequestFieldEnter: if (RequestFieldEnter.Read(r).Error == 0) npcOf.Clear(); break;
                case Inbound.FieldAddNpc:
                {
                    var n = FieldAddNpc.Read(r, layout); npcOf[n.ObjectId] = n.NpcId;
                    if (!t.TryGetValue(n.ObjectId, out var ls)) t[n.ObjectId] = ls = new();
                    ls.Add(new List<System.Numerics.Vector3> { n.Position });
                    break;
                }
                case Inbound.FieldRemoveNpc: npcOf.Remove(NpcObjectId.Read(r).ObjectId); break;
                case Inbound.NpcControl:
                    foreach (var b in NpcControl.Read(r, id => npcOf.TryGetValue(id, out int nid) && layout(nid).IsBoss).Blocks)
                        if (npcOf.ContainsKey(b.ObjectId)) t[b.ObjectId][^1].Add(b.Position.ToVector3());
                    break;
            }
        }
        return t;
    }

    // The same events as the tracker, taken straight from the recorded packets, to check what arrives in the scene.
    static Dictionary<int, List<List<System.Numerics.Vector3>>> BuildTruth(MsbFile f)
    {
        var t = new Dictionary<int, List<List<System.Numerics.Vector3>>>(); int self = 0; var live = new HashSet<int>();
        foreach (var p in f.Packets)
        {
            var r = new Maple2.PacketLib.Tools.ByteReader(p.Data);
            if (p.ServerToClient)
                switch ((Inbound)p.Opcode)
                {
                    case Inbound.ServerEnter: self = ServerEnter.Read(r).ObjectId; break;
                    case Inbound.RequestFieldEnter: if (RequestFieldEnter.Read(r).Error == 0) live.Clear(); break;
                    case Inbound.FieldAddUser: { var u = FieldAddUser.Read(r); live.Add(u.ObjectId); if (!t.TryGetValue(u.ObjectId, out var ls)) t[u.ObjectId] = ls = new(); ls.Add(new List<System.Numerics.Vector3> { u.Position }); break; }
                    case Inbound.FieldRemoveUser: live.Remove(FieldRemoveUser.Read(r).ObjectId); break;
                    case Inbound.UserSync: case Inbound.RideSync:
                    {
                        var s = UserSyncIn.Read(r);
                        if (live.Contains(s.ObjectId)) foreach (var rec in s.Records) t[s.ObjectId][^1].Add(rec.Position.ToVector3());
                        break;
                    }
                }
            else if (p.Opcode == (ushort)Outbound.UserSync && self != 0 && live.Contains(self))
                foreach (var (rec, _) in UserSyncOut.Read(r).Records) t[self][^1].Add(rec.Position.ToVector3());
        }
        return t;
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    (ModelData md, Godot.Animation idle, Godot.Animation walk, Godot.Animation run, string model) Body(byte gender)
    {
        if (bodies.TryGetValue(gender, out var b)) return b;
        string dir = gender == 0 ? "male" : "female", nif = gender == 0 ? "m_body.nif" : "f_body.nif";
        var md = modelBuilder.LoadModel($"{dir}/{nif}");
        b = (md, ModelBuilder.BuildAnimation(modelBuilder.LoadKf($"{dir}/idle_a.kf"), md), ModelBuilder.BuildAnimation(modelBuilder.LoadKf($"{dir}/walk_a.kf"), md),
             ModelBuilder.BuildAnimation(modelBuilder.LoadKf($"{dir}/run_a.kf"), md), dir);
        return bodies[gender] = b;
    }

    Actor Spawn(UserAddedEvent e)
    {
        var (md, idle, walk, run, model) = Body(e.User.User.Gender);
        var visual = modelBuilder.Build(md, e.User.User.Gender == 0 ? "Male" : "Female");
        if (applyEquips) equip.Apply(visual, md, e.User.User.Gender, e.User.Equips, e.User.Skin.Primary, e.User.Skin.Secondary);
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var lib = anim.GetAnimationLibrary("");
        lib.AddAnimation("idle", idle); lib.AddAnimation("walk", walk); lib.AddAnimation("run", run);
        var root = new Node3D { Name = $"Pc{e.User.ObjectId}" };
        root.AddChild(visual);
        AddChild(root);
        // MyPC::vfunc_49 for the recorded player, PC::vfunc_49 for the others (mypc.cpp:2879-2887, pc.cpp:3028-3035)
        FieldShadows.Character(root, visual, e.IsSelf ? Ms2.Formats.Options.GraphicOptionTable.MyPC : Ms2.Formats.Options.GraphicOptionTable.PC, 100f, 3f);
        tags.AddPc(root, visual.GetNodeOrNull<Node3D>("Model"), md.RootName, e.User, e.IsSelf);
        if (e.IsSelf) tags.Viewer = root;               // the fade is measured from the recorded player
        var a = new Actor { Gender = e.User.User.Gender, Root = root, Visual = visual, Anim = anim, Name = e.User.User.Name, IsSelf = e.IsSelf, Target = G(e.User.Position), TargetYaw = e.User.Rotation.Z };
        root.Position = a.Target; a.LastPos = a.Target;
        visual.Rotation = new Vector3(0, Mathf.DegToRad(a.TargetYaw), 0);
        anim.Play("idle"); a.Current = "idle";
        return a;
    }

    // ---- combat ----

    (Node3D root, float height, float yaw)? Find(int id)
    {
        if (actors.TryGetValue(id, out var a)) return (a.Root, 135, a.Visual.Rotation.Y);
        return npcs.Info(id);
    }

    Godot.Animation SkillAnim(byte gender, string seq)
    {
        if (skillAnims.TryGetValue((gender, seq), out var an)) return an;
        an = null;
        string dir = gender == 0 ? "male" : "female", entry = $"{dir}/{seq.ToLowerInvariant()}.kf";
        try
        {
            if (packs.Exists(ModelBuilder.ModelArchive, entry))
            {
                an = ModelBuilder.BuildAnimation(modelBuilder.LoadKf(entry), Body(gender).md);
                an.LoopMode = Godot.Animation.LoopModeEnum.None;
            }
        }
        catch (Exception ex) { GD.PushWarning($"skill kf {entry}: {ex.Message}"); }
        return skillAnims[(gender, seq)] = an;
    }

    void OnSkillUse(SkillUse u)
    {
        SkillUses++;
        var motion = skills.Motion(u.SkillId, u.Level, u.Motion);
        if (motion == null) { SkillNoRow++; return; }
        var who = Find(u.ActorId);
        if (who == null) return;
        if (!string.IsNullOrEmpty(motion.SequenceName))
        {
            if (actors.TryGetValue(u.ActorId, out var a))
            {
                var an = SkillAnim(a.Gender, motion.SequenceName);
                if (an == null) SkillNoKf++;
                else
                {
                    var lib = a.Anim.GetAnimationLibrary("");
                    if (lib.HasAnimation("skill")) lib.RemoveAnimation("skill");
                    lib.AddAnimation("skill", an);
                    a.Anim.Play("skill", 0.05, (float)Math.Max(u.Speed, 0.1));
                    a.Current = "skill"; SkillAnimsPlayed++;
                }
            }
            else npcs.PlaySequence(u.ActorId, motion.SequenceName);
        }
        PlayFx(motion.MotionEffect, who.Value.root.Position, who.Value.yaw, CasterKindOf(u.ActorId));
    }

    /// <summary>
    /// Effect target kind for an actor id per WorldService::GetObjectKind (@0x141bd1a10, worldservice_1.cpp:6304-6309):
    /// StateSkill (stateskill.cpp:541,757,824,856,1834,2367,2383) and CMagic hit effects (magicdata.cpp:2797,2805) pass
    /// MyPC::vfunc_58 = 1 (@0x141522cc0, mypc.cpp:1247) for the local player and PCBase::vfunc_58 = 3 (pcbase.cpp:1092)
    /// for remote PCs. STILL-OPEN: a replay actor that is an npc takes 3 here, where CNpc::vfunc_58 gives 5 (6 for a boss).
    /// </summary>
    int CasterKindOf(int actorId) =>
        Ms2.Audio.SoundMusicEffectState.CasterKindForActor(actors.TryGetValue(actorId, out var a) && a.IsSelf);

    void PlayFx(string id, Vector3 at, float yaw, int casterKind)
    {
        int before = combat.ParticleSystemsRun;
        combat.PlayEffect(id, at, yaw, casterKind);
        if (System.Environment.GetEnvironmentVariable("MS2_FXLOG") != null && combat.ParticleSystemsRun > before) GD.Print($"fx {id} at {replay.Stream.PlayedSeconds:F1}s systems +{combat.ParticleSystemsRun - before}");
    }

    void OnDamage(SkillDamage d)
    {
        var motion = d.SkillId != 0 ? skills.Motion(d.SkillId, d.Level, d.Motion) : null;
        int casterKind = CasterKindOf(d.OwnerId);
        bool hitFx = false;
        foreach (var t in d.Targets)
        {
            var who = Find(t.TargetId);
            if (who == null) continue;
            foreach (var h in t.Hits) { combat.Number(who.Value.root.Position + new Vector3(0, who.Value.height + 40, 0), h.Damage, h.Flag); DamageNumbers++; }
            if (!hitFx && motion != null && motion.HitEffects.Count > 0) { PlayFx(motion.HitEffects[0], who.Value.root.Position + new Vector3(0, who.Value.height * 0.5f, 0), 0, casterKind); }
        }
    }

    void Drain()
    {
        int budget = 400;                                   // bound the work per frame
        while (budget-- > 0 && replay.Tracker.Events.TryDequeue(out var ev))
        {
            switch (ev)
            {
                case UserAddedEvent add:
                    if (actors.TryGetValue(add.User.ObjectId, out var old)) { tags.Remove(old.Root); old.Root.QueueFree(); }
                    { var sp = Spawn(add); sp.Life = lives[add.User.ObjectId] = lives.GetValueOrDefault(add.User.ObjectId, -1) + 1; actors[add.User.ObjectId] = sp; }
                    break;
                case UserRemovedEvent rem:
                    if (actors.Remove(rem.ObjectId, out var gone)) { tags.Remove(gone.Root); gone.Root.QueueFree(); }
                    break;
                case SkillUsedEvent su: OnSkillUse(su.Use); break;
                case SkillCancelledEvent sc:
                    if (actors.TryGetValue(sc.ActorId, out var cancelled) && cancelled.Current == "skill") { cancelled.Anim.Stop(); cancelled.Current = ""; }
                    break;
                case SkillDamageEvent sd: OnDamage(sd.Damage); break;
                case NpcHpEvent hp: npcs.SetHp(hp.ObjectId, hp.Current, hp.Max); break;
                case NpcAddedEvent na: npcs.Add(na); break;
                case NpcRemovedEvent nr: npcs.Remove(nr.ObjectId, nr.Dead); break;
                case NpcMovedEvent nm: npcs.Move(nm); break;
                case UserMovedEvent mv:
                    if (!actors.TryGetValue(mv.ObjectId, out var a)) break;
                    a.Target = G(mv.Position); a.TargetYaw = mv.YawDegrees; a.Moves++;
                    break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (replay == null) return;
        float dt = (float)delta;
        Drain();
        foreach (var a in actors.Values)
        {
            // glide toward the latest recorded position (records come every ~50-100 ms); snap when far (teleport, field change)
            var d = a.Target - a.Root.Position;
            if (d.Length() > 400) a.Root.Position = a.Target;
            else a.Root.Position += d * FrameRate.Ease(12f / FrameRate.Reference, dt);   // the share dt * 12 gave at 60 fps, per second at any rate
            float want = Mathf.DegToRad(a.TargetYaw);
            float cur = a.Visual.Rotation.Y;
            a.Visual.Rotation = new Vector3(0, Mathf.LerpAngle(cur, want, FrameRate.Ease(14f / FrameRate.Reference, dt)), 0);
            float sp = (a.Root.Position - a.LastPos).Length() / Mathf.Max(dt, 1e-4f);
            a.Speed = Mathf.Lerp(a.Speed, sp, FrameRate.Ease(8f / FrameRate.Reference, dt));
            a.LastPos = a.Root.Position;
            if (a.Current == "skill")
            {
                if (a.Anim.IsPlaying()) continue;      // a skill motion runs to its end, then idle/walk/run take over again
                a.Current = "";
            }
            string animName = a.Speed < 25 ? "idle" : a.Speed < 220 ? "walk" : "run";
            if (animName != a.Current) { a.Anim.Play(animName, 0.15); a.Current = animName; }
        }
        npcs.Process(dt);
        combat.Process(dt);
        UpdateReplayCamera(dt);
        HandleShots();
        if (replay.Completion.IsCompleted && replay.Tracker.Events.IsEmpty && pendingShot < 0 && (shotTimes.Count == 0 || nextShot >= shotTimes.Count) && outDir != null)
        {
            ReportChecks(); ReportChecks3(); ReportChecks2();
            GetTree().Quit(mismatches == 0 ? 0 : 3);
        }
    }

    void UpdateReplayCamera(float dt)
    {
        Actor target = null;
        if (followName == "self") target = actors.Values.FirstOrDefault(a => a.IsSelf);
        else if (followName != "center" && followName != "npcs") target = actors.Values.FirstOrDefault(a => a.Name == followName);
        Vector3 focus;
        if (followName == "npcs" && npcs.ActivePosition() is Vector3 np) focus = np;
        else if (target != null) focus = target.Root.Position;
        else if (actors.Count > 0) focus = actors.Values.Aggregate(Vector3.Zero, (s, a) => s + a.Root.Position) / actors.Count;
        else return;
        focus += new Vector3(0, 60, 0);
        camFocus = camInit ? camFocus.Lerp(focus, FrameRate.Ease(4f / FrameRate.Reference, dt)) : focus; camInit = true;
        var back = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        camera.LookAtFromPosition(camFocus + back * dist, camFocus, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Right) != 0)
        {
            yaw -= mm.Relative.X * 0.005f;
            pitch = Mathf.Clamp(pitch + mm.Relative.Y * 0.005f, Mathf.DegToRad(10), Mathf.DegToRad(80));
        }
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) dist = Mathf.Max(200, dist * 0.9f);
            if (mb.ButtonIndex == MouseButton.WheelDown) dist = Mathf.Min(6000, dist * 1.1f);
        }
    }

    public override void _PhysicsProcess(double delta) { }

    void HandleShots()
    {
        if (outDir == null || shotTimes.Count == 0) return;
        if (pendingShot >= 0)
        {
            if (--settle > 0) return;
            Check($"shot {pendingShot}");
            DirAccess.MakeDirRecursiveAbsolute(outDir);
            var path = System.IO.Path.Combine(outDir, $"replay_{pendingShot}_{shotTimes[pendingShot]:F0}s.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"shot {path} at {replay.Stream.PlayedSeconds:F1}s, {actors.Count} players");
            pendingShot = -1; nextShot++;
            return;
        }
        if (nextShot < shotTimes.Count && replay.Stream.PlayedSeconds >= shotTimes[nextShot] - startOffset + startOffset && replay.Tracker.Events.IsEmpty)
        {
            pendingShot = nextShot; settle = 4;      // let the glide and the frame settle
            if (shotFollow[nextShot] != null) { followName = shotFollow[nextShot]; camInit = false; }
        }
    }

    // Each player's last applied position must be the recorded one after the same number of moves, and the body must have reached it.
    void Check(string when)
    {
        int n = 0, bad = 0; float maxLag = 0;
        foreach (var (id, a) in actors)
        {
            if (!truth.TryGetValue(id, out var allLives) || a.Life >= allLives.Count) { bad++; continue; }
            var list = allLives[a.Life];
            n++;
            // moves applied so far: index into the recorded positions (index 0 is the add position)
            var expect = G(list[Math.Min(a.Moves, list.Count - 1)]);
            if ((expect - a.Target).Length() > 0.01f) { bad++; GD.PrintErr($"MISMATCH {a.Name}: expected {expect} got {a.Target} after {a.Moves} moves, truth has {list.Count}; actual target is truth index {list.FindIndex(q => (G(q) - a.Target).Length() < 0.5f)}; neighbours {string.Join(" ", Enumerable.Range(Math.Max(0, a.Moves - 2), 5).Where(i => i < list.Count).Select(i => G(list[i]).ToString()))}"); }
            maxLag = Mathf.Max(maxLag, (a.Root.Position - a.Target).Length());
        }
        var (nn, nbad) = npcs.Verify(npcTruth, m => GD.PrintErr(m));
        GD.Print($"check {when}: {nn} npcs, {nbad} position mismatches");
        n += nn; bad += nbad;
        checkedActors += n; mismatches += bad;
        GD.Print($"check {when}: {n} players, {bad} position mismatches against the recorded coordinates, bodies trail their target by at most {maxLag:F1} units");
    }

    void ReportChecks() => GD.Print($"combat: {SkillUses} skill uses ({SkillNoRow} without a skill row, {SkillAnimsPlayed} player motions played, {SkillNoKf} without a kf), {DamageNumbers} damage numbers, {npcs.Deaths} npc deaths ({npcs.NoDeathSequence} without a death sequence); effects: {combat.EffectsSpawned} parts shown, {combat.EffectsNoXml} xml missing, {combat.EffectsNoNif} nif missing, {combat.EffectsNotNif} non-nif parts skipped, {combat.EffectsWithParticles} with particle systems ({combat.ParticleEffects.Count} distinct), {combat.SceneEffects} played as scenes ({combat.ParticleSystemsRun} particle systems, {combat.SceneFallbacks} scene fallbacks, unsupported: {string.Join(",", combat.Unsupported)}), {combat.EffectsFailed} failed to load");
    void ReportChecks3() => GD.Print($"npcs: {npcs.Spawned} spawned ({npcs.NoRow} without a table row, {npcs.NoModel} without a model in the local data: {string.Join(", ", npcs.MissingModels.Take(8))}), {npcs.SequencesPlayed} sequences played, {npcs.SequencesMissing} sequence ids without a kf; equips: {equip.Loaded} item models applied, {equip.NoModelEntry} items without an itemmodel entry, {equip.NoAsset} without a usable asset, {equip.Failed} failed; {equip.MissingIds.Count} distinct ids missing from the local data");
    void ReportChecks2() => GD.Print($"replay done: {checkedActors} player checks, {mismatches} mismatches");
}
