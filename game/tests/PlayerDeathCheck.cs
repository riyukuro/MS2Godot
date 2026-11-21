using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/player_death.tscn) of the own player's StateDead (0xc) and StateRevival (0x18) on the female
/// body (PlayerDeath.cs):
/// - dead: DEAD plays, the move and jump keys do not move the body, the sync records after the queued walk record carry state 0xc, a repeat death
///   (ChangeState to the current state) does not restart the sequence;
/// - revival: REVIVAL plays, the keys still do not move it, its end goes to idle, then the keys walk again;
/// - the revive-in-place death plays MODEPVP_DEAD entry 1 (ShadowWorldAutoReviveDeadAction's default), and an entry past
///   the list's end plays nothing (PCActionList::Name gives ""); another user state then leaves the death state for idle;
/// - the dead flag alone (a player added dead): the body stays idle, the keys neither walk nor jump it, the death state still
///   enters (PC::vfunc_124); with the flag cleared the revival goes on to idle.
/// - the body: hidden by the normal death (PC::vfunc_23(1)), shown by the revival and the revive-in-place death;
/// - the tomb (PlayerTombstone): MS2TombStone and MS2TombStoneDark load with their kfm, play the initial sequence, a hit
///   plays the hit sequence and gives the bar's stage, it stays while the player is dead and is gone after the revival;
/// - the bar's movie (uitombgaugebar): the class tombGaugeBar builds with the labels start and hit0-hit4;
/// - the own death effects: dead_Start_MyPC, dead_Start_MyPC_PvP and EFFECT_RESOLVE_PANELTY name effect xmls with parts.
/// With MS2_SHOT_DIR set and a window, saves dead.png and revival.png. Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerDeathCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<Ms2.Net.Packets.StateSync> sent = new();
    Player pc;
    PlayerTombstone tomb;
    PackSet packs;
    ModelBuilder builder;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;
    static readonly string ShotDir = System.Environment.GetEnvironmentVariable("MS2_SHOT_DIR");

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } });
        sb.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4000, 100, 4000) } });
        AddChild(sb);
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);   // the field's own body (Field.SpawnPlayer)
        pc = Player.Create(visual, anim, faceGrids);
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.Live = true;
        pc.BoundsMin = new Vector3(-2000, -2000, -1000);
        pc.BoundsMax = new Vector3(2000, 2000, 1000);
        pc.StateSyncOut += list => { foreach (var (r, _) in list) sent.Add(r); };
        pc.GlobalPosition = new Vector3(0, 5, 0);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
        if (ShotDir != null)
        {
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0) });
            var cam = new Camera3D { Position = new Vector3(0, 220, 420) };
            AddChild(cam);
            cam.LookAt(new Vector3(0, 60, 0));
            cam.Current = true;
        }
    }

    void Steps(int n)
    {
        for (int i = 0; i < n; i++) { step.Invoke(pc, new object[] { (double)Dt }); pc.Anim.Advance(Dt); }
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    void Shot(string name)
    {
        if (ShotDir == null || DisplayServer.GetName() == "headless") return;
        RenderingServer.ForceDraw(false);   // a window the desktop does not show draws no frames by itself
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(ShotDir, name));
    }

    // the phases run a frame apart so a window draws the pose before its shot
    public override void _Process(double delta)
    {
        frames++;
        try
        {
            switch (frames)
            {
                case 5: Dead(); break;
                case 30: Shot("dead.png"); TombHeld(); Revival(); break;
                case 60: Shot("revival.png"); TombGone(); AfterRevival(); InPlace(); FlagHold(); Bar(); Effects(); Finish(); break;
            }
        }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); Finish(); }
    }

    void Dead()
    {
        Steps(30);
        pc.Input2 = new Vector2(0, 1);
        Steps(20);
        Expect(pc.State == Player.MoveState.Walk, $"walk before death: {pc.State}");
        string dead = pc.Anims.Name(Act.Dead);
        Expect(dead != null, "the body has no DEAD sequence");
        pc.EnterDead(false, 1);
        Expect(pc.State == Player.MoveState.Dead && pc.CurrentAnim == dead, $"dead: {pc.State} '{pc.CurrentAnim}' (DEAD '{dead}')");
        var at = pc.GlobalPosition;
        sent.Clear();
        pc.JumpHeld = true;
        Steps(60);
        pc.JumpHeld = false;
        Expect(Flat(pc.GlobalPosition, at) < 0.01f && pc.State == Player.MoveState.Dead, $"dead body moved {Flat(pc.GlobalPosition, at):0.##} ({pc.State})");
        // the walk record queued before the death is flushed first; every record from the first 0xc on is 0xc
        int first = sent.FindIndex(r => r.State == 0xc);
        bool sync = first >= 0 && sent.FindIndex(first, r => r.State != 0xc) < 0;
        Expect(sync, $"dead sync states: {string.Join(",", sent.ConvertAll(r => r.State))}");
        double t = pc.Anim.CurrentAnimationPosition;
        pc.EnterDead(false, 1);
        Expect(pc.Anim.CurrentAnimationPosition == t, "a repeat death restarted the sequence");
        Expect(!pc.Visual.Visible, "the body is shown while dead");
        GD.Print($"player death: dead '{dead}' held at {t:0.##} s, {sent.Count} sync records");
        Tomb();
    }

    bool InDeath() => pc.State == Player.MoveState.Dead;

    void Tomb()
    {
        var dark = PlayerTombstone.Build(this, packs, builder, pc, InDeath, true);
        Expect(dark != null && dark.Current == dark.State.Initial, $"dark tomb: {(dark == null ? "not built" : $"'{dark.Current}' (initial '{dark.State.Initial}')")}");
        dark?.QueueFree();
        tomb = PlayerTombstone.Build(this, packs, builder, pc, InDeath, false);
        if (tomb == null) { fails.Add("tomb not built"); return; }
        string initial = tomb.Current;
        Expect(!string.IsNullOrEmpty(initial) && initial == tomb.State.Initial, $"tomb initial: '{initial}' (state '{tomb.State.Initial}')");
        int? stage = tomb.Hit(1, 5);
        Expect(stage == 1 && tomb.State.State == Ms2.Ui.Hud.Tombstone.Hit, $"tomb hit 1 of 5: stage {stage}, state {tomb.State.State}");
        GD.Print($"player death: tomb initial '{initial}', dark '{dark?.Current}'");
    }

    void TombHeld()
    {
        Expect(tomb != null && IsInstanceValid(tomb) && !tomb.IsQueuedForDeletion(), "the tomb went while the player is dead");
        if (tomb == null || !IsInstanceValid(tomb)) return;
        Expect(!string.IsNullOrEmpty(tomb.Current), "the tomb plays nothing after the hit");
        GD.Print($"player death: tomb after the hit '{tomb.Current}', state {tomb.State.State}");
    }

    void TombGone() => Expect(tomb == null || !IsInstanceValid(tomb) || tomb.IsQueuedForDeletion(), "the tomb stayed after the revival");

    void Bar()
    {
        var movie = new Ms2Godot.Ui.UiMovie(Ms2Godot.World.FieldContents.Ui(packs), Ms2Godot.Ui.TombGaugeBar.Movie);
        var clip = movie.BuildClass(Ms2Godot.Ui.TombGaugeBar.Linkage, 1);
        if (clip == null) { fails.Add("tombGaugeBar not built"); return; }
        AddChild(clip);
        var missing = new List<string>();
        foreach (var l in new[] { "start", "hit0", "hit1", "hit2", "hit3", "hit4" }) if (!clip.HasLabel(l)) missing.Add(l);
        Expect(missing.Count == 0, $"tombGaugeBar labels missing: {string.Join(",", missing)} (has {string.Join(",", clip.Labels.Keys)})");
        GD.Print($"player death: tombGaugeBar labels {string.Join(",", clip.Labels.Keys)}");
        clip.QueueFree();
    }

    /// <summary>The own death, revive-in-place and revival-penalty-end effects (LiveSession's CommonResource keys) name effect
    /// xmls with parts.</summary>
    void Effects()
    {
        var tables = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        var fx = new Ms2.Formats.World.EffectTable(packs);
        var got = new List<string>();
        foreach (var key in new[] { Net.LiveSession.DeathEffectKey, Net.LiveSession.DeathEffectInPlaceKey, Net.LiveSession.RevivalPenaltyEndKey })
        {
            string path = tables.CommonResource(key);
            int parts = fx.Get(path)?.Count ?? -1;
            Expect(parts > 0, $"effect {key} '{path}': {(parts < 0 ? "no xml" : "no parts")}");
            got.Add($"{key}={path}({parts})");
        }
        GD.Print($"player death: effects {string.Join(" ", got)}");
    }

    void Revival()
    {
        string rev = pc.Anims.Name(Act.Revival);
        Expect(rev != null, "the body has no REVIVAL sequence");
        pc.EnterRevival();
        Expect(pc.State == Player.MoveState.Revival && pc.CurrentAnim == rev, $"revival: {pc.State} '{pc.CurrentAnim}' (REVIVAL '{rev}')");
        Expect(pc.Visual.Visible, "the body is hidden while reviving");
        var at = pc.GlobalPosition;
        Steps(10);
        Expect(Flat(pc.GlobalPosition, at) < 0.01f, $"reviving body moved {Flat(pc.GlobalPosition, at):0.##}");
        GD.Print($"player death: revival '{rev}' {pc.Anim.CurrentAnimationLength:0.##} s");
    }

    void AfterRevival()
    {
        pc.Input2 = Vector2.Zero;
        int n = 0;
        while (pc.State == Player.MoveState.Revival && n < 600) { Steps(1); n++; }
        Expect(pc.State == Player.MoveState.Idle, $"after revival: {pc.State} after {n} frames");
        var at = pc.GlobalPosition;
        pc.Input2 = new Vector2(0, 1);
        Steps(30);
        Expect(pc.State == Player.MoveState.Walk && Flat(pc.GlobalPosition, at) > 10, $"walk after revival: {pc.State}, {Flat(pc.GlobalPosition, at):0.#}");
        pc.Input2 = Vector2.Zero;
        Steps(30);
        GD.Print($"player death: idle {n * Dt:0.##} s after the revival's end step");
    }

    void InPlace()
    {
        string entry = pc.Anims.NameAt(Act.ModePvpDead, 1);
        pc.EnterDead(true, 1);
        Expect(entry == null || pc.CurrentAnim == entry, $"revive-in-place death: '{pc.CurrentAnim}' (MODEPVP_DEAD 1 '{entry}')");
        Expect(pc.Visual.Visible, "the body is hidden by the revive-in-place death");
        Expect(pc.Anims.NameAt(Act.ModePvpDead, 99) == null, "MODEPVP_DEAD entry 99 resolved");
        pc.LeaveDeath();        // another user state let in (OnUserState's vfunc_59)
        Expect(pc.State == Player.MoveState.Idle, $"user state after death: {pc.State}");
        pc.EnterRevival();
        GD.Print($"player death: revive-in-place entry 1 '{entry ?? "-"}'");
    }

    void FlagHold()
    {
        int n = 0;
        while (pc.State != Player.MoveState.Idle && n < 600) { Steps(1); n++; }
        pc.DeadFlag = true;
        var at = pc.GlobalPosition;
        pc.Input2 = new Vector2(0, 1);
        pc.JumpHeld = true;
        Steps(40);
        pc.JumpHeld = false;
        pc.Input2 = Vector2.Zero;
        Expect(pc.State == Player.MoveState.Idle && Flat(pc.GlobalPosition, at) < 0.01f, $"dead flag: {pc.State}, moved {Flat(pc.GlobalPosition, at):0.##}");
        pc.EnterDead(false, 1);
        Expect(pc.State == Player.MoveState.Dead, $"dead flag, death: {pc.State}");
        pc.DeadFlag = false;
        pc.EnterRevival();
        n = 0;
        while (pc.State == Player.MoveState.Revival && n < 600) { Steps(1); n++; }
        Expect(pc.State == Player.MoveState.Idle, $"dead flag cleared, after revival: {pc.State}");
        GD.Print($"player death: dead flag holds idle, death and revival pass");
    }

    void Finish()
    {
        SetProcess(false);
        GD.Print($"player death: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
