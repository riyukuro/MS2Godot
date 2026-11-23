using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The remote PC's sync replay (Replay.SyncRecord, Replay.RemotePcStates, Replay.GroundEffectGate) against the client's
/// CStateManager (statemanager.cpp) and states, on a fake body: flat ground at z 0 (water for x above <see cref="Body.WaterFromX"/>),
/// GameObjectController's jump (StartJump: v0 = sqrt(2 * 2h * -g)) and fall (dz = g t dt + g dt^2 / 2) integration.
/// </summary>
public class RemotePcStatesTests {
    sealed class Body : IRemotePc {
        public Vector3 Position { get; set; }
        public float RotationZ { get; set; }
        public float WaterFromX = float.MaxValue;
        public bool OnGround { get; private set; }
        bool hitWater;                                   // the hit report's entity is water
        public bool OnWater => hitWater;
        public string Material = "";                     // m_str958
        float fallTime; bool fallRunning;
        public bool InAirFor(int ms) => fallRunning && fallTime * 1000f > ms;
        public bool Jumping { get; private set; }
        float v0, jumpT, jumpH;
        public bool Rising => Jumping && jumpT < JumpApexTime;
        public float JumpApexTime => v0 / -Gravity;
        public int FallTimeMs => (int) (fallTime * 1000f);
        Vector3 kVel;
        public void StopJump() { Jumping = false; fallTime = 0; fallRunning = false; }
        public void StartJump(float height, Vector3 dir, float speed) { v0 = MathF.Sqrt(2f * 2f * height * -Gravity); jumpT = 0; jumpH = 0; Jumping = true; kVel = dir * speed; }
        public void SetVelocity(Vector3 v) => kVel = v;
        public void Move(Vector3 move, float dt) {
            var d = move;
            if (Jumping) {
                jumpT += dt;
                float h = v0 * jumpT + 0.5f * Gravity * jumpT * jumpT;
                d.Z += h - jumpH; jumpH = h;
                d.X += kVel.X * dt; d.Y += kVel.Y * dt;
            } else if (fallRunning || Position.Z > 0) {
                d.Z += Gravity * fallTime * dt + 0.5f * Gravity * dt * dt;
                if (move == Vector3.Zero && InAirFor(0)) { d.X += kVel.X * dt; d.Y += kVel.Y * dt; }
            }
            var p = Position + d;
            if (p.Z <= 0 && d.Z <= 0) {
                p.Z = 0; OnGround = true; hitWater = p.X >= WaterFromX;
                if (!(Jumping && jumpT <= dt)) { Jumping = false; fallTime = 0; fallRunning = false; }
            } else {
                OnGround = false;
                if (!fallRunning) { fallRunning = true; fallTime = 0; }
                fallTime += dt;
            }
            Position = p;
        }
        public int LandRefreshes, Refreshes;
        public void LandRefresh() { LandRefreshes++; hitWater = Position.X >= WaterFromX; RefreshGround(); }
        public void RefreshGround() { Refreshes++; Material = hitWater ? "water" : "default"; }
        public readonly List<(string action, string ground)> Effects = new();
        public void PlayEffects(string action) => Effects.Add((action, Material));
        public int Current = -1; public readonly List<int> Played = new();
        public readonly List<(int action, float rate, bool restart)> PlayLog = new();
        public bool IsAction(int action) => Current == action;
        public bool HasAction(int action) => true;
        public bool PlayAction(int action, float rate, bool restart) { PlayLog.Add((action, rate, restart)); if (Current != action || restart) Played.Add(action); Current = action; return true; }
        public float Gait(float speed, float run, float walk, out bool isRun) { isRun = speed > walk; return (speed == run ? 1f : speed / run) * ScaleAnimRate; }
        public float WalkSpeed => 120f;
        public float RunSpeed => 350f;
        public float RowRunSpeed => 350f;
        public float SwimSpeed => 350f;
        public bool WaterRunner { get; set; }
        public float MoveSpeedRate { get; set; } = 1f;
        public float ScaleAnimRate { get; set; } = 1f;
        float animSpeed = 1f;
        public float? AnimSpeedReadOverride;
        public int AnimSpeedReads;
        public float AnimSpeed { get { AnimSpeedReads++; return AnimSpeedReadOverride ?? animSpeed; } set => animSpeed = value; }
        public float Gravity => -3460.2074f;
        public float JumpHeight => 100f;
        public float AirSpeedScale => 1f;
        public float TurnSpeed => 1800f;
        public bool BattleStance => false;
        public HoldTarget Hold;
        public (int id, string node) HoldAsked;
        public HoldTarget FindHoldTarget(int id, string node) { HoldAsked = (id, node); return Hold; }
        public readonly List<string> Sequences = new();
        public bool PlaySequence(string name, float rate) { if (Sequences.Count == 0 || Sequences[^1] != name) Sequences.Add(name); Current = -1; return true; }
        public readonly Dictionary<string, float> CastKeys = new();
        public float CastKeyTime(string key) => CastKeys.TryGetValue(key, out float t) ? t : -1f;
        public readonly List<float> Seeks = new();
        public void SeekCast(float time) => Seeks.Add(time);
        public bool Ledge;
        public readonly List<(Vector3 dir, float dist, float height, float up)> LedgeAsked = new();
        public bool LedgeAhead(Vector3 dir, float dist, float height, float up) { LedgeAsked.Add((dir, dist, height, up)); return Ledge; }
        public bool Pushed { get; set; }
    }

    // the default field camera's Euler rotation (CameraMathTests: hangle 45, vangle 30), its flat view direction (1, 1)/sqrt 2
    static readonly Vector3 Cam = new(90, 45, -40);

    static SyncRecord Rec(int state, int sub, Vector3 pos, Vector2 move, float timeS, float yaw = 0, Vector3? cam = null, int skill = 0, int hold = 0, string holdNode = null) {
        var s = new StateSync {
            State = (byte) state, SubState = (byte) sub, Position = new Vector3S((short) pos.X, (short) pos.Y, (short) pos.Z),
            Rotation = (short) (yaw * 10), Rotation2 = (short) (yaw * 10), Unknown1 = 2, Unknown3 = (short) (timeS * 1000), SyncNumber = 0x7fffffff,
        };
        if (skill != 0) { s.Flags |= StateSync.Flag.Flag1; s.Flag1Value = skill; }   // m_n64, optional bit 0
        if (hold != 0) { s.Flags |= StateSync.Flag.Flag6; s.Flag6Value = hold; s.Flag6Text = holdNode; }   // m_n90 / m_str98, optional bit 5
        var c = cam ?? Cam;
        s.Speed = new Vector3S((short) c.X, (short) c.Y, (short) c.Z);
        int ax = move.X == -1 ? 0 : move.X == 0 ? 1 : move.X == 1 ? 2 : -1, ay = move.Y == -1 ? 0 : move.Y == 0 ? 1 : move.Y == 1 ? 2 : -1;
        if (ax >= 0 && ay >= 0) s.Animation = (byte) (ax + 4 * (ay + 1)); else { s.Animation = 0x80; s.Float1 = move.X; s.Float2 = move.Y; }
        // through the wire: the client's reader, not the object we built
        var w = new ByteWriter(); s.Write(w);
        return SyncRecord.From(StateSync.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())));
    }

    static void Run(RemotePcStates sm, float seconds, float dt = 1 / 60f) { for (float t = 0; t < seconds - 1e-4f; t += dt) sm.Update(dt); }

    [Test] public void RecordReadsTheClientsUnits() {
        // StateSyncObject::vfunc_2 + FUN_1408365d0: rotations /10, time /1000, the direction flags unpacked (x 0/1/2, y 4/8/12)
        var r = Rec(2, 0x18, new Vector3(100, -200, 300), new Vector2(1, 1), 0.05f, yaw: 123.4f);
        Assert.That(r.State, Is.EqualTo(2)); Assert.That(r.Sub, Is.EqualTo(0x18));
        Assert.That(r.Position, Is.EqualTo(new Vector3(100, -200, 300)));
        Assert.That(r.RotationZ, Is.EqualTo(123.4f).Within(1e-4)); Assert.That(r.TargetYaw, Is.EqualTo(123.4f).Within(1e-4));
        Assert.That(r.Time, Is.EqualTo(0.05f).Within(1e-6));
        Assert.That(r.Move, Is.EqualTo(new Vector2(1, 1)));
        Assert.That(r.CameraEuler, Is.EqualTo(Cam)); Assert.That(r.CameraMode, Is.EqualTo(2));
        Assert.That(SyncRecord.UnpackMove(0x80, 0.6f, -0.8f), Is.EqualTo(new Vector2(0.6f, -0.8f)));
        Assert.That(SyncRecord.UnpackMove(0 + 4, 0, 0), Is.EqualTo(new Vector2(-1, -1)));
        Assert.That(SyncRecord.UnpackMove(1 + 8, 0, 0), Is.EqualTo(Vector2.Zero));
        Assert.That(SyncRecord.UnpackMove(3 + 0, 0, 0), Is.EqualTo(Vector2.Zero));   // 3 and 0 name no value: both stay 0
    }

    [Test] public void QueueKeepsTheLast100Ms() {
        // FUN_1417db2e0: from the newest record with a time, the record taking the sum past 100 ms goes with the older ones
        var sm = new RemotePcStates(new Body());
        for (int i = 0; i < 6; i++) sm.Push(Rec(1, 0, Vector3.Zero, Vector2.Zero, 0.03f));
        Assert.That(sm.Queued, Is.EqualTo(3));          // 0.03 + 0.03 + 0.03 = 0.09 kept; the fourth makes 0.12
        sm.Push(Rec(1, 0, Vector3.Zero, Vector2.Zero, 0f));
        Assert.That(sm.Queued, Is.EqualTo(4));          // a zero-time record is skipped for the sum's start
        var z = new RemotePcStates(new Body());
        for (int i = 0; i < 10; i++) z.Push(Rec(1, 0, Vector3.Zero, Vector2.Zero, 0f));
        Assert.That(z.Queued, Is.EqualTo(10));          // no record with a time: nothing is erased
    }

    [Test] public void QueuePlaysEachRecordOnceOverItsTime() {
        // FUN_1417dc230: the front record counts down by the frame and plays once; a shorter one gives way to the next
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateLand));   // spawned falling (pcbase.cpp:792) and landed
        sm.SequenceEnded(RemotePcStates.ActJumpLand);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
        sm.Push(Rec(2, 0x18, Vector3.Zero, new Vector2(0, 1), 0.05f));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateWalk));
        Assert.That(sm.Queued, Is.EqualTo(1));          // held (the only record, counted down to 0)
        sm.Push(Rec(1, 1, Vector3.Zero, Vector2.Zero, 0.05f));
        Run(sm, 0.05f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    [Test] public void WalkRecordsMoveAlongTheSendersCamera() {
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        var start = b.Position;
        sm.Push(Rec(2, 0x18, start, new Vector2(0, 1), 0f));
        Run(sm, 1f);
        // StateWalk::vfunc_3: CalcMoveDirection(forward) along the camera's flat view (1, 1)/sqrt 2 at the run speed
        var d = b.Position - start;
        Assert.That(d.Length(), Is.EqualTo(350f).Within(350f / 60f + 0.5f));
        Assert.That(d.X, Is.EqualTo(d.Y).Within(1f));
        Assert.That(d.X, Is.GreaterThan(0));
        Assert.That(b.Played, Does.Contain(RemotePcStates.ActRun));
        // moveLeft is x = +1 (Unk_PCMove::GetMoveDirection): left of the (1, 1) view is (-1, 1)
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(1, 0), 0f));
        var p0 = b.Position; Run(sm, 0.5f);
        var l = b.Position - p0;
        Assert.That(l.X, Is.LessThan(0)); Assert.That(l.Y, Is.GreaterThan(0)); Assert.That(-l.X, Is.EqualTo(l.Y).Within(1f));
        // the walk sub (0x19) walks at 120 with the walk sequence
        sm.Push(Rec(2, 0x19, b.Position, new Vector2(0, 1), 0f));
        p0 = b.Position; Run(sm, 1f);
        Assert.That((b.Position - p0).Length(), Is.EqualTo(120f).Within(3f));
        Assert.That(b.Current, Is.EqualTo(RemotePcStates.ActWalk));
    }

    [Test] public void CameraModeFourteenUsesTheBodyFacing() {
        // PCBase::vfunc_67 with m_unk_c00 0xe: -UNIT_Y * MakeZRotation(z), forward (sin z, -cos z)
        var b = new Body { RotationZ = 90 };
        var sm = new RemotePcStates(b);
        var s = Rec(1, 1, Vector3.Zero, new Vector2(0, 1), 0f, yaw: 90);
        s.CameraMode = 0xe;
        sm.Push(s); sm.Update(1 / 60f);
        var dir = sm.MoveDirection();
        Assert.That(dir.X, Is.EqualTo(1f).Within(1e-4)); Assert.That(dir.Y, Is.EqualTo(0f).Within(1e-4));
    }

    [Test] public void ReplayedJumpLandsByItselfAndPlaysTheLanding() {
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        b.Effects.Clear();
        var states = new List<int>();
        sm.Push(Rec(6, 0x21, b.Position, Vector2.Zero, 0f));
        for (int i = 0; i < 90; i++) { sm.Update(1 / 60f); if (states.Count == 0 || states[^1] != sm.Cur) states.Add(sm.Cur); }
        // StateJump -> (StateJump::vfunc_3 / vfunc_9: the jump ended / touched down) StateLand -> held by the sequence
        Assert.That(states, Is.EqualTo(new[] { RemotePcStates.StateJump, RemotePcStates.StateLand }));
        Assert.That(b.Played, Does.Contain(RemotePcStates.ActJumpUp));
        Assert.That(b.Played, Does.Contain(RemotePcStates.ActJumpDown));
        Assert.That(b.Effects, Is.EqualTo(new[] { ("land", "default") }));    // StateLand::vfunc_4: one "land"
        // a StateLand record is not applied (FUN_1417db550: m_nState 4); the landing sequence's end goes to StateIdle
        sm.Push(Rec(4, 0, b.Position, Vector2.Zero, 0f));
        sm.Update(1 / 60f);
        Assert.That(b.Effects.Count, Is.EqualTo(1));
        sm.SequenceEnded(RemotePcStates.ActJumpLand);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
        // the jump went up about 2h (StartJump(h): v0 = sqrt(2 * 2h * -g))
    }

    [Test] public void RemoteLandingOnWaterPlaysTheWaterLand() {
        // a remote walking off a ledge onto water: StateWalk -> StateFall (vfunc_9, the fall timer past sqrt(3/|g|)) ->
        // StateLand (StateFall::vfunc_9) whose ray puts the water in the hit report and refreshes the material before
        // "land", then on water straight to StateIdle with the water idle (sub 7)
        var b = new Body { WaterFromX = 50 };
        var sm = new RemotePcStates(b);
        b.Position = new Vector3(100, 0, 300);          // above the water, the hit report still the stone it left
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0f, cam: new Vector3(57, 90, 0)));   // the (0, 1) view
        var states = new List<int>();
        sm.StateChanged = states.Add;
        for (int i = 0; i < 120; i++) sm.Update(1 / 60f);
        Assert.That(states, Is.EqualTo(new[] { RemotePcStates.StateWalk, RemotePcStates.StateFall, RemotePcStates.StateLand, RemotePcStates.StateIdle }));
        Assert.That(b.Effects, Is.EqualTo(new[] { ("land", "water") }));
        Assert.That(sm.Sub, Is.EqualTo(7));
        Assert.That(b.Current, Is.EqualTo(RemotePcStates.ActWaterIdle));
        Assert.That(b.LandRefreshes, Is.EqualTo(1));
    }

    [Test] public void PositionCorrectionFollowsTheClient() {
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        // a walk record less than 100 away on the ground plane leaves the PC where it is (FUN_1417dbd00)
        sm.Push(Rec(2, 0x18, new Vector3(60, 60, 0), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.LessThan(5));
        Assert.That(sm.Snaps, Is.EqualTo(0));
        // 100 or more away: put there
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.EqualTo(300).Within(1));
        Assert.That(sm.Snaps, Is.EqualTo(1));
        Assert.That(sm.LastSnap, Is.InRange(290f, 310f));
        // a height difference over 200 snaps even when close
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 250), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.Z, Is.GreaterThan(200));
        Assert.That(sm.Snaps, Is.EqualTo(2));
        // a state outside the moving list (StateEmotion 0xb) snaps at any distance
        b.Position = Vector3.Zero;
        sm.Push(Rec(0xb, 0, new Vector3(10, 0, 0), Vector2.Zero, 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.EqualTo(10).Within(0.01));
        Assert.That(sm.Cur, Is.EqualTo(0xb));
        // StateHit / StateStun records place the PC but do not change its state
        sm.Push(Rec(0xd, 0, new Vector3(20, 0, 0), Vector2.Zero, 0f));
        sm.Update(1e-4f);
        Assert.That(sm.Cur, Is.EqualTo(0xb));
        // an unregistered id is ignored by ChangeState
        sm.Push(Rec(0x13, 0, new Vector3(30, 0, 0), Vector2.Zero, 0f));
        sm.Update(1e-4f);
        Assert.That(sm.Cur, Is.EqualTo(0xb));
        Assert.That(RemotePcStates.CorrectionAllowed(0x20), Is.False);
        Assert.That(RemotePcStates.CorrectionAllowed(2), Is.True);
    }

    [Test] public void AHitTakesNoRecordUntilItEnds() {
        // FUN_1417dc4c0 (statemanager.cpp:622): StateHit::vfunc_32 is m_bC8, cleared at the end of vfunc_4 and set by
        // vfunc_5, so while the PC reacts to a hit a record still places it but leaves its state
        var b = new Body();
        var sm = new RemotePcStates(b);
        int entered = 0, left = 0, updates = 0, frames = 0;
        sm.HitEntered = () => entered++;
        sm.HitLeft = () => left++;
        sm.HitUpdate = _ => updates++;
        sm.HitFrame = () => frames++;
        sm.Spawn(); Run(sm, 0.1f);
        Assert.That(sm.TakesRecords, Is.True);
        sm.ChangeState(RemotePcStates.StateHit);
        Assert.That((sm.Cur, entered, sm.TakesRecords), Is.EqualTo((RemotePcStates.StateHit, 1, false)));
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHit), "a walk record took the PC out of its hit");
        Assert.That(b.Position.X, Is.EqualTo(300).Within(1), "the record no longer placed the PC");
        Assert.That((updates, frames), Is.EqualTo((1, 1)), "the hit's vfunc_3 and vfunc_9 did not run each frame");
        // the hit's own end (vfunc_59(1)) runs vfunc_5: records are taken again
        sm.ChangeState(RemotePcStates.StateIdle);
        Assert.That((left, sm.TakesRecords), Is.EqualTo((1, true)));
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateWalk));
        // StateStun::vfunc_32 is false: a stunned PC keeps its stun
        sm.ChangeState(RemotePcStates.StateStun);
        sm.Push(Rec(1, 1, b.Position, Vector2.Zero, 0f));
        sm.Update(1e-4f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateStun));
    }

    [Test] public void APushedPcIsNotCorrected() {
        // FUN_1417dc4c0 (statemanager.cpp:600-608): a running push (m_pos18 not zero) skips the correction, the rotation and
        // the target yaw; the record's state is still applied
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        b.Pushed = true;
        sm.Push(Rec(0xb, 0, new Vector3(300, 0, 0), Vector2.Zero, 0f, yaw: 90));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.LessThan(5), "a pushed PC was put on the record");
        Assert.That(b.RotationZ, Is.Not.EqualTo(90f).Within(0.1f), "a pushed PC took the record's rotation");
        Assert.That(sm.Cur, Is.EqualTo(0xb));
        b.Pushed = false;
        sm.Push(Rec(0xb, 0, new Vector3(300, 0, 0), Vector2.Zero, 0f, yaw: 90));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.EqualTo(300).Within(0.01));
    }

    [Test] public void JumpWithANewSubRestarts() {
        // StateJump::vfunc_28: a jump record whose sub differs from the jump's forces a new jump (FUN_1417dc4c0)
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        sm.Push(Rec(6, 0x21, b.Position, Vector2.Zero, 0f)); Run(sm, 0.2f);
        float h1 = b.Position.Z;
        sm.Push(Rec(6, 0x21, b.Position, Vector2.Zero, 0f)); Run(sm, 1 / 60f);
        Assert.That(b.Position.Z, Is.GreaterThan(h1 - 50));      // same sub: the jump goes on
        sm.Push(Rec(6, 0x23, b.Position, Vector2.Zero, 0f)); sm.Update(1 / 60f);
        Assert.That(sm.Sub, Is.EqualTo(0x23));
        Assert.That(b.Jumping, Is.True);
    }

    [Test] public void RandomJumpSubFollowsClientTable() {
        // StateJump::StateJump (@0x1417d5160, statejump.cpp:82-85): m_aSub = {0x21, 0x23, 0x22, 0x24};
        // StateJump::vfunc_4 (@0x1417d5320, statejump.cpp:107-116): toggle = (toggle + 1) % 2; odd rand -> (m_aSub + 2)[toggle], even -> m_aSub[toggle]
        int oddSeed = 0;
        while (true) { var r = new Random(oddSeed); if (r.Next(2) == 1 && r.Next(2) == 1 && r.Next(2) == 1) break; oddSeed++; }
        var b = new Body();
        var sm = new RemotePcStates(b, oddSeed);
        var oddSubs = new int[3];
        for (int i = 0; i < 3; i++) {
            sm.ChangeState(RemotePcStates.StateIdle);
            sm.Push(Rec(6, 0x25, b.Position, Vector2.Zero, 0f));
            sm.Update(1 / 60f);
            oddSubs[i] = sm.Sub;
        }
        Assert.That(oddSubs, Is.EqualTo(oddSubs[0] == 0x22 ? new[] { 0x22, 0x24, 0x22 } : new[] { 0x24, 0x22, 0x24 }));

        int evenSeed = 0;
        while (true) { var r = new Random(evenSeed); if (r.Next(2) == 0 && r.Next(2) == 0 && r.Next(2) == 0) break; evenSeed++; }
        var sm2 = new RemotePcStates(new Body(), evenSeed);
        var evenSubs = new int[3];
        for (int i = 0; i < 3; i++) {
            sm2.ChangeState(RemotePcStates.StateIdle);
            sm2.Push(Rec(6, 0x25, Vector3.Zero, Vector2.Zero, 0f));
            sm2.Update(1 / 60f);
            evenSubs[i] = sm2.Sub;
        }
        Assert.That(evenSubs, Is.EqualTo(evenSubs[0] == 0x21 ? new[] { 0x21, 0x23, 0x21 } : new[] { 0x23, 0x21, 0x23 }));
    }

    [Test] public void WeaponKeyChangedFiresOnStateSubAndGaitChangeOnly() {
        // The weapon-hold key (state, sub state, StateWalk's run flag) is what State::vfunc_15 reads (@0x1417ff..., statewalk.cpp);
        // a remote slowing from run to slow walk changes the key without changing the state.
        var b = new Body();
        var sm = new RemotePcStates(b);
        var keys = new List<(int cur, int sub, bool run)>();
        sm.WeaponKeyChanged = () => keys.Add((sm.Cur, sm.Sub, sm.WalkRun));
        sm.Spawn(); Run(sm, 0.1f);
        sm.SequenceEnded(RemotePcStates.ActJumpLand);
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(2));
        Assert.That(sm.WalkRun, Is.True);
        Assert.That(keys[^1], Is.EqualTo((2, 0x18, true)));
        int n = keys.Count;
        sm.Update(1 / 60f); sm.Update(1 / 60f);
        Assert.That(keys.Count, Is.EqualTo(n), "no per-frame notification");

        // slow walk: sub 0x19 clears the run flag
        sm.Push(Rec(2, 0x19, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(sm.WalkRun, Is.False);
        Assert.That(keys[^1], Is.EqualTo((2, 0x19, false)));
        Assert.That(keys.Count, Is.GreaterThan(n));

        // nothing changed: frames at sub 0x19 (WalkFrame clears the flag every frame) and the same record pushed again
        // (ChangeState's same-state branch) must not notify
        n = keys.Count;
        for (int i = 0; i < 5; i++) sm.Update(1 / 60f);
        sm.Push(Rec(2, 0x19, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        sm.Push(Rec(2, 0x19, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(keys.Count, Is.EqualTo(n), "no notification without a key change");
    }

    [Test] public void StateWalkChangeTestUsesStatRates() {
        // StateWalk::vfunc_4 (@0x1417ff800, statewalk.cpp:103-104) stores vfunc_96/vfunc_97 (MoveSpeedRate/ScaleAnimRate);
        // StateWalk::vfunc_9 (@0x141800580, statewalk.cpp:356-371) compares those, not Speed/RunSpeed.
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        sm.SequenceEnded(RemotePcStates.ActJumpLand);
        b.PlayLog.Clear();

        // Walk entry: vfunc_96/vfunc_97 match what vfunc_4 stored -> plays ActRun with restart = false
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(1));
        Assert.That(b.PlayLog[^1].action, Is.EqualTo(RemotePcStates.ActRun));
        Assert.That(b.PlayLog[^1].restart, Is.False);

        // 0x18 -> 0x19 sub change: switches to ActWalk with restart = false (rates unchanged)
        sm.Push(Rec(2, 0x19, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(2));
        Assert.That(b.PlayLog[^1].action, Is.EqualTo(RemotePcStates.ActWalk));
        Assert.That(b.PlayLog[^1].restart, Is.False);

        // 0x19 -> 0x18 sub change while staying in StateWalk: rates unchanged, sequence keeps running without restart
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(2));

        // MoveSpeedRate (vfunc_96) changes -> restarts once (vfunc_10 updates walkRun to 1, switching to ActRun on the next frame), then holds
        b.MoveSpeedRate = 1.5f;
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(3));
        Assert.That(b.PlayLog[^1].restart, Is.True);
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(4));
        Assert.That(b.PlayLog[^1].action, Is.EqualTo(RemotePcStates.ActRun));
        Assert.That(b.PlayLog[^1].restart, Is.False);
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(4));

        // ScaleAnimRate (vfunc_97) changes -> restarts once, then holds
        b.ScaleAnimRate = 0.75f;
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(5));
        Assert.That(b.PlayLog[^1].restart, Is.True);
        Assert.That(b.PlayLog[^1].rate, Is.EqualTo(1.5f * 0.75f).Within(1e-4));
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog.Count, Is.EqualTo(5));

        // PCBase::vfunc_97 (@0x141671720, pcbase.cpp:2504-2519)
        Assert.That(RemotePcStates.ScaleRate(1f, 1f), Is.EqualTo(1f));
        Assert.That(RemotePcStates.ScaleRate(0.4f, 1f), Is.EqualTo(1.5f));
        Assert.That(RemotePcStates.ScaleRate(0.75f, 1f), Is.EqualTo(1.25f).Within(1e-5));
        Assert.That(RemotePcStates.ScaleRate(1.5f, 1f), Is.EqualTo(0.75f).Within(1e-5));
        Assert.That(RemotePcStates.ScaleRate(2.5f, 1f), Is.EqualTo(0.5f));
    }

    [Test] public void StateFallUsesOwnerAnimSpeed() {
        // StateFall::vfunc_4 (@0x1417c6ef0, statefall.cpp:95, 128) calls State::vfunc_4 -> State::vfunc_10
        // (@0x1417b8090, state.cpp:649-652 -> FUN_141670d80(1f)) and then PlayAnimation(m_strAnim, FUN_1416713c0()).
        var b = new Body { MoveSpeedRate = 1.5f };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.1f);
        sm.SequenceEnded(RemotePcStates.ActJumpLand);

        // Walking sets AnimSpeed (m_f938) via StateWalk::vfunc_10 -> FUN_141670d80
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0f));
        sm.Update(1 / 60f);
        Assert.That(b.AnimSpeed, Is.EqualTo(1.5f).Within(1e-4));

        // Entering StateFall resets AnimSpeed to 1f (State::vfunc_4 -> State::vfunc_10) and reads FUN_1416713c0()
        b.PlayLog.Clear();
        int readsBefore = b.AnimSpeedReads;
        b.Position = new Vector3(0, 0, 200);
        sm.ChangeState(RemotePcStates.StateFall);
        Assert.That(b.AnimSpeedReads, Is.GreaterThan(readsBefore));
        Assert.That(b.AnimSpeed, Is.EqualTo(1f));
        Assert.That(b.PlayLog[^1], Is.EqualTo((RemotePcStates.ActJumpFall, 1f, false)));

        // When FUN_1416713c0() returns a non-unit rate, StateFall passes that rate to PlayAction (not hardcoded 1f)
        b.AnimSpeedReadOverride = 1.25f;
        b.PlayLog.Clear();
        sm.ChangeState(RemotePcStates.StateFall, force: true);
        Assert.That(b.PlayLog[^1], Is.EqualTo((RemotePcStates.ActJumpFall, 1.25f, false)));

        // StateSwim::vfunc_9 (@0x1417fac90, stateswim.cpp:257-258) also updates and passes AnimSpeed via vfunc_10
        b.AnimSpeedReadOverride = null;
        b.PlayLog.Clear();
        sm.ChangeState(RemotePcStates.StateSwim);
        sm.Update(1 / 60f);
        Assert.That(b.PlayLog[^1].action, Is.EqualTo(RemotePcStates.ActWaterSwim));
        Assert.That(b.PlayLog[^1].rate, Is.EqualTo(1.5f).Within(1e-4));
    }

    [Test] public void GroundMaterialRefreshesEvery200Ms() {
        // PC::vfunc_53: vfunc_93(true) on the first frame and whenever m_timer970 (200 ms) has expired
        var b = new Body();
        var sm = new RemotePcStates(b);
        for (int i = 0; i < 60; i++) sm.Update(1 / 60f);
        Assert.That(b.Refreshes, Is.EqualTo(1 + 4));   // frame 0, then at 200, 400, 600, 800 ms (12 frames each)
    }

    [Test] public void PlayEffectsGates() {
        // groundeffectdata.cpp:278-291
        Assert.That(GroundEffectGate.Allows(true, 0x6e, true, "run0", "stone", false, true, false), Is.False);
        Assert.That(GroundEffectGate.Allows(true, 0x6e, true, "walk", "stone", false, true, false), Is.True);
        Assert.That(GroundEffectGate.Allows(true, 0x6e, false, "run0", "stone", false, true, false), Is.True);
        Assert.That(GroundEffectGate.Allows(true, 0x6e, true, "land", "stone", false, true, false), Is.True);
        Assert.That(GroundEffectGate.Allows(true, 10, true, "run0", "stone", false, true, false), Is.True);
        Assert.That(GroundEffectGate.Allows(true, 10, false, "land", "", false, true, true), Is.False);
        Assert.That(GroundEffectGate.Allows(true, 10, false, "land", "water", true, true, true), Is.False);
        Assert.That(GroundEffectGate.Allows(true, 10, false, "land", "water", false, false, false), Is.False);
        Assert.That(GroundEffectGate.Allows(true, 10, false, "land", "water", false, false, true), Is.True);
        Assert.That(GroundEffectGate.Allows(true, 10, false, "land", "water", false, true, false), Is.True);
        // PCBase::TextKeyEvent: "run*" / "walk*" keys, not in state 0, not class 0x6200028
        Assert.That(GroundEffectGate.StepKey("run0", 2, 0x6200003), Is.True);
        Assert.That(GroundEffectGate.StepKey("walk", 2, 0x6200003), Is.True);
        Assert.That(GroundEffectGate.StepKey("run1", 0, 0x6200003), Is.False);
        Assert.That(GroundEffectGate.StepKey("run1", 2, 0x6200028), Is.False);
        Assert.That(GroundEffectGate.StepKey("effect_x", 2, 0x6200003), Is.False);
    }

    [Test] public void CastFlushesTheQueueAndKeepsTheSkillState() {
        // PlayCastStartEffect's vfunc_59(0x10, .., 1): ChangeState(0x10) applies every queued record first (FlushRequests),
        // then StateSkill::vfunc_4 puts the PC on the record's position and rotation
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        var states = new List<int>();
        sm.StateChanged = states.Add;
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0.05f));
        sm.Push(Rec(2, 0x18, b.Position, new Vector2(0, 1), 0.05f));
        sm.Cast(10200001, false, new Vector3(40, 0, 0), 90f);
        Assert.That(sm.Queued, Is.Zero);
        Assert.That(states, Is.EqualTo(new[] { RemotePcStates.StateWalk, RemotePcStates.StateSkill }));
        Assert.That((sm.Cur, sm.SkillId, b.Position, b.RotationZ, sm.TargetYaw), Is.EqualTo((RemotePcStates.StateSkill, 10200001, new Vector3(40, 0, 0), 90f, 90f)));
        // FUN_1417db550 in StateSkill: a record without a skill id, or the same skill's, is dropped (not even placed)
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        Run(sm, 0.1f);
        sm.Push(Rec(RemotePcStates.StateSkill, 0, new Vector3(300, 0, 0), Vector2.Zero, 0f, skill: 10200001));
        Run(sm, 0.1f);
        Assert.That((sm.Cur, b.Position), Is.EqualTo((RemotePcStates.StateSkill, new Vector3(40, 0, 0))));
        // another skill's record is applied
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f, skill: 10200011));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateWalk));
        Assert.That(b.Current, Is.EqualTo(RemotePcStates.ActRun));
    }

    [Test] public void ChargingSkillTakesTheOtherStatesRecords() {
        // m_blockA4.m_bChargingSkill: a record not in StateSkill is applied (a landing record too); a StateSkill record
        // of the same skill is not
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, true, Vector3.Zero, 0f);
        sm.Push(Rec(RemotePcStates.StateSkill, 0, Vector3.Zero, Vector2.Zero, 0f, skill: 10200001));
        Run(sm, 0.1f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        sm.Push(Rec(RemotePcStates.StateJump, 0x21, Vector3.Zero, Vector2.Zero, 0f));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateJump));
        Assert.That(b.Played, Does.Contain(RemotePcStates.ActJumpUp));
    }

    [Test] public void TheSkillStateHooksFollowItsEntryItsExitAndEachAttackFired() {
        // the remote magics' cast record follows StateSkill: its entry (m_spRecord), its exit (stateskill.cpp:512-535, also on
        // a forced entry again) and FUN_1417ecce0 for each attack FireAttacksAtPoint fires
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        var log = new List<string>();
        sm.SkillEntered = () => log.Add("enter");
        sm.SkillExited = () => log.Add("exit");
        sm.AttackFired = a => log.Add("fire" + a);
        sm.Cast(10200001, false, Vector3.Zero, 0f, attackPoints: new[] { "Hit_01", "Hit_01", "Hit_02" });
        sm.CastTextKey("Hit_01", 0.1f);
        sm.CastTextKey("Hit_02", 0.2f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, attackPoints: new[] { "Hit_01" });
        sm.CastSequenceEnded();
        Assert.That(log, Is.EqualTo(new[] { "enter", "fire0", "fire1", "fire2", "exit", "enter", "exit" }));
    }

    [Test] public void TheSkillStateEndsInIdleAndTheNextStatePlaysItsSequence() {
        // a cast again in StateSkill re-enters it (forced); its last motion's end (EndOfSequence -> FUN_1417f0070) or a cancel
        // (State::vfunc_7(1)) enters StateIdle; a PC in the air then falls with the fall sequence
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        var states = new List<int>();
        sm.StateChanged = states.Add;
        sm.Cast(10200001, false, Vector3.Zero, 0f);
        sm.Cast(10200001, false, new Vector3(0, 0, 400), 0f);
        Assert.That(states, Is.EqualTo(new[] { RemotePcStates.StateSkill, RemotePcStates.StateSkill }));
        b.Played.Clear();
        sm.CastSequenceEnded();
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
        Run(sm, 0.3f);
        Assert.That(states, Is.EqualTo(new[] { RemotePcStates.StateSkill, RemotePcStates.StateSkill, RemotePcStates.StateIdle, RemotePcStates.StateFall }));
        Assert.That(b.Played, Does.Contain(RemotePcStates.ActJumpFall));
    }

    [Test] public void AnEarlierMotionsEndWaitsInTheSkillStateForTheNextMotionOrFiveSeconds() {
        // FUN_1417ee120: EndOfSequence ends the state only on the level's last motion (CSkillRecord::FUN_141759e40,
        // m_nMotion + 1 >= GetMotionCount); after motion 0 of 2 the PC stays in StateSkill, dropping the walk records,
        // until the next motion's cast or 5 s after the sequence end (m_fStartTime + 5 < GetGameTime)
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, new Vector3(40, 0, 0), 0f, motion: 0, motionCount: 2);
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        Run(sm, 4.8f);
        Assert.That((sm.Cur, b.Position), Is.EqualTo((RemotePcStates.StateSkill, new Vector3(40, 0, 0))));
        Run(sm, 0.3f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));

        // the next motion's cast (motion 1 of 2) re-enters the state and clears the wait; its end is the last motion's
        sm.Cast(10200001, false, Vector3.Zero, 0f, motion: 0, motionCount: 2);
        sm.CastSequenceEnded();
        Run(sm, 4f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, motion: 1, motionCount: 2);
        Run(sm, 2f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    static CastMove Linear(float dist, float keyMove = 0, Vector3 dir = default, int skillMoveType = 0, bool moveKey = false, bool magic = false)
        => new(0, keyMove, dist, 0f, skillMoveType, moveKey, magic, dir);

    [Test] public void LinearMotionMovesItsDistanceBetweenTheMoveKeys() {
        // CLinearMotionController: nothing before "move0" (the record has no m_fDuration); the key gives it
        // (move1 - move0) / speed = 0.5 s and the move covers movedistance (300) along the rotation's forward (90: +x)
        var b = new Body();
        b.CastKeys["move0"] = 0.2f; b.CastKeys["move1"] = 0.7f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, new Vector3(40, 0, 0), 90f, move: Linear(300f));
        Run(sm, 0.2f);
        Assert.That(b.Position, Is.EqualTo(new Vector3(40, 0, 0)));
        sm.CastTextKey("move0", 0.2f);
        Assert.That(sm.CastDuration, Is.EqualTo(0.5f).Within(1e-6f));
        Run(sm, 1f);
        Assert.That(b.Position.X, Is.EqualTo(340f).Within(0.01f));
        Assert.That(MathF.Abs(b.Position.Y), Is.LessThan(0.01f));
        Assert.That(b.LedgeAsked[0].dist, Is.EqualTo(300f / 30f).Within(1e-3f));   // speed * 100 of a 1/60 s step
        Assert.That((b.LedgeAsked[0].height, b.LedgeAsked[0].up), Is.EqualTo((750f, 0f)));
        // the key is armed once per entry (m_bMove0): a second "move0" starts no new move
        sm.CastTextKey("move0", 0.2f);
        Run(sm, 1f);
        Assert.That(b.Position.X, Is.EqualTo(340f).Within(0.01f));
    }

    [Test] public void LinearMotionWithoutADistanceTakesKeyMoveAlongThePacketDirection() {
        // CMotionController::vfunc_5: movedistance 0 -> the record's m_kStart direction and the motion's keyMove;
        // a movingInMovekey skill's duration is 0.05 s longer
        var b = new Body();
        b.CastKeys["move2"] = 0.1f; b.CastKeys["move3"] = 0.3f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, speed: 2f, move: Linear(0f, keyMove: 120f, dir: new Vector3(0, 2, 0), moveKey: true));
        sm.CastTextKey("move2", 0.1f);
        Assert.That(sm.CastDuration, Is.EqualTo(0.15f).Within(1e-6f));
        Run(sm, 0.5f);
        Assert.That(b.Position.Y, Is.EqualTo(120f).Within(0.01f));
    }

    [Test] public void ALedgeAheadStopsTheLinearMoveAndClearsItsTime() {
        // FUN_1416725c0 != 0: the record's m_fPad7c, m_fDuration and m_fDistance are cleared, no move
        var b = new Body { Ledge = true };
        b.CastKeys["move0"] = 0.2f; b.CastKeys["move1"] = 0.7f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 90f, move: Linear(300f));
        sm.CastTextKey("move0", 0.2f);
        Run(sm, 0.1f);
        Assert.That((b.Position, sm.CastDuration, sm.CastElapsed), Is.EqualTo((Vector3.Zero, 0f, 0f)));
    }

    [Test] public void ATiltedMoveAsksTheLedgeTestWithTheUnitTiltedDirection() {
        // moveangle != 0: FUN_1416725c0 takes the unit tilted direction and the step distance; its flat ray's direction
        // is normalized by CPhysXHelper::CastRay (physxhelper.cpp:207) with the same m_maxDist, as RemotePc.LedgeAhead casts it
        var b = new Body();
        b.CastKeys["move0"] = 0.2f; b.CastKeys["move1"] = 0.7f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 90f, move: new CastMove(0, 0, 300f, 30f, 0, false, false, default));
        sm.CastTextKey("move0", 0.2f);
        b.LedgeAsked.Clear();
        Run(sm, 0.1f);
        Assert.That(b.LedgeAsked, Is.Not.Empty);
        var (dir, dist, height, up) = b.LedgeAsked[0];
        Assert.That(dir.Length(), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(MathF.Abs(dir.Z), Is.EqualTo(0.5f).Within(1e-3f));   // sin 30
        Assert.That((height, up), Is.EqualTo((750f, 0f)));
        Assert.That(dist, Is.GreaterThan(0f));
    }

    [Test] public void MagicControlKeepsThePlaceUnlessTheCastHoldsAnActor() {
        // vfunc_4: m_n10 = 0x3c for magicControl without m_nB0, and FUN_1417edc20 returns at once; with m_nB0 it is 0x3b
        var b = new Body();
        b.CastKeys["move0"] = 0.2f; b.CastKeys["move1"] = 0.7f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 90f, move: Linear(300f, magic: true));
        sm.CastTextKey("move0", 0.2f);
        Run(sm, 1f);
        Assert.That(b.Position, Is.EqualTo(Vector3.Zero));
        sm.Cast(10200001, false, Vector3.Zero, 90f, 4242, "n", move: Linear(300f, magic: true));
        sm.CastTextKey("move0", 0.2f);
        Run(sm, 1f);
        Assert.That(b.Position.X, Is.EqualTo(300f).Within(0.01f));
    }

    [Test] public void MoveTypeTwoSkillsWalkByTheInputUnlessAMoveKeySkillRanOut() {
        // FUN_1417edc20: stateAttr moveType 2 / 3 adds CalcMoveDirectionNoYaw * vfunc_95 * dt (the sync's move input);
        // movingInMovekey without time left on the record (GetRemainingDuration 0) does not
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, move: new CastMove(2, 0, 0, 0, 2, false, false, default));
        sm.SkillSync(0f, new Vector2(0, 1), false, false);
        var dir = sm.MoveDirection();
        Assert.That(dir, Is.Not.EqualTo(Vector3.Zero));
        Run(sm, 1f);
        Assert.That(Vector3.Distance(b.Position, dir * sm.Speed), Is.LessThan(0.5f));
        b.Position = Vector3.Zero;
        sm.Cast(10200001, false, Vector3.Zero, 0f, move: new CastMove(2, 0, 0, 0, 2, true, false, default));
        sm.SkillSync(0f, new Vector2(0, 1), false, false);
        Run(sm, 1f);
        Assert.That(b.Position, Is.EqualTo(Vector3.Zero));
    }

    [Test] public void TheInputMoveReadsItsDirectionAfterTheMotionStepTurnedThePc() {
        // FUN_1417edc20: vfunc_68 of the controller's displacement turns the PC toward the target yaw first; the input
        // move's CalcMoveDirectionNoYaw (camera mode 0xe: the body's facing) comes after it (stateskill.cpp:2866-2869)
        var b = new Body();
        var sm = new RemotePcStates(b);
        var s = Rec(1, 1, Vector3.Zero, Vector2.Zero, 0f);
        s.CameraMode = 0xe;
        sm.Push(s); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, move: new CastMove(2, 0, 0, 0, 2, false, false, default));
        sm.SkillSync(0f, new Vector2(0, 1), false, false);
        sm.TargetYaw = 90f;
        var p0 = b.Position;
        sm.Update(1 / 60f);
        // each vfunc_68 call turns by fTime (pcbase.cpp:1866-1868): 30 deg after the motion step, 60 after the input step
        Assert.That(b.RotationZ, Is.EqualTo(60f).Within(1e-3f), "two vfunc_68 calls of the 1800 deg/s turn");
        var d = Vector3.Normalize(b.Position - p0);
        var facing30 = new Vector3(MathF.Sin(MathF.PI / 6), -MathF.Cos(MathF.PI / 6), 0);
        Assert.That(Vector3.Distance(d, facing30), Is.LessThan(1e-3f), "the step follows the facing after the first turn (sin 30, -cos 30)");
    }

    [Test] public void ArcMotionReachesItsDistanceInFourTenthsOfASecond() {
        // CArcMotionController: a bezier from the entry place to movedistance along the forward, its control point
        // halfway and 100 higher, walked by elapsed / 0.4
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 90f, move: new CastMove(1, 0, 200f, 0, 0, false, false, default));
        Run(sm, 0.2f);
        Assert.That(b.Position.X, Is.EqualTo(100f).Within(6f));     // halfway along x at t 0.5 (its lift is spent on gravity)
        Run(sm, 0.4f);
        Assert.That(b.Position.X, Is.EqualTo(200f).Within(0.5f));
        Assert.That(MathF.Abs(b.Position.Y), Is.LessThan(0.01f));
    }

    [Test] public void TheLoopRestartArmsTheMovePhaseThatSpansTheLoop() {
        // FUN_1417f1a90 after the "loopend" restart: move0 inside the loop is armed again and, spanning the loop exactly,
        // takes its duration at once
        var b = new Body();
        foreach (var (k, t) in new[] { ("loopstart", 0.2f), ("loopend", 0.7f), ("move0", 0.2f), ("move1", 0.7f) }) b.CastKeys[k] = t;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 90f, loopCount: 1, move: Linear(100f));
        sm.CastTextKey("loopstart", 0.2f);
        sm.CastTextKey("move0", 0.2f);
        Run(sm, 0.6f);
        Assert.That(b.Position.X, Is.EqualTo(100f).Within(0.01f));
        sm.CastTextKey("loopend", 0.7f);
        Assert.That((sm.CastDuration, sm.CastElapsed), Is.EqualTo((0.5f, 0f)));
        Run(sm, 0.6f);
        Assert.That(b.Position.X, Is.EqualTo(200f).Within(0.01f));
    }

    [Test] public void TheCastsTagKeysPlayTheirEffectsOutsideAndInsideTheLoop() {
        // StateSkill::TextKeyEvent (stateskill.cpp:792-834): a tag key plays its effect; inside the loop (m_b117) only a key
        // between "loopstart" and "loopend" does, and again each round
        var b = new Body();
        foreach (var (k, t) in new[] { ("e0", 0.1f), ("loopstart", 0.2f), ("e1", 0.3f), ("loopend", 0.7f), ("e2", 0.8f) }) b.CastKeys[k] = t;
        var sm = new RemotePcStates(b);
        var played = new List<string>();
        sm.TagEffect = (k, fx) => played.Add(k + "=" + fx);
        sm.Spawn(); Run(sm, 0.5f);
        var tags = new Dictionary<string, string> { ["e0"] = "a.xml", ["e1"] = "b.xml", ["e2"] = "c.xml" };
        sm.Cast(10200001, false, Vector3.Zero, 90f, loopCount: 1, tagEffects: tags);
        sm.CastTextKey("e0", 0.1f);
        sm.CastTextKey("loopstart", 0.2f);
        sm.CastTextKey("e1", 0.3f);
        sm.CastTextKey("loopend", 0.7f);            // a loop left: the next round, inside the loop again
        sm.CastTextKey("e1", 0.3f);
        sm.CastTextKey("loopend", 0.7f);            // the last round: out of the loop
        sm.CastTextKey("e2", 0.8f);
        Assert.That(played, Is.EqualTo(new[] { "e0=a.xml", "e1=b.xml", "e1=b.xml", "e2=c.xml" }));
        played.Clear();
        sm.Cast(10200001, false, Vector3.Zero, 90f, loopCount: 0, tagEffects: tags);
        sm.CastTextKey("loopstart", 0.2f);
        sm.CastTextKey("e2", 0.8f);                 // inside the loop, after "loopend": none
        sm.CastTextKey("e0", 0.1f);                 // before "loopstart": none
        Assert.That(played, Is.Empty);
    }

    [Test] public void AChargingCastsTagKeyBeforeTheAttackPlaysNoMoreAfterTheLoop() {
        // m_bLoop, here set by the sync's release (FUN_1417ec630, the sequence from "charging_attack"): a key before that key
        // plays nothing after, a key after it plays (TextKeyEvent :812-818)
        var b = new Body();
        foreach (var (k, t) in new[] { ("e0", 0.1f), ("charging_attack", 0.5f), ("e1", 0.6f) }) b.CastKeys[k] = t;
        var sm = new RemotePcStates(b);
        var played = new List<string>();
        sm.TagEffect = (k, _) => played.Add(k);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, true, Vector3.Zero, 90f, tagEffects: new Dictionary<string, string> { ["e0"] = "a", ["e1"] = "b" });
        sm.CastTextKey("e0", 0.1f);
        sm.SkillSync(90f, Vector2.Zero, false, true);
        sm.CastTextKey("e0", 0.1f);
        sm.CastTextKey("e1", 0.6f);
        Assert.That(played, Is.EqualTo(new[] { "e0", "e1" }));
    }

    [Test] public void ACastOnWaterGivesWayToTheSwim() {
        // StateSkill::vfunc_3: FUN_141675ed0 (the hit report's entity is water) -> vfunc_7(0x1b)
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, new Vector3(40, 0, 0), 0f);
        b.WaterFromX = 0f;
        Run(sm, 0.1f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSwim));
    }

    [Test] public void RepeatedMotionLoopsBetweenItsKeysThenEnds() {
        // FUN_1417f0940: m_nRepeat = loopCount (2); "loopend" with loops left takes one off and plays the sequence again
        // from the loop start plus the overrun past the loop (ClampedElapsedAfter16ms: 0.5 s loop at speed 1, 0.6 s since
        // "loopstart" -> 100 - 16, at most 50 ms); EndOfSequence waits while loops are left, the third pass ends the state
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 30f, loopCount: 2);
        Assert.That(sm.CastRepeat, Is.EqualTo(2));
        sm.CastTextKey("loopstart", 0.25f);
        Run(sm, 0.6f);
        b.RotationZ = 0f;
        sm.CastTextKey("loopend", 0.75f);
        Assert.That(sm.CastRepeat, Is.EqualTo(1));
        Assert.That(b.Seeks, Has.Count.EqualTo(1));
        Assert.That(b.Seeks[0], Is.EqualTo(0.30f).Within(1e-5f));
        Assert.That(b.RotationZ, Is.EqualTo(30f));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        // the restarted sequence throws "loopstart" again; a loop within 16 ms of its length seeks to the loop start
        sm.CastTextKey("loopstart", 0.25f);
        Run(sm, 0.5f);
        sm.CastTextKey("loopend", 0.75f);
        Assert.That((sm.CastRepeat, b.Seeks.Count), Is.EqualTo((0, 2)));
        Assert.That(b.Seeks[1], Is.EqualTo(0.25f).Within(1e-5f));
        sm.CastTextKey("loopstart", 0.25f);
        sm.CastTextKey("loopend", 0.75f);
        Assert.That(b.Seeks, Has.Count.EqualTo(2));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    [Test] public void RemoteChargingSkillLoopsUntilTheSyncReleasesItToTheChargingAttack() {
        // another player's charging skill loops 20 more times (m_nRepeat += 0x14); the sync's release (A false, B true)
        // plays the sequence from "charging_attack" and ends the loop (m_nRepeat 0, m_bLoop): "loopstart" no longer marks,
        // "loopend" no longer repeats and the sequence end leaves the state
        var b = new Body();
        b.CastKeys["charging_attack"] = 1.2f;
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, true, Vector3.Zero, 0f, attackPoints: new[] { "Attack_01" });
        Assert.That(sm.CastRepeat, Is.EqualTo(20));
        sm.CastTextKey("loopstart", 0.3f);
        sm.CastTextKey("loopend", 0.9f);
        Assert.That((sm.CastRepeat, b.Seeks.Count), Is.EqualTo((19, 1)));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        sm.SkillSync(45f, new Vector2(1, 0), false, true);
        Assert.That(sm.CastRepeat, Is.EqualTo(0));
        Assert.That(b.Seeks[^1], Is.EqualTo(1.2f));
        Assert.That(sm.MoveInput, Is.EqualTo(new Vector2(1, 0)));
        sm.CastTextKey("Attack_01", 1.3f);
        sm.CastTextKey("loopend", 0.9f);
        Assert.That(b.Seeks, Has.Count.EqualTo(2));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    [Test] public void SyncChargeRestartsTheLoopAfterItsEndAndHoldsTheSequenceEnd() {
        // FUN_1417ec630(true, ..): m_bCharging; after a "loopend" (m_fChargeTime set) the PC turns to the record rotation
        // and the sequence plays again from the loop start; before it, the charge makes "loopend" repeat and EndOfSequence wait
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f);
        sm.CastTextKey("loopstart", 0.4f);
        sm.SkillSync(0f, Vector2.Zero, true, false);
        Assert.That(b.Seeks, Is.Empty);
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateSkill));
        sm.CastTextKey("loopend", 0.8f);
        Assert.That(b.Seeks, Is.EqualTo(new[] { 0.4f }));
        // the loop's end passed without a charge: a charge now restarts the loop at its start, turned to the new rotation
        sm.CastTextKey("loopstart", 0.4f);
        sm.CastTextKey("loopend", 0.8f);
        Assert.That(b.Seeks, Has.Count.EqualTo(1));
        sm.SkillSync(60f, Vector2.Zero, true, false);
        Assert.That(b.Seeks, Is.EqualTo(new[] { 0.4f, 0.4f }));
        Assert.That((b.RotationZ, sm.TargetYaw), Is.EqualTo((60f, 60f)));
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    // an npc's node at (100, 200, 300), turned 30 degrees about MS2 Z: m[0] = (cos, -sin, 0) for R_z(30)
    static HoldTarget NpcNode(int npcId = 0x5f5e101) {
        float c = MathF.Cos(MathF.PI / 6), sn = MathF.Sin(MathF.PI / 6);
        return new HoldTarget { Kind = HoldKind.Npc, NpcId = npcId, HasNode = true, NodePosition = new Vector3(100, 200, 300), NodeRow0 = new Vector3(c, -sn, 0) };
    }

    [Test] public void CastHoldingAnActorEndsInHoldOnItsNode() {
        // FUN_1417f0070: a record with m_nB0 enters StateHold (0x20) with m_nB0 / m_strB8; StateHold::vfunc_3 puts the PC on
        // the node turned by -z degrees of ToEulerAnglesXYZ, without gravity; vfunc_9 plays HOLD_IDLE (0x3e)
        var b = new Body { Hold = NpcNode() };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, 4242, "Bip01 Spine");
        sm.CastSequenceEnded();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHold));
        Assert.That(sm.HoldTargetId, Is.EqualTo(4242)); Assert.That(sm.HoldNode, Is.EqualTo("Bip01 Spine"));
        Run(sm, 0.5f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHold));
        Assert.That(b.HoldAsked, Is.EqualTo((4242, "Bip01 Spine")));
        Assert.That(b.Position, Is.EqualTo(new Vector3(100, 200, 300)));
        Assert.That(b.RotationZ, Is.EqualTo(30f).Within(1e-3));
        Assert.That(b.Current, Is.EqualTo(RemotePcStates.ActHoldIdle));
        // the node moves: the PC follows it
        b.Hold.NodePosition = new Vector3(150, 200, 320);
        Run(sm, 1 / 60f);
        Assert.That(b.Position, Is.EqualTo(new Vector3(150, 200, 320)));
    }

    [Test] public void HoldOnAGimbalEdgeKeepsTheRotation() {
        // ToEulerAnglesXYZ returns false at |y| >= pi/2 - 1e-3: vfunc_21 is not called, the position still follows
        var b = new Body { Hold = NpcNode() };
        b.Hold.NodeRow0 = new Vector3(0, 0, -1);
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        b.RotationZ = 12f;
        sm.Cast(10200001, false, Vector3.Zero, 12f, 4242, "n");
        sm.CastSequenceEnded();
        Run(sm, 0.1f);
        Assert.That(b.RotationZ, Is.EqualTo(12f).Within(1e-4));
        Assert.That(b.Position, Is.EqualTo(new Vector3(100, 200, 300)));
    }

    static HoldTarget PcSeat(bool seat = true) => new HoldTarget {
        Kind = HoldKind.Pc, HasNode = true, NodePosition = new Vector3(1, 2, 3), NodeRow0 = new Vector3(1, 0, 0),
        HasSeat = seat, SeatPosition = new Vector3(100, 200, 300), SeatRow0 = new Vector3(0, -1, 0),
        RideeState = RemotePcStates.StateWalk, RideeSub = 0x17, RideeAnimSpeed = 1.5f,
        Seat = new RiderSequences("pc", "idle", "run", "jump", "spIdle", "spRun", "spJump"),
    };

    [Test] public void HoldOnAPcsMountSitsOnTheSeatAndPlaysItsSequence() {
        // vfunc_3: Ridee::FUN_1417083a0 puts the passenger on the seat, z = atan2(m01, m00) * -57.2957764 (row 0 (0, -1, 0):
        // -90 deg -> 90); vfunc_9: walk sub 0x17 plays the seat's SP run
        var b = new Body { Hold = PcSeat() };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Push(Rec(RemotePcStates.StateHold, 0, Vector3.Zero, Vector2.Zero, 0.05f, hold: 777, holdNode: "Ride_B"));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHold));
        Assert.That(b.Position, Is.EqualTo(new Vector3(100, 200, 300)));
        Assert.That(b.RotationZ, Is.EqualTo(90f).Within(1e-3));
        Assert.That(b.Sequences[^1], Is.EqualTo("spRun"));
    }

    [Test] public void HoldOnAPcWithoutASeatStaysPut() {
        // vfunc_3 / vfunc_9 return for a PC without a ridee, data or seat model: no move, no sequence, no fallback
        var b = new Body { Hold = PcSeat(seat: false) };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        b.Position = new Vector3(5, 6, 7);
        sm.Push(Rec(RemotePcStates.StateHold, 0, new Vector3(5, 6, 7), Vector2.Zero, 0.05f, hold: 777, holdNode: "Ride_B"));
        int seqs = b.Sequences.Count;
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHold));
        Assert.That(b.Position, Is.EqualTo(new Vector3(5, 6, 7)));
        Assert.That(b.Sequences.Count, Is.EqualTo(seqs));
    }

    [Test] public void SeatOnAGimbalEdgeTurnsToZero() {
        // FUN_140a0b1f0 applies the angles whether unique or not: the non-unique branch sets z = 0
        Assert.That(RemotePcStates.EulerZ(new Vector3(0, 0, -1), out float z), Is.False);
        Assert.That(z, Is.EqualTo(0f));
        var b = new Body { Hold = PcSeat() with { SeatRow0 = new Vector3(0, 0, 1) } };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        b.RotationZ = 33f;
        sm.Push(Rec(RemotePcStates.StateHold, 0, Vector3.Zero, Vector2.Zero, 0.05f, hold: 777, holdNode: "Ride_B", yaw: 33f));
        sm.Update(1 / 60f);
        Assert.That(b.RotationZ, Is.EqualTo(0f).Within(1e-4));
    }

    [Test] public void HoldWithoutTheActorOrNodeFallsBackToTheJump() {
        // StateHold::ChangeState: no actor, no node or a dead actor -> vfunc_59(6, 0, 0x22, 0)
        foreach (var t in new[] { default, NpcNode() with { HasNode = false }, NpcNode() with { Dead = true } }) {
            var b = new Body { Hold = t };
            var sm = new RemotePcStates(b);
            sm.Spawn(); Run(sm, 0.5f);
            sm.Cast(10200001, false, Vector3.Zero, 0f, 4242, "n");
            sm.CastSequenceEnded();
            var seen = new List<(int, int)>();
            sm.StateChanged = st => seen.Add((st, sm.Sub));
            sm.Update(1 / 60f);
            Assert.That(seen, Has.Member((RemotePcStates.StateJump, 0x22)), $"{t.Kind} node {t.HasNode} dead {t.Dead}");
            Assert.That(sm.Cur, Is.Not.EqualTo(RemotePcStates.StateHold));
        }
    }

    [Test] public void HoldRecordEntersHoldWithItsTarget() {
        // StateSyncObject::vfunc_2's optional bit 5 (m_n90, m_str98) -> StateHoldData::vfunc_2
        var b = new Body { Hold = NpcNode() };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Push(Rec(RemotePcStates.StateHold, 0, Vector3.Zero, Vector2.Zero, 0.05f, hold: 777, holdNode: "Dummy01"));
        sm.Update(1 / 60f);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateHold));
        Assert.That(sm.HoldTargetId, Is.EqualTo(777)); Assert.That(sm.HoldNode, Is.EqualTo("Dummy01"));
        Assert.That(b.Position, Is.EqualTo(new Vector3(100, 200, 300)));
    }

    [Test] public void HoldPlaysTheNpcsSequenceAndLeavesWithItsRotation() {
        // vfunc_9: npc 0xa7e02a "Ride_Idle_U", 0xadf341 "Idle_A"; vfunc_5 turns back to the rotation kept on entry
        foreach (var (npc, seq) in new[] { (0xa7e02a, "Ride_Idle_U"), (0xadf341, "Idle_A"), (0xadf342, "Idle_A") }) {
            var b = new Body { Hold = NpcNode(npc) };
            var sm = new RemotePcStates(b);
            sm.Spawn(); Run(sm, 0.5f);
            b.RotationZ = 45f;
            sm.Cast(10200001, false, Vector3.Zero, 45f, 4242, "n");
            sm.CastSequenceEnded();
            Run(sm, 0.2f);
            Assert.That(b.Sequences, Is.EqualTo(new[] { seq }));
            Assert.That(b.RotationZ, Is.EqualTo(30f).Within(1e-3));
            sm.Push(Rec(1, 0, b.Position, Vector2.Zero, 0.05f, yaw: 45f));
            sm.Update(1 / 60f);
            Assert.That(sm.Cur, Is.Not.EqualTo(RemotePcStates.StateHold));
        }
    }

    [Test] public void CancelledCastHoldingAnActorGoesToIdle() {
        // OnSkillCancel -> State::vfunc_7(1): StateIdle, not the hold
        var b = new Body { Hold = NpcNode() };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, 4242, "n");
        sm.CancelSkill();
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }

    [Test] public void HoldAllocatesNothingPerFrame() {
        var b = new Body { Hold = NpcNode() };
        var sm = new RemotePcStates(b);
        sm.Spawn(); Run(sm, 0.5f);
        sm.Cast(10200001, false, Vector3.Zero, 0f, 4242, "n");
        sm.CastSequenceEnded();
        for (int i = 0; i < 60; i++) sm.Update(1 / 60f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) sm.Update(1 / 60f);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(256));
    }

    [Test] public void UpdateAllocatesNothingPerFrame() {
        var b = new Body();
        var sm = new RemotePcStates(b);
        sm.Spawn();
        sm.Push(Rec(2, 0x18, Vector3.Zero, new Vector2(0, 1), 0f));
        for (int i = 0; i < 120; i++) sm.Update(1 / 60f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) sm.Update(1 / 60f);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(256));
    }

    [Test] public void UserStateTwelveEntersTheDeathStateWithoutTheFlag() {
        // OnUserState (scenestategame.cpp:5759-5771): vfunc_59(12) first, so PC::vfunc_137 finds state 0xc and returns
        var sm = new RemotePcStates(new Body());
        sm.Spawn(); Run(sm, 0.1f);
        Assert.That(sm.UserState(12, 0, 100), Is.False);
        Assert.That(sm.Cur, Is.EqualTo(0xc));
        Assert.That(sm.Dead, Is.False);
    }

    [Test] public void DeadUserSetsTheFlagOnlyWithNoHealthOutsideTheDeathState() {
        // PC::vfunc_137 (pc.cpp:4593-4607): GetCurStateId() != 0xc and FUN_141670a00(4) == 0
        var sm = new RemotePcStates(new Body());
        sm.Spawn(); Run(sm, 0.1f);
        Assert.That(sm.Die(5), Is.False);
        Assert.That(sm.Dead, Is.False);
        Assert.That(sm.Die(0), Is.True);
        Assert.That(sm.Dead, Is.True);
        var inDeath = new RemotePcStates(new Body());
        inDeath.ChangeState(0xc);
        Assert.That(inDeath.Die(0), Is.False);
        Assert.That(inDeath.Dead, Is.False);
    }

    [Test] public void ADeadPcTakesOnlyTheDeathRevivalAndObserverStates() {
        // PC::vfunc_124 (pc.cpp:1198-1205): dead, or alive with no health under a max, lets only 0xc / 0x18 / 0x3f through
        var sm = new RemotePcStates(new Body());
        sm.Spawn(); Run(sm, 0.1f);
        sm.SetDead(true);
        sm.UserState(RemotePcStates.StateWalk, 50, 100);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateLand));
        sm.UserState(0x18, 50, 100);
        Assert.That(sm.Cur, Is.EqualTo(0x18));
        // OnRevival (scenestategame.cpp:7398): FUN_14166ce50(0), then states pass again
        sm.SetDead(false);
        sm.UserState(RemotePcStates.StateIdle, 50, 100);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
        // alive with no health and a max: still held; no max health (FUN_141670a30(4) <= 0): let through
        sm.UserState(RemotePcStates.StateWalk, 0, 100);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateIdle));
        sm.UserState(RemotePcStates.StateWalk, 0, 0);
        Assert.That(sm.Cur, Is.EqualTo(RemotePcStates.StateWalk));
    }

    [Test] public void AMountedPcPassesServerStatesToItsRidee() {
        // PCBase::vfunc_59 (pcbase.cpp:1097-1111): in StateRide with m_spB10 the state goes to the ridee; OnUserState 12
        // then runs vfunc_137 on the rider, which is not in the death state, so its dead flag is set
        var rider = new RemotePcStates(new Body());
        var ridee = new RemotePcStates(new Body());
        rider.Spawn(); ridee.Spawn();
        rider.RideOn(ridee, 100, 100);
        Assert.That((rider.Cur, rider.Ridee), Is.EqualTo((RemotePcStates.StateRide, ridee)));
        Assert.That(rider.UserState(RemotePcStates.StateDead, 0, 100), Is.True);
        Assert.That((rider.Cur, ridee.Cur, rider.Dead), Is.EqualTo((RemotePcStates.StateRide, RemotePcStates.StateDead, true)));
        // vfunc_121: m_spB10 cleared first, then vfunc_59(1) through vfunc_124, which a dead pc refuses
        rider.RideOff(true, 0, 100);
        Assert.That((rider.Cur, rider.Ridee), Is.EqualTo((RemotePcStates.StateRide, (RemotePcStates)null)));
    }

    [Test] public void ADeadPcStaysOutOfTheRideAndALivingOneGetsOff() {
        // FieldAddUser with no health and a ride: Ridee::vfunc_10's vfunc_59(0x21) is refused by PC::vfunc_124
        var rider = new RemotePcStates(new Body());
        var ridee = new RemotePcStates(new Body());
        rider.Spawn();
        rider.SetDead(true);
        rider.RideOn(ridee, 0, 100);
        Assert.That((rider.Cur, rider.Ridee), Is.EqualTo((RemotePcStates.StateFall, ridee)));
        var live = new RemotePcStates(new Body());
        live.Spawn();
        live.RideOn(ridee, 50, 100);
        live.RideOff(false, 50, 100);
        Assert.That((live.Cur, live.Ridee), Is.EqualTo((RemotePcStates.StateFall, (RemotePcStates)null)));
        live.RideOn(ridee, 50, 100);
        live.RideOff(true, 50, 100);
        Assert.That(live.Cur, Is.EqualTo(RemotePcStates.StateIdle));
    }
}
