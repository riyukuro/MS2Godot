using System.Collections.Concurrent;
using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net.Replay;

public abstract record ReplayEvent(double Time);
public sealed record FieldEnterEvent(double Time, RequestFieldEnter Enter) : ReplayEvent(Time);
public sealed record UserAddedEvent(double Time, FieldAddUser User, bool IsSelf) : ReplayEvent(Time);
public sealed record UserRemovedEvent(double Time, int ObjectId) : ReplayEvent(Time);
/// <summary>One movement record of a player. Position is in the packet's own units (MS2 file coordinates, Z up).</summary>
public sealed record UserMovedEvent(double Time, int ObjectId, bool IsSelf, Vector3 Position, float YawDegrees, StateSync Record) : ReplayEvent(Time);

public sealed record NpcAddedEvent(double Time, FieldAddNpc Npc) : ReplayEvent(Time);
public sealed record NpcRemovedEvent(double Time, int ObjectId, bool Dead) : ReplayEvent(Time);
/// <summary>One npc sync block (position, rotation, animation sequence id, state).</summary>
public sealed record NpcMovedEvent(double Time, NpcSync Sync) : ReplayEvent(Time);

public sealed record SkillUsedEvent(double Time, SkillUse Use) : ReplayEvent(Time);
public sealed record SkillCancelledEvent(double Time, int ActorId, long CastUid) : ReplayEvent(Time);
public sealed record SkillDamageEvent(double Time, SkillDamage Damage) : ReplayEvent(Time);
/// <summary>An npc's health from a Stat packet: current and maximum.</summary>
public sealed record NpcHpEvent(double Time, int ObjectId, long Current, long Max) : ReplayEvent(Time);

/// <summary>
/// Turns packets into field events and keeps the current player table. Fed from the read loop (inbound packets) and from the
/// recording's own client packets (the local player's movement); events can be drained from another thread.
/// Yaw: Rotation2 / 10 is degrees about +Z with 90 meaning facing +X (checked against the recorded walking direction).
/// </summary>
public sealed class FieldTracker {
    public sealed class Actor {
        public int ObjectId; public string Name; public byte Gender; public bool IsSelf;
        public Vector3 Position; public float YawDegrees; public int Moves;
        public FieldAddUser Add;
    }

    public sealed class NpcState { public int ObjectId, NpcId, Moves; public Vector3 Position; public float YawDegrees; }

    readonly object gate = new();
    readonly Dictionary<int, NpcState> npcs = new();
    /// <summary>The npc table's view of an npc id (boss layout, property tag). Without it every npc reads as a plain one.</summary>
    public Func<int, NpcLayout> NpcLayoutOf { get; set; } = _ => default;
    public IReadOnlyList<NpcState> NpcSnapshot() { lock (gate) return npcs.Values.Select(n => new NpcState { ObjectId = n.ObjectId, NpcId = n.NpcId, Moves = n.Moves, Position = n.Position, YawDegrees = n.YawDegrees }).ToList(); }
    readonly Dictionary<int, Actor> actors = new();
    public ConcurrentQueue<ReplayEvent> Events { get; } = new();
    public int SelfObjectId { get; private set; }
    public RequestFieldEnter Field { get; private set; }
    public double Time { get; set; }

    public IReadOnlyList<Actor> Snapshot() { lock (gate) return actors.Values.Select(a => new Actor { ObjectId = a.ObjectId, Name = a.Name, Gender = a.Gender, IsSelf = a.IsSelf, Position = a.Position, YawDegrees = a.YawDegrees, Moves = a.Moves, Add = a.Add }).ToList(); }

    /// <summary>The wire numbering the packets arrive in (the connection's profile).</summary>
    public ProtocolProfile Profile = ProtocolProfile.Kms2525;

    public void Inbound(RawPacket p) {
        if (!Profile.TryInbound(p.Opcode, out var op)) return;
        switch (op) {
            case Ms2.Net.Inbound.ServerEnter: SelfObjectId = ServerEnter.Read(p.Reader()).ObjectId; break;
            case Ms2.Net.Inbound.RequestFieldEnter: {
                var e = RequestFieldEnter.Read(p.Reader());
                if (e.Error != 0) break;
                lock (gate) { Field = e; actors.Clear(); npcs.Clear(); }
                Events.Enqueue(new FieldEnterEvent(Time, e));
                break;
            }
            case Ms2.Net.Inbound.FieldAddUser: {
                var u = FieldAddUser.Read(p.Reader());
                bool self = u.ObjectId == SelfObjectId;
                lock (gate) actors[u.ObjectId] = new Actor { ObjectId = u.ObjectId, Name = u.User.Name, Gender = u.User.Gender, IsSelf = self, Position = u.Position, YawDegrees = u.Rotation.Z, Add = u };
                Events.Enqueue(new UserAddedEvent(Time, u, self));
                break;
            }
            case Ms2.Net.Inbound.FieldRemoveUser: {
                int id = FieldRemoveUser.Read(p.Reader()).ObjectId;
                lock (gate) actors.Remove(id);
                Events.Enqueue(new UserRemovedEvent(Time, id));
                break;
            }
            case Ms2.Net.Inbound.FieldAddNpc: {
                var n = FieldAddNpc.Read(p.Reader(), NpcLayoutOf);
                lock (gate) npcs[n.ObjectId] = new NpcState { ObjectId = n.ObjectId, NpcId = n.NpcId, Position = n.Position, YawDegrees = n.Rotation.Z };
                Events.Enqueue(new NpcAddedEvent(Time, n));
                break;
            }
            case Ms2.Net.Inbound.FieldRemoveNpc:
            case Ms2.Net.Inbound.FieldDeadNpc: {
                int id = NpcObjectId.Read(p.Reader()).ObjectId;
                bool dead = op == Ms2.Net.Inbound.FieldDeadNpc;
                if (!dead) lock (gate) npcs.Remove(id);
                Events.Enqueue(new NpcRemovedEvent(Time, id, dead));
                break;
            }
            case Ms2.Net.Inbound.NpcControl: {
                NpcControl c;
                lock (gate) c = NpcControl.Read(p.Reader(), id => npcs.TryGetValue(id, out var st) && NpcLayoutOf(st.NpcId).IsBoss);
                foreach (var b in c.Blocks) {
                    lock (gate) { if (!npcs.TryGetValue(b.ObjectId, out var st)) continue; st.Position = b.Position.ToVector3(); st.YawDegrees = b.RotationZ; st.Moves++; }
                    Events.Enqueue(new NpcMovedEvent(Time, b));
                }
                break;
            }
            case Ms2.Net.Inbound.SkillUse: Events.Enqueue(new SkillUsedEvent(Time, SkillUse.Read(p.Reader()))); break;
            case Ms2.Net.Inbound.SkillCancel: { var c = SkillCancel.Read(p.Reader()); Events.Enqueue(new SkillCancelledEvent(Time, c.ActorId, c.CastUid)); break; }
            case Ms2.Net.Inbound.SkillDamage: {
                var d = SkillDamage.Read(p.Reader());
                if (d.Raw == null && d.Targets.Count > 0) Events.Enqueue(new SkillDamageEvent(Time, d));
                break;
            }
            case Ms2.Net.Inbound.Stat: {
                bool isNpc; int id = BitConverter.ToInt32(p.Body, 0);
                lock (gate) isNpc = npcs.ContainsKey(id);
                if (!isNpc) break;                       // player stat packets use another list form
                var st = NpcStat.Read(p.Reader());
                if (st.IsHealth) Events.Enqueue(new NpcHpEvent(Time, id, st.Current, st.Max));
                break;
            }
            case Ms2.Net.Inbound.UserSync:
            case Ms2.Net.Inbound.RideSync: {
                var s = UserSyncIn.Read(p.Reader());
                foreach (var r in s.Records) Moved(s.ObjectId, r, false);
                break;
            }
        }
    }

    /// <summary>The recording's own client packets: the local player's movement.</summary>
    public void Outbound(MsbPacket p) {
        if (p.Opcode != (ushort) Ms2.Net.Outbound.UserSync || SelfObjectId == 0) return;
        var o = UserSyncOut.Read(new Maple2.PacketLib.Tools.ByteReader(p.Data));
        foreach (var (rec, _) in o.Records) Moved(SelfObjectId, rec, true);
    }

    void Moved(int id, StateSync r, bool self) {
        var pos = r.Position.ToVector3();
        float yaw = r.Rotation2 / 10f;
        lock (gate) {
            if (!actors.TryGetValue(id, out var a)) return;   // a sync for somebody we never saw added
            a.Position = pos; a.YawDegrees = yaw; a.Moves++;
        }
        Events.Enqueue(new UserMovedEvent(Time, id, self, pos, yaw, r));
    }
}
