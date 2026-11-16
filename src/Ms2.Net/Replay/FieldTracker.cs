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

    readonly object gate = new();
    readonly Dictionary<int, Actor> actors = new();
    public ConcurrentQueue<ReplayEvent> Events { get; } = new();
    public int SelfObjectId { get; private set; }
    public RequestFieldEnter Field { get; private set; }
    public double Time { get; set; }

    public IReadOnlyList<Actor> Snapshot() { lock (gate) return actors.Values.Select(a => new Actor { ObjectId = a.ObjectId, Name = a.Name, Gender = a.Gender, IsSelf = a.IsSelf, Position = a.Position, YawDegrees = a.YawDegrees, Moves = a.Moves, Add = a.Add }).ToList(); }

    public void Inbound(RawPacket p) {
        switch ((Ms2.Net.Inbound) p.Opcode) {
            case Ms2.Net.Inbound.ServerEnter: SelfObjectId = ServerEnter.Read(p.Reader()).ObjectId; break;
            case Ms2.Net.Inbound.RequestFieldEnter: {
                var e = RequestFieldEnter.Read(p.Reader());
                if (e.Error != 0) break;
                lock (gate) { Field = e; actors.Clear(); }
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
