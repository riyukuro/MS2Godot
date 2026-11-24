using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_move.tscn) of an npc's sync movement in NpcLayer, as CNpc::ApplySyncBlock (@0x1410918e0) and
/// CNpc::vfunc_53 (@0x1410952f0) move it:
/// - a block puts the npc at its position (raised by the packet lift) and the block velocity moves it on every frame;
/// - the rotate flag snaps the yaw;
/// - on a Run_ sequence an npc 40 short of the block position stays put, and its velocity is scaled by the clamped catch-up ratio (1.2);
/// - the Run_ animation plays at the row's anispeed times PCBase::vfunc_97 times the unclamped catch-up ratio; an npc made with the row's
///   model scale at 1.5 (no scale buff) plays it at 2 - 1/1.5, and vfunc_97's four branches give the client's rates;
/// - a block speed of 0 on a one-shot sequence the model has is kept, not taken as 1 (a model without one fails); a death after it
///   plays at 1 and ends, so the exp is shown; the idle queued after a one-shot played at 2 starts at 1;
/// - a jump sequence stops the velocity and keeps the yaw;
/// - a block of the spawn state (0x11) hides the npc for its delay without playing its own sequence, then shows it with the
///   spawn point's sequence and ends in idle; a spawn block while in the state does nothing; one naming no point idles at once;
/// - the yaw turns at most the row's rotationSpeed per second (CNpc::TurnTowardTargetYaw @0x141099170);
/// - Process allocates nothing per frame.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NpcMoveCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var table = new Ms2.Formats.World.NpcTable(packs);
        var npcs = new NpcLayer(packs, builder, table, this);
        int npcId = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 11000001 })
            if (table.Find(id) != null) { npcId = id; break; }
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        Check(npcId != 0, "no npc row");
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 1, NpcId = npcId }));
        float lift = World.FieldNpcs.PacketLift;
        var row = table.Find(npcId);
        void Block(short x, short vx, short seq = -1, bool rotates = false, float yaw = 0, int obj = 1, float speed = 1) =>
            npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Flags = (byte)(rotates ? 1 : 0), Position = new Vector3S(x, 0, 0), Velocity = new Vector3S(vx, 0, 0), RotationZ = yaw, Sequence = seq, AnimSpeed = speed }));
        float Yaw() => Mathf.RadToDeg(npcs.Info(1)?.yaw ?? float.NaN);
        Vector3 At() => npcs.Info(1)?.root.Position ?? new Vector3(float.NaN, 0, 0);
        bool Near(Vector3 a, float x) => (a - new Vector3(x, lift, 0)).Length() < 0.01f;

        Block(100, 50, rotates: true, yaw: 90);
        Check(Near(At(), 100), $"the block did not place the npc: {At()}");
        Check(Mathf.Abs((npcs.Info(1)?.yaw ?? 0) - Mathf.DegToRad(90)) < 0.001f, $"the rotate flag did not snap the yaw: {npcs.Info(1)?.yaw}");
        npcs.Process(0.5f);
        Check(Near(At(), 125), $"velocity 50 for 0.5 s did not move the npc to 125: {At()}");
        npcs.Process(0.5f);
        Check(Near(At(), 150), $"the velocity did not keep moving the npc to 150: {At()}");

        int run = npcs.SequenceId(1, "Run_A");
        if (run < 0) GD.Print("npc move: no Run_A sequence, the catch-up part is skipped");
        else
        {
            Block(0, 0);
            Check(Near(At(), 0), $"the block did not place the npc at 0: {At()}");
            // ahead = 40 + 200 * 0.3 = 100, 100 from the npc and 60 from the block: ratio 1.67, clamped to 1.2
            Block(40, 200, (short)run);
            Check(Near(At(), 0), $"the catch-up block placed the npc: {At()}");
            float anim = npcs.AnimSpeed(1);
            float rate = NpcLayer.ScaleRate(1f, row.Scale);
            Check(Mathf.Abs(anim - row.AniSpeed * rate * (100f / 60f)) < 0.001f, $"the catch-up animation speed {anim} is not anispeed {row.AniSpeed} x the scale rate {rate} x ratio 1.67");
            npcs.Process(0.25f);
            Check(Near(At(), 60), $"the catch-up velocity is not 240: {At()} after 0.25 s");

            // the same row with model scale 1.5 (m_fScaleAC) and no scale buff (m_fScaleA8 1): Run_ at anispeed x (2 - 1/1.5), on the block in place
            float scale0 = row.Scale;
            row.Scale = 1.5f;
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 2, NpcId = npcId }));
            row.Scale = scale0;
            Block(0, 0, obj: 2);
            Block(0, 100, (short)run, obj: 2);
            float scaled = npcs.AnimSpeed(2);
            Check(Mathf.Abs(scaled - row.AniSpeed * (2f - 1f / 1.5f)) < 0.001f, $"the scale 1.5 npc's Run_ speed {scaled} is not anispeed {row.AniSpeed} x 1.333");
        }
        // the scale rate's branches (buff scale against model scale)
        foreach (var (buff, model, rateWant) in new[] { (1f, 1f, 1f), (1f, 3f, 1.5f), (1f, 1.5f, 2f - 1f / 1.5f), (1f, 0.5f, 0.5f), (1f, 0.8f, 1f - 0.2f / 1.6f), (1f, 0.4f, 0.5f) })
            Check(Mathf.Abs(NpcLayer.ScaleRate(buff, model) - rateWant) < 1e-5f, $"the scale rate of buff {buff}, model {model} is {NpcLayer.ScaleRate(buff, model)}, want {rateWant}");

        // a block speed of 0 on a one-shot sequence: PlayAnimation keeps it (the sequence's frequency is 0)
        int oneShot = npcs.SequenceId(1, OneShot);
        Check(oneShot >= 0, $"npc {npcId} has no one-shot sequence for the speed 0 block");
        if (oneShot >= 0)
        {
            Block(0, 0, (short)oneShot, speed: 0);
            Check(npcs.AnimSpeed(1) == 0, $"a block speed of 0 played the sequence at {npcs.AnimSpeed(1)}");
        }

        // the turn: snapped to 0, then a target of 90 without the snap flag turns at the row's cap (or closes 10x the gap per second)
        Block(0, 0, rotates: true, yaw: 0);
        Block(0, 0, yaw: 90);
        npcs.Process(0.05f);
        float want = Mathf.Min(90f / 0.1f, row.RotationSpeed) * 0.05f;
        Check(Mathf.Abs(Yaw() - want) < 0.01f, $"the turn step is {Yaw()}, want {want} (rotationSpeed {row.RotationSpeed})");

        Block(0, 0, rotates: true, yaw: 0);
        // a jump block without the arc data: from the block position (4 up) to its to point (2 up) in 0.45 s, the peak
        // 150 / 212.132 of the distance (CNpc +0xdb0); the block's yaw and snap do not apply, its velocity moves the npc after
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Flags = 1, Position = new Vector3S(300, 0, 0), Velocity = new Vector3S(100, 0, 0),
            RotationZ = 45, Sequence = -2, AnimSpeed = 1, JumpTo = new System.Numerics.Vector3(300, 200, 0) }));
        Check(Mathf.Abs(Yaw()) < 0.01f, $"the jump block snapped the yaw to {Yaw()}");
        npcs.Process(0.225f);
        float peak = 150f / 212.132f * new System.Numerics.Vector3(0, 200, -2).Length();
        Check((At() - new Vector3(300, 3 + peak, -100)).Length() < 0.01f, $"the arc's middle is {At()}, want (300, {3 + peak}, -100)");
        npcs.Process(0.5f);
        Check((At() - new Vector3(300, 2, -200)).Length() < 0.01f, $"the arc did not end at its to point: {At()}");
        npcs.Process(0.5f);
        Check((At() - new Vector3(350, 2, -200)).Length() < 0.01f, $"the block velocity did not move the npc after the arc: {At()}");

        // in the hit state (0xd) a block keeps the npc's own height
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Position = new Vector3S(0, 0, 0), State = 0xd, Serial = 77, Sequence = -1, AnimSpeed = 1 }));
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Position = new Vector3S(10, 0, 50), Serial = 77, Sequence = -1, AnimSpeed = 1 }));
        Check(Near(At(), 10), $"the hit state did not keep the npc's height: {At()}");
        // the block that leaves it is placed in the hit state still (its state is entered after the placement), the next one is not
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Position = new Vector3S(10, 0, 50), State = 1, Serial = 78, Sequence = -1, AnimSpeed = 1 }));
        Check(Near(At(), 10), $"the block that leaves the hit state did not keep the height: {At()}");
        npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = 1, Position = new Vector3S(10, 0, 50), State = 1, Serial = 78, Sequence = -1, AnimSpeed = 1 }));
        Check((At() - new Vector3(10, 50 + lift, 0)).Length() < 0.01f, $"out of the hit state the block did not place the height: {At()}");

        // the spawn state (0x11): a block with a spawn id hides the npc for 100-400 ms (StateSpawn), plays no sequence of its own,
        // then shows it with the spawn point's sequence and goes to idle at its length; a spawn block while in the state changes nothing
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 4, NpcId = npcId }));
        string spawnSeq = npcs.SequenceId(4, "Regen_A") >= 0 ? "Regen_A" : null;
        npcs.SequenceId(4, n => { if (spawnSeq == null && OneShot(n)) spawnSeq = n; return spawnSeq == n; });
        npcs.SpawnAnimation = id => id == 55 ? spawnSeq : null;
        void Spawn(int obj, ushort serial, int spawnId, short seq = -1) => npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Position = new Vector3S(0, 0, 0),
            State = 0x11, Serial = serial, Sequence = seq, AnimSpeed = 1, Tail = System.BitConverter.GetBytes(spawnId) }));
        string before = npcs.CurrentAnimation(4);
        int other = npcs.SequenceId(4, n => n != spawnSeq && OneShot(n));
        Spawn(4, 5, 55, (short)other);
        Check(!npcs.BodyShown(4) && npcs.StateOf(4) == 0x11, $"the spawn block did not hide the npc in the spawn state: shown {npcs.BodyShown(4)}, state {npcs.StateOf(4)}");
        Check(npcs.CurrentAnimation(4) == before, $"the spawn block played its own sequence {npcs.CurrentAnimation(4)}");
        npcs.Process(0.05f);
        Check(!npcs.BodyShown(4), "the npc was shown before the spawn delay");
        npcs.Process(0.4f);
        Check(npcs.BodyShown(4), "the npc was not shown after the spawn delay");
        Check(spawnSeq == null || npcs.CurrentAnimation(4) == "s" + npcs.SequenceId(4, spawnSeq), $"the spawn point's sequence {spawnSeq} did not play: {npcs.CurrentAnimation(4)}");
        Spawn(4, 6, 99);
        Check(npcs.BodyShown(4), "a spawn block in the spawn state entered it again");
        npcs.Process(30f);
        Check(npcs.StateOf(4) == 1, $"the spawn state did not end in idle: {npcs.StateOf(4)}");
        Spawn(4, 7, 99);
        Check(!npcs.BodyShown(4), "a spawn block naming no point did not hide the npc");
        npcs.Process(0.45f);
        Check(npcs.BodyShown(4) && npcs.StateOf(4) == 1, $"without a point the spawn state did not show and idle after the delay: shown {npcs.BodyShown(4)}, state {npcs.StateOf(4)}");

        Block(300, 100);
        npcs.Process(0.016f);
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) npcs.Process(0.016f);
        long alloc = System.GC.GetAllocatedBytesForCurrentThread() - a0;
        Check(alloc == 0, $"Process allocated {alloc} B in 100 frames");
        string head = $"npc move: npc {npcId}, run {run}, spawn sequence {spawnSeq ?? "none"}, anispeed {row.AniSpeed}, scale {row.Scale}, rotationSpeed {row.RotationSpeed}, {alloc} B per 100 frames";
        AfterFrames(npcs, npcId, fails, head);
    }

    static bool OneShot(string n) => !n.StartsWith("Idle", System.StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Walk", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Run", System.StringComparison.OrdinalIgnoreCase) && !n.Contains("Idle", System.StringComparison.OrdinalIgnoreCase)
        && !n.StartsWith("Dead", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The checks that need the animation player to run: a death after a speed 0 block ends (its exp is shown), and the
    /// idle queued after a one-shot at speed 2 starts at 1.</summary>
    async void AfterFrames(NpcLayer npcs, int npcId, System.Collections.Generic.List<string> fails, string head)
    {
        void Block(int obj, short seq, float speed) =>
            npcs.Move(new NpcMovedEvent(0, new NpcSync { ObjectId = obj, Position = new Vector3S(0, 0, 0), Sequence = seq, AnimSpeed = speed }));
        async System.Threading.Tasks.Task<bool> Until(System.Func<bool> done, int frames)
        {
            for (int f = 0; f < frames && !done(); f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return done();
        }
        string extra = "";
        // death after a speed 0 block
        npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = 3, NpcId = npcId }));
        int shot = npcs.SequenceId(3, OneShot);
        long shown = 0;
        npcs.ExpShown += (_, exp) => shown = exp;
        if (shot >= 0)
        {
            Block(3, (short)shot, 0);
            int noDeath = npcs.NoDeathSequence;
            npcs.AddExp(3, 7);
            npcs.Die(3, true);
            if (npcs.NoDeathSequence != noDeath) fails.Add($"npc {npcId} has no death sequence to play after the speed 0 block");
            else
            {
                if (npcs.AnimSpeed(3) != 1f) fails.Add($"the death sequence after a speed 0 block plays at {npcs.AnimSpeed(3)}");
                if (!await Until(() => shown != 0, 600)) fails.Add("the death sequence after a speed 0 block never ended: the exp text is lost");
            }
        }
        // the idle queued after a one-shot at speed 2: the first candidate npc with Idle_A and a one-shot sequence
        int obj = 10, idleNpc = 0;
        foreach (int id in new[] { 21000001, 21000002, 21000003, 21000004, 21000005, 11000001, 11000002, 11000003, 11000004, 11000005 })
        {
            npcs.Add(new NpcAddedEvent(0, new FieldAddNpc { ObjectId = ++obj, NpcId = id }));
            if (npcs.SequenceId(obj, "Idle_A") >= 0 && npcs.SequenceId(obj, OneShot) >= 0) { idleNpc = id; break; }
        }
        if (idleNpc == 0) fails.Add("no candidate npc has Idle_A and a one-shot sequence");
        else
        {
            int s2 = npcs.SequenceId(obj, OneShot);
            Block(obj, (short)s2, 2f);
            bool two = npcs.AnimSpeed(obj) == 2f;
            bool idled = await Until(() => npcs.CurrentAnimation(obj) == "s" + npcs.SequenceId(obj, "Idle_A"), 900);
            float sp = npcs.AnimSpeed(obj);
            if (!two || !idled || sp != 1f) fails.Add($"npc {idleNpc}: one-shot at 2 {two}, queued idle started {idled}, at speed {sp}, want 1");
            extra = $", queued idle npc {idleNpc} at {sp}";
        }
        GD.Print($"{head}, death exp shown {shown}{extra}, {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
