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
        public float SwimSpeed => 350f;
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
        // 100 or more away: put there
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 0), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.X, Is.EqualTo(300).Within(1));
        // a height difference over 200 snaps even when close
        sm.Push(Rec(2, 0x18, new Vector3(300, 0, 250), new Vector2(0, 1), 0f));
        sm.Update(1e-4f);
        Assert.That(b.Position.Z, Is.GreaterThan(200));
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
}
