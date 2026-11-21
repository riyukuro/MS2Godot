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
/// With MS2_SHOT_DIR set and a window, saves dead.png and revival.png. Quits with 0 when all hold, else 1.
/// </summary>
public partial class PlayerDeathCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    readonly List<Ms2.Net.Packets.StateSync> sent = new();
    Player pc;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;
    static readonly string ShotDir = System.Environment.GetEnvironmentVariable("MS2_SHOT_DIR");

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
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
                case 30: Shot("dead.png"); Revival(); break;
                case 60: Shot("revival.png"); AfterRevival(); InPlace(); FlagHold(); Finish(); break;
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
        GD.Print($"player death: dead '{dead}' held at {t:0.##} s, {sent.Count} sync records");
    }

    void Revival()
    {
        string rev = pc.Anims.Name(Act.Revival);
        Expect(rev != null, "the body has no REVIVAL sequence");
        pc.EnterRevival();
        Expect(pc.State == Player.MoveState.Revival && pc.CurrentAnim == rev, $"revival: {pc.State} '{pc.CurrentAnim}' (REVIVAL '{rev}')");
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
