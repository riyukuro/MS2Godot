using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/attach_release.tscn, game data) of a skill state's kept effects on another player when the
/// state ends, through the field's real effect service (RemoteLayer on RegionSkills) and the remote's own release
/// (RemotePcStates.ReleaseEffect, StateSkill::vfunc_5's FUN_1411a69e0 calls, stateskill.cpp:521-524):
/// - Flame Wave's cast effect (an attachToNode nif part, 1.5 s) released inside its duration leaves the actor: CNifEffect::
///   vfunc_11 (nifeffectparam.cpp:946-967) puts it under the world effect root at its world transform then, so it stays
///   where it was when the actor moves on;
/// - the same effect let go of as an insert (InsertEffect keeps no id, so nothing releases it) stays on the actor;
/// - Fire Tornado's cast effect (attachToNode false, type 0) is in the world from its start and does not move either way;
/// - a part past its duration is gone and the release does nothing more;
/// - the split keeps the part's whole world transform (the actor turned and scaled first), it stays put when the actor turns
///   and moves on, and it ends at its own duration;
/// - the real exit: the other player's cast (RemotePcStates.Cast keeping the motion effect, AnimActivated) left for a walk
///   splits the part (SkillTagEffects.Handles.End's release); left for StateHit (a hard exit, stateskill.cpp:515-524) the
///   effect stops at once and nothing is left;
/// - a looping effect (Jet Boots, attach type 1 loop parts) released by the state stops whole (vfunc_11 returns 0,
///   FUN_1411a6060), its type 0 part with it;
/// - the own player's cast (HudScene's KeepOwnEffect / ReleaseOwnEffect / InsertOwnEffect through the live session): the
///   state's release leaves the Flame Wave part in the world as well, and the motion's insert (the end effect, the loop
///   keep's end) stays on the player.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class AttachReleaseCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const string FlameWave = "CH/Skill/Wizard/Eff_Wizard_FlameWave_Cast_01.xml";
    const string FireTornado = "CH/Skill/Wizard/Eff_Wizard_FireTornado_Cast_01.xml";
    const string JetBoots = "CH/Skill/HeavyGunner/Eff_HeavyGunner_JetBoots_Cast_01.xml";
    const int Other = Ms2.Audio.SoundMusicEffectState.CasterOtherPc;
    static readonly Vector3 Step = new(5, 0, 3);
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    RegionSkills fx;
    IDictionary kept;
    Node3D owner, root;
    RemotePcStates states;

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        fx = new RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new RemoteLayer(field, builder, 1);
        kept = (IDictionary)typeof(RegionSkills).GetField("kept", Private).GetValue(fx);
        var effects = new Ms2.Formats.World.EffectTable(packs);
        Check(KindOf(effects, FlameWave) == 1 && DurationOf(effects, FlameWave) > 0, $"{FlameWave}: its nif part is not an attach type 1 part with a duration");
        Check(KindOf(effects, FireTornado) == 0, $"{FireTornado}: its nif part is not an attach type 0 part");

        var user = new FieldAddUser { ObjectId = 5, User = new UserBlock { Name = "r5", CharacterId = 105, Gender = 1 } };
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, user, false));
        layer._Process(0);
        var users = (IDictionary)typeof(RemoteLayer).GetField("users", Private).GetValue(layer);
        var r = users.Contains(5) ? users[5] : null;
        owner = r != null ? (Node3D)r.GetType().GetField("Visual").GetValue(r) : null;
        root = r != null ? (Node3D)r.GetType().GetField("Root").GetValue(r) : null;
        states = r != null ? ((RemotePc)r.GetType().GetField("Pc").GetValue(r))?.States : null;
        Check(owner != null && root != null && states?.ReleaseEffect != null, "no remote body, root or release for the other player");
        if (owner != null) { var at = EffectView.Client(owner.GlobalPosition); fx.ViewerOf = () => at; }
        if (fails.Count == 0) Run();
        if (fails.Count == 0) RunExits();
        if (fails.Count == 0) RunOwn(layer);

        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "attach release: ok" : $"attach release: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run()
    {
        // released by the skill state inside its duration: off the actor, where it was
        var (h, part, before) = Spawn(FlameWave);
        Check(part != null && part.GetParent() != fx, $"Flame Wave's part was not made on the actor (handle {h})");
        states.ReleaseEffect(h);
        var released = Move(part);
        Check(part != null && GodotObject.IsInstanceValid(part) && part.GetParent() == fx, "the released Flame Wave part is not under the world effect root");
        Check(part != null && released.DistanceTo(before) < 0.01f, $"the released Flame Wave part moved with the actor: {before} -> {released}");
        Check(!kept.Contains(h), "the release left the handle kept");

        // let go of as an insert: it stays on the actor and follows it
        var (hi, inserted, at) = Spawn(FlameWave);
        fx.Release(hi);
        var followed = Move(inserted);
        Check(inserted != null && inserted.GetParent() != fx && followed.DistanceTo(at + Step) < 0.01f,
            $"the inserted Flame Wave part did not stay on the actor: {at} -> {followed}");

        // type 0: in the world from the start, kept or released
        var (ht, tornado, start) = Spawn(FireTornado);
        Check(tornado != null && tornado.GetParent() == fx, "Fire Tornado's type 0 part is not in the world");
        var kept0 = Move(tornado);
        states.ReleaseEffect(ht);
        var released0 = Move(tornado);
        Check(tornado != null && kept0.DistanceTo(start) < 0.01f && released0.DistanceTo(start) < 0.01f,
            $"Fire Tornado's type 0 part moved: {start} -> {kept0} -> {released0}");

        // past its duration the part is gone; the release only lets the handle go
        var (hl, late, _) = Spawn(FlameWave);
        if (late != null) for (int i = 0; i < 20; i++) late._Process(0.1);
        Check(late == null || !GodotObject.IsInstanceValid(late) || late.IsQueuedForDeletion(), "Flame Wave's part outlived its 1.5 s");
        states.ReleaseEffect(hl);
        Check(!kept.Contains(hl), "the late release left the handle kept");
        GD.Print($"attach release: released part stayed at {released} (actor moved by {Step}), inserted part followed to {followed}, type 0 stayed at {released0}");
    }

    /// <summary>The review's gaps: the split's whole transform and its end, the real state exits (soft and hard) through
    /// RemotePcStates, and a looping effect's release.</summary>
    void RunExits()
    {
        // a turned, scaled actor: the split part keeps its world transform, and later turns and moves do not touch it
        root.RotateY(0.6f);
        root.Scale = new Vector3(1.25f, 1.25f, 1.25f);
        var (h, part, _) = Spawn(FlameWave);
        var t0 = part?.GlobalTransform ?? default;
        states.ReleaseEffect(h);
        Check(part != null && part.GetParent() == fx && part.GlobalTransform.IsEqualApprox(t0),
            $"the split part's world transform changed at the split: {t0} -> {part?.GlobalTransform}");
        root.RotateY(0.9f);
        root.Scale = Vector3.One;
        Move(part);
        Check(part != null && part.GlobalTransform.IsEqualApprox(t0),
            $"the split part moved when the actor turned, scaled and moved on: {t0} -> {part?.GlobalTransform}");
        if (part != null) for (int i = 0; i < 20; i++) part._Process(0.1);
        Check(part == null || !GodotObject.IsInstanceValid(part) || part.IsQueuedForDeletion(), "the split Flame Wave part outlived its 1.5 s");

        // the real exit: the cast's motion effect kept by RemotePcStates.Cast, the state left for a walk. The stub player
        // came in with no hp, so it is dead (a hard exit for every state end): alive first
        states.SetDead(false);
        var fw = new Ms2.Net.Replay.SkillTagEffects.MotionEffects(FlameWave, null, null, null, null);
        var (hs, soft) = CastKept(fw);
        var softAt = soft?.GlobalPosition ?? default;
        states.ChangeState(Ms2.Net.Replay.RemotePcStates.StateWalk);
        var softAfter = Move(soft);
        Check(soft != null && soft.GetParent() == fx && softAfter.DistanceTo(softAt) < 0.01f && !kept.Contains(hs),
            $"the cast's Flame Wave part was not split when the state was left for a walk (handle {hs}): {softAt} -> {softAfter}");

        // a hard exit (StateHit): stopped at once, not split
        var (hh, hard) = CastKept(fw);
        states.ChangeState(Ms2.Net.Replay.RemotePcStates.StateHit);
        Check(hard != null && (!GodotObject.IsInstanceValid(hard) || hard.IsQueuedForDeletion()) && !kept.Contains(hh),
            $"the cast's Flame Wave part was not stopped by the exit to StateHit (handle {hh})");

        // a looping effect released by the state: the whole effect stops
        int hj = fx.SpawnOn(JetBoots, owner, Other);
        var jet = new List<Node3D>();
        if (hj != 0 && kept[hj] is List<Node3D> jn) jet.AddRange(jn);
        states.ReleaseEffect(hj);
        bool allStopped = jet.Count >= 2;
        foreach (var n in jet) if (GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion()) allStopped = false;
        Check(allStopped && !kept.Contains(hj), $"Jet Boots' {jet.Count} parts did not all stop at the state's release (handle {hj})");
        GD.Print($"attach release: split transform kept through a turn and scale, walk exit split at {softAfter}, hit exit stopped, Jet Boots' {jet.Count} parts stopped");
    }

    /// <summary>The other player casts with <paramref name="effects"/> as the motion effect; the handle it kept and its first part.</summary>
    (int Handle, EffectInstance Part) CastKept(Ms2.Net.Replay.SkillTagEffects.MotionEffects effects)
    {
        var before = new HashSet<int>();
        foreach (var k in kept.Keys) before.Add((int)k);
        states.Cast(10300021, false, default, 0, effects: effects);
        foreach (var k in kept.Keys)
        {
            int h = (int)k;
            if (before.Contains(h)) continue;
            if (kept[h] is List<Node3D> nodes)
                foreach (var n in nodes) if (n is EffectInstance e) return (h, e);
            return (h, null);
        }
        return (0, null);
    }

    /// <summary>The own player's path, on the same body: a HudScene (not in the tree) with the player and the live session
    /// set, its own keep, release and insert called as EndOwnTagEffects calls them.</summary>
    void RunOwn(RemoteLayer layer)
    {
        var session = new LiveSession();
        typeof(LiveSession).GetProperty("Instance")!.SetValue(null, session);
        typeof(LiveSession).GetField("remote", Private)!.SetValue(session, layer);
        var hud = new HudScene();
        var player = new Player { Visual = owner };
        typeof(HudScene).GetField("player", Private)!.SetValue(hud, player);
        var keep = (System.Func<string, int>)typeof(HudScene).GetProperty("KeepOwnEffect", Private)!.GetValue(hud);
        var release = (System.Action<int>)typeof(HudScene).GetProperty("ReleaseOwnEffect", Private)!.GetValue(hud);
        var insert = (System.Action<string>)typeof(HudScene).GetProperty("InsertOwnEffect", Private)!.GetValue(hud);

        int h = keep(FlameWave);
        EffectInstance part = null;
        if (h != 0 && kept[h] is List<Node3D> nodes)
            foreach (var n in nodes) if (n is EffectInstance e) { part = e; break; }
        var before = part?.GlobalPosition ?? default;
        release(h);
        var released = Move(part);
        Check(part != null && part.GetParent() == fx && released.DistanceTo(before) < 0.01f,
            $"the own released Flame Wave part is not left in the world: {before} -> {released}");

        var had = new HashSet<EffectInstance>(PartsOn(owner));
        insert(FlameWave);
        EffectInstance inserted = null;
        foreach (var e in PartsOn(owner)) if (!had.Contains(e)) { inserted = e; break; }
        var at = inserted?.GlobalPosition ?? default;
        var followed = Move(inserted);
        Check(inserted != null && followed.DistanceTo(at + Step) < 0.01f,
            $"the own inserted Flame Wave part did not stay on the player ({(inserted == null ? "not on the body" : $"{at} -> {followed}")})");
        GD.Print($"attach release: own released part stayed at {released}, own inserted part followed to {followed}");

        typeof(LiveSession).GetProperty("Instance")!.SetValue(null, null);
        hud.Free(); player.Free(); session.Free();
    }

    static List<EffectInstance> PartsOn(Node n, List<EffectInstance> into = null)
    {
        into ??= new();
        foreach (var c in n.GetChildren())
        {
            if (c is EffectInstance e && GodotObject.IsInstanceValid(e) && !e.IsQueuedForDeletion()) into.Add(e);
            PartsOn(c, into);
        }
        return into;
    }

    (int Handle, EffectInstance Part, Vector3 At) Spawn(string xml)
    {
        int h = fx.SpawnOn(xml, owner, Other);
        EffectInstance part = null;
        if (h != 0 && kept[h] is List<Node3D> nodes)
            foreach (var n in nodes) if (n is EffectInstance e) { part = e; break; }
        return (h, part, part?.GlobalPosition ?? default);
    }

    /// <summary>Moves the actor's root on by <see cref="Step"/> and gives the part's place after it.</summary>
    Vector3 Move(Node3D part)
    {
        root.GlobalPosition += Step;
        return part != null && GodotObject.IsInstanceValid(part) ? part.GlobalPosition : default;
    }

    static int KindOf(Ms2.Formats.World.EffectTable t, string xml)
    {
        var ps = t.Get(xml);
        if (ps != null) foreach (var p in ps) if (p.TopLevel && p.Type == "nif") return p.AttachKind;
        return -1;
    }

    static float DurationOf(Ms2.Formats.World.EffectTable t, string xml)
    {
        var ps = t.Get(xml);
        if (ps != null) foreach (var p in ps) if (p.TopLevel && p.Type == "nif") return p.Loop ? 0 : p.Duration;
        return 0;
    }
}
