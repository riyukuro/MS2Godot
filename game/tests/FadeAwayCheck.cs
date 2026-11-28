using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Movement;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/fade_away.tscn) of the fade away: walk left, jump left, let go of left, then press right and
/// cast. MyPC::vfunc_53 (mypc.cpp:2942-2966) runs the state's update (StateJump::vfunc_3, statejump.cpp:175-214, steering by
/// the move direction the last frame stored) before its input step (vfunc_8), which casts, so the keys the cast aims by
/// (PCBase::vfunc_100 / vfunc_105, pcbase.cpp:2561-2604, 2812-2825) are read live while the jump still moves the old way. Here
/// the HUD's cast comes from an input event, before the player's step of the frame, the same order.
/// - same frame (right + cast): the body keeps the jump's left drift through the skill state (PCBase::vfunc_68,
///   pcbase.cpp:1783-1788: state 0x10 keeps x / y while jumping), the cast aims right and the body faces right at once
///   (StateSkill::vfunc_4, stateskill.cpp:446-448);
/// - one frame late (right, then the cast): the jump steered right first, so the drift goes right;
/// - one frame early (the cast, then right): the cast aims the way the body faces (left), the drift stays left.
/// The sync records of the same-frame fade away, deduplicated by (state, move flags, rotation), are walk (moving, left),
/// jump (moving, left), jump (9, left), skill 0x10 (9, right), idle (9, right): the retail order. A remote fed those records
/// and the cast keeps the left drift through its skill state too. An arc motion or magicControl cast in the air takes no
/// drift (no controller step).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class FadeAwayCheck : Node3D
{
    readonly NxController.FaceGrids faceGrids = new();
    readonly List<string> fails = new();
    PackSet packs;
    ModelBuilder builder;
    MethodInfo step;
    int frames;
    const float Dt = 1f / 60;
    const string CastSeq = "attack_01_a";
    static readonly Vector2 Left = new(-1, 0), Right = new(1, 0);
    const float WallRow = 2250f;
    readonly List<((int, int, int) Key, System.Numerics.Vector3 Pos)> cubes = new();

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var sb = new StaticBody3D { Position = new Vector3(0, -50, 0) };
        sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8000, 100, 8000) } });
        AddChild(sb);
        step = typeof(Player).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
        // a wall of cubes at MS2 x 0, three high and three wide around the godot row z WallRow (NoClimbInTheSkill): each cube
        // fills 150 around its position and 150 up from it (CubeGrid), and every player gets the cells
        foreach (int dy in new[] { -1, 0, 1 })
            for (int level = 0; level < 3; level++)
            {
                var ms2 = new Vector3(0, -WallRow + dy * MoveConfig.CubeSize, level * MoveConfig.CubeSize);
                var wall = new StaticBody3D { Position = new Vector3(ms2.X, ms2.Z + MoveConfig.CubeSize / 2, -ms2.Y) };
                wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * MoveConfig.CubeSize } });
                AddChild(wall);
                cubes.Add((CubeGrid.Key(ms2), new System.Numerics.Vector3(ms2.X, ms2.Y, ms2.Z)));
            }
    }

    sealed class Run
    {
        public Player Pc;
        public readonly List<(StateSync Rec, int Frame, int Made)> Sent = new();   // the frame it went out, the frame it was made
        public int Frame, CastFrame, JumpFrame;
        public float AimRotZ, FacingAfterCast;
        public System.Numerics.Vector3 CastPos;
        public float DriftX;   // godot x moved over the skill's first frames in the air
    }

    Run Make(Vector3 at)
    {
        var (visual, anim, md, bodyDir) = Ms2Godot.Look.PlayerLook.Build(builder, packs);
        var pc = Player.Create(visual, anim, faceGrids);
        pc.Gender = bodyDir == "male" ? 0 : 1;
        pc.Anims = ActionAnims.Load(packs, builder, md, anim, bodyDir);
        AddChild(pc);
        pc.SetProcess(false);
        pc.SetPhysicsProcess(false);
        pc.ScriptDriven = true;
        pc.Live = true;
        pc.CameraYaw = 0;                              // camera forward godot -z: right is +x
        // the record's camera field for the same camera (eye behind and above, looking along MS2 +y = godot -z), which
        // a remote turns into its move axes (PCBase::vfunc_67, pcbase.cpp:1310-1340)
        var (ex, ey, ez) = Ms2.Net.CameraMath.Euler(0, -600, 400, 0, 0, 0);
        pc.CameraEuler = new Vector3(ex, ey, ez);
        foreach (var (key, pos) in cubes) pc.Cubes.Cells.AddMapCube(key, pos);
        pc.BoundsMin = new Vector3(-4000, -4000, -1000);
        pc.BoundsMax = new Vector3(4000, 4000, 1000);
        pc.GlobalPosition = at;
        var run = new Run { Pc = pc };
        pc.ServerTick = () => run.Frame;                // the record's tick (mypc.cpp:2332): here the frame it was made on
        pc.StateSyncOut += list => { foreach (var (r, tick) in list) run.Sent.Add((r, run.Frame, tick)); };
        return run;
    }

    void Frame(Run r, int n = 1)
    {
        for (int i = 0; i < n; i++)
        {
            r.Frame++;
            step.Invoke(r.Pc, new object[] { (double)Dt });
            r.Pc.Anim.Advance(Dt);
        }
    }

    void StepDt(Run r, float dt)
    {
        r.Frame++;
        step.Invoke(r.Pc, new object[] { (double)dt });
        r.Pc.Anim.Advance(dt);
    }

    // the HUD's cast (CastKey -> AimOwn -> PlaySkillSequence) as the input event gives it, before the frame's step
    void Cast(Run r, CastMove move = default)
    {
        var k = r.Pc.CastAimDirection();
        float face = Norm(r.Pc.FacingRotZ);
        r.AimRotZ = Ms2.Ui.Skills.SkillCast.AimRotZ(face, new System.Numerics.Vector3(k.X, -k.Z, 0));
        var g = r.Pc.GlobalPosition;
        r.CastPos = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        r.CastFrame = r.Frame + 1;
        if (!r.Pc.PlaySkillSequence(CastSeq, 1f, move, r.AimRotZ)) fails.Add($"no {CastSeq} sequence on the body");
        r.FacingAfterCast = Norm(r.Pc.FacingRotZ);
    }

    static float Norm(float z) => ((z % 360f) + 360f) % 360f;

    // walk left, jump left, let go; then the cast and the right key by the timing (-1 early, 0 same frame, +1 late)
    Run FadeAway(int timing, Vector3 at, CastMove move = default)
    {
        var r = Make(at);
        var pc = r.Pc;
        pc.FaceRotZ(270);                                   // face the way the walk goes, no turn records
        Frame(r, 20);                                       // settle on the floor
        r.Sent.Clear();
        pc.Input2 = Left; Frame(r, 12);                     // walk left
        pc.JumpHeld = true; Frame(r);                       // jump left
        r.JumpFrame = r.Frame;
        pc.JumpHeld = false; Frame(r, 6);
        pc.Input2 = Vector2.Zero; Frame(r, 3);              // let go: the jump goes on left
        if (timing < 0) { Cast(r); Frame(r); pc.Input2 = Right; }
        else if (timing > 0) { pc.Input2 = Right; Frame(r); Cast(r); }
        else { pc.Input2 = Right; Cast(r, move); }
        float x0 = pc.GlobalPosition.X;
        Frame(r, 6);
        r.DriftX = pc.GlobalPosition.X - x0;
        Frame(r, 120);                                      // the skill ends, the body lands
        pc.Input2 = Vector2.Zero;
        Frame(r, 30);
        return r;
    }

    static List<(int State, int Anim, int Dir)> Seq(Run r) =>
        r.Sent.Select(s => ((int)s.Rec.State, (int)s.Rec.Animation, (int)MathF.Round(Norm(s.Rec.Rotation / 10f))))
            .Aggregate(new List<(int, int, int)>(), (l, x) => { if (l.Count == 0 || l[^1] != x) l.Add(x); return l; });

    static string Show(IEnumerable<(int State, int Anim, int Dir)> s) => string.Join(" ", s.Select(x => $"({x.State:x},{x.Anim},{x.Dir})"));

    public override void _Process(double delta)
    {
        if (++frames != 5) return;
        try { Check(); }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); }
        SetProcess(false);
        GD.Print($"fade away: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Check()
    {
        var same = FadeAway(0, new Vector3(0, 5, 0));
        var late = FadeAway(1, new Vector3(0, 5, 1500));
        var early = FadeAway(-1, new Vector3(0, 5, -1500));
        const float right = 90f, left = 270f;               // MS2 rotation z of godot +x / -x (forward (sin z, -cos z))
        foreach (var (name, r, aim, drift) in new[] { ("same frame", same, right, -1), ("one late", late, right, 1), ("one early", early, left, -1) })
        {
            GD.Print($"fade away {name}: aim {r.AimRotZ:0.#}, facing after the cast {r.FacingAfterCast:0.#}, drift x {r.DriftX:0.##} over 6 frames, records {Show(Seq(r))}");
            if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(r.AimRotZ), Mathf.DegToRad(aim))) > 0.01f) fails.Add($"{name}: aim {r.AimRotZ}, not {aim}");
            if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(r.FacingAfterCast), Mathf.DegToRad(aim))) > 0.01f)
                fails.Add($"{name}: facing {r.FacingAfterCast} after the cast, not the aim {aim} (a mid-turn lock)");
            if (Mathf.Sign(r.DriftX) != drift || Mathf.Abs(r.DriftX) < 3f) fails.Add($"{name}: drift {r.DriftX:0.##}, expected the sign {drift}");
        }
        var want = new List<(int, int, int)> { (2, 10, (int)left), (6, 10, (int)left), (6, 9, (int)left), (0x10, 9, (int)right), (1, 9, (int)right) };
        var got = Seq(same).Where(x => x.State is 1 or 2 or 6 or 0x10).ToList();
        if (!got.SequenceEqual(want)) fails.Add($"same-frame records {Show(got)}, not {Show(want)}");
        var skillRecs = same.Sent.Where(s => s.Rec.State == 0x10).ToList();
        // the skill state's record is made on the cast's frame (StateSkill::vfunc_4 -> the frame's record test, mypc.cpp:2308-2311)
        // and goes out when the next one replaces it or it is due (state.cpp:411-423), as the jump's records do
        if (skillRecs.Count == 0 || skillRecs[0].Made != same.CastFrame) fails.Add($"the skill record is made on the cast's frame {same.CastFrame}: {string.Join(",", skillRecs.Select(s => s.Made))}");
        var jumpRec = same.Sent.FirstOrDefault(s => s.Rec.State == 6);
        if (jumpRec.Made != same.JumpFrame) fails.Add($"the jump record is made on the jump's frame {same.JumpFrame}, not {jumpRec.Made}");
        if (skillRecs.Any(s => s.Rec.SubState != 0 || s.Rec.Flags != 0)) fails.Add("the skill record carries a sub or optional fields (no skill id: State::vfunc_26 writes data only when present)");
        Remote(same);
        HeldDiagonal();
        NoClimbInTheSkill();
        // an arc motion (bNoVelocity = the controller's m_nType 1, stateskill.cpp:2865, motioncontroller.cpp:168-171) and
        // magicControl (m_n10 0x3c returns before any vfunc_68, :2854) take no controller step: no jump drift in the air
        var arc = FadeAway(0, new Vector3(0, 5, 3000), new CastMove(1, 0, 0, 0, 0, false, false, default));
        var magic = FadeAway(0, new Vector3(0, 5, -3000), new CastMove(0, 0, 0, 0, 0, false, true, default));
        foreach (var (name, r) in new[] { ("arc motion", arc), ("magic control", magic) })
        {
            GD.Print($"fade away {name}: drift x {r.DriftX:0.##} over 6 frames");
            if (Mathf.Abs(r.DriftX) > 0.5f) fails.Add($"{name}: drift {r.DriftX:0.##} in the air, not 0 (no velocity step)");
        }
    }

    // Unk_PCMove::UpdateMoveDirection (unit_141682f80.cpp:454-490) runs first in the cast's frame: up + right, then up let go
    // on a long frame (the 50 ms timer starts at that frame's time), one short frame on right, then right let go with the
    // cast. The frame's time is two short frames after the start, under 50 ms, so the cast aims the held diagonal
    // (FUN_141683900: no key down and m_moving). A hold that counted the start frame's own delta would have run out.
    void HeldDiagonal()
    {
        var r = Make(new Vector3(0, 5, 750));
        var pc = r.Pc;
        pc.FaceRotZ(270);
        Frame(r, 20);
        pc.Input2 = new Vector2(1, 1); Frame(r, 4);
        pc.Input2 = Right; StepDt(r, 0.04f);
        StepDt(r, Dt);
        pc.Input2 = Vector2.Zero;
        pc.FaceRotZ(270);                                   // the walk turned the body to the diagonal; with no hold the cast aims the facing
        float AimOf(Vector3 k) => Norm(Ms2.Ui.Skills.SkillCast.AimRotZ(Norm(pc.FacingRotZ), new System.Numerics.Vector3(k.X, -k.Z, 0)));
        float aim = AimOf(pc.CastAimDirection());
        var worldDir = typeof(Player).GetMethod("WorldDir", BindingFlags.Instance | BindingFlags.NonPublic);
        float diag = AimOf((Vector3)worldDir.Invoke(pc, new object[] { new Vector2(1, 1) }));
        GD.Print($"fade away held diagonal: aim {aim:0.#} on the release frame after a 40 ms start frame (the diagonal is {diag:0.#})");
        if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(aim), Mathf.DegToRad(diag))) > 0.01f)
            fails.Add($"held diagonal: aim {aim:0.#}, not the up-right diagonal {diag:0.#} (the 50 ms timer runs from its start frame's time)");
        Frame(r, 10);
        // the timer out: four short frames on right, the first starting it (67 ms at the release frame), so the cast aims the facing
        pc.Input2 = new Vector2(1, 1); Frame(r, 4);
        pc.Input2 = Right; Frame(r, 4);
        pc.Input2 = Vector2.Zero;
        pc.FaceRotZ(270);
        float late = AimOf(pc.CastAimDirection());
        GD.Print($"fade away held diagonal out: aim {late:0.#} on the release frame 67 ms after the start");
        if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(late), Mathf.DegToRad(270f))) > 0.01f) fails.Add($"held diagonal out: aim {late:0.#}, not the facing 270");
        Frame(r, 10);
    }

    // walk right, jump right, let go, cast (aim = the facing, right): the skill state keeps the jump's drift into a wall of
    // cubes. The client is in StateSkill, whose update (stateskill.cpp) has no climb check (PCBase::FUN_141678820 and
    // PC::FUN_14165b500 run from StateJump / StateFall vfunc_8), so the body does not climb while the skill plays. A probe
    // run without the wall gives the drift; the wall stands halfway between the cast and the landing.
    void NoClimbInTheSkill()
    {
        var holds = typeof(Player).GetMethod("SkillHoldsMove", BindingFlags.Instance | BindingFlags.NonPublic);
        var wallHit = typeof(Player).GetField("wallHit", BindingFlags.Instance | BindingFlags.NonPublic);
        (float Cast, float Land, int Climbed, int Hits, string Log) Jump(Vector3 at)
        {
            var r = Make(at);
            var pc = r.Pc;
            pc.FaceRotZ(90);
            Frame(r, 20);
            pc.Input2 = Right; Frame(r, 12);
            pc.JumpHeld = true; Frame(r);
            pc.JumpHeld = false; Frame(r, 6);
            pc.Input2 = Vector2.Zero; Frame(r, 3);
            Cast(r);
            float cast = pc.GlobalPosition.X, land = cast;
            int climbed = 0, hits = 0;
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 150; i++)
            {
                bool skill = (bool)holds.Invoke(pc, null);
                Frame(r);
                if (skill && (bool)wallHit.GetValue(pc)) hits++;
                if (skill && pc.State == Player.MoveState.Climb) climbed++;
                if (skill && pc.State is Player.MoveState.Jump or Player.MoveState.Fall) land = pc.GlobalPosition.X;
                if (i % 10 == 0) log.Append($" {i}:{pc.State}{(skill ? "*" : "")}@{pc.GlobalPosition.X:0},{pc.GlobalPosition.Y:0}");
            }
            return (cast, land, climbed, hits, log.ToString());
        }
        const float x0 = -1500f, z = WallRow;
        var probe = Jump(new Vector3(x0, 5, -z));
        GD.Print($"fade away climb probe: cast x {probe.Cast:0}, last air x in the skill {probe.Land:0};{probe.Log}");
        // the wall (built at the start, WallRow): its face at MS2 x -75; the run starts so the face is halfway along the drift
        float face = -MoveConfig.CubeSize / 2;
        float start = face - ((probe.Cast + probe.Land) / 2 - x0);
        var wall = Jump(new Vector3(start, 5, z));
        GD.Print($"fade away climb: start x {start:0}, wall face x {face:0}, wall hits in the skill {wall.Hits}, climb frames in the skill {wall.Climbed};{wall.Log}");
        if (wall.Hits == 0) fails.Add("climb: the skill's drift never hit the cube wall while descending (the scene does not test the case)");
        if (wall.Climbed > 0) fails.Add($"climb: the body climbed the cube wall on {wall.Climbed} frames while the skill state played (StateSkill has no climb check)");
    }

    // a remote fed the same-frame records on their frames and the cast on its frame
    void Remote(Run own)
    {
        var md = builder.LoadModel("female/f_body.nif");
        var visual = builder.Build(md, "Female");
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var anims = ActionAnims.Load(packs, builder, md, anim, "female");
        var start = own.Sent.Count > 0 ? own.Sent[0].Rec.Position : default;
        var pc = RemotePc.Create("Remote", visual, anim, anims, MoveConfig.SceneGravity, new Vector3(start.X, start.Z + 25, -(start.Y + 3000)), 0, faceGrids);
        for (int kind = 0; kind < 3; kind += 2) { pc.SetStat(kind, MoveSpeedRate.MovementSpeedStat, 100); pc.SetStat(kind, MoveSpeedRate.MountSpeedStat, 100); }
        var castAnim = ModelBuilder.BuildAnimation(builder.LoadKf($"female/{CastSeq}.kf"), md);
        castAnim.LoopMode = Animation.LoopModeEnum.None;
        if (!anim.GetAnimationLibrary("").HasAnimation("cast_" + CastSeq)) anim.GetAnimationLibrary("").AddAnimation("cast_" + CastSeq, castAnim);
        AddChild(pc.Body);
        pc.Spawn();
        for (int i = 0; i < 20; i++) pc.Tick(Dt);
        int first = own.Sent[0].Frame, idx = 0;
        float x0 = 0; bool jumping = false;
        for (int f = first; f <= own.CastFrame + 6; f++)
        {
            while (idx < own.Sent.Count && own.Sent[idx].Frame == f)
            {
                var rec = own.Sent[idx++].Rec;
                // the own records are at y 0; the remote runs 3000 further along y, on the same floor
                rec.Position = new Vector3S(rec.Position.X, (short)(rec.Position.Y + 3000), rec.Position.Z);
                pc.States.Push(SyncRecord.From(rec));
            }
            if (f == own.CastFrame)
            {
                pc.PlaySkill("cast_" + CastSeq, 1f, 10200001, false, new System.Numerics.Vector3(pc.Position.X, pc.Position.Y, pc.Position.Z), own.AimRotZ);
                x0 = pc.Body.GlobalPosition.X;
                jumping = pc.Jumping;
            }
            pc.Tick(Dt);
        }
        float drift = pc.Body.GlobalPosition.X - x0;
        GD.Print($"fade away remote: state {pc.States.Cur:x} jumping at the cast {jumping}, drift x {drift:0.##} over 6 frames, rotation {pc.States.TargetYaw:0.#}");
        if (pc.States.Cur != RemotePcStates.StateSkill || !jumping) fails.Add($"remote: state {pc.States.Cur:x}, jumping at the cast {jumping}");
        else if (drift > -3f) fails.Add($"remote: drift {drift:0.##} in the skill state, not the jump's left drift");
    }
}
