using Godot;
using System.Text.Json.Nodes;
using Ms2.Net.Packets;
using Ms2.Net.Replay;

namespace Ms2Godot.Ui;

/// <summary>
/// The perf gate's other players: a <see cref="Net.RemoteLayer"/> on the offline field, fed the events a live field's
/// game connection gives it. <see cref="GateRemoteCount"/> players (MS2_PERF_GATE_REMOTES, default 20) are added around the
/// own one, wearing its offline look, with the minimap's proxies wired as the HUD wires a live field's
/// (<see cref="WatchMinimapPings"/>). The spawn is its own entry (remotes_spawn); the remotes window then has each player
/// send a walk record every 6 frames (100 ms, the client's sync cadence while moving) along the own player's walk of the
/// move window (each player at its own place on it), a jump every 300 and a normal attack cast every 120, each player on
/// its own frame offset.
/// </summary>
public partial class HudScene
{
    static readonly int GateRemoteCount = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_REMOTES"), out var gr) && gr >= 0 ? gr : 20;
    const int GateRemoteSettle = 120, GateRemoteBase = 0x10000;
    Net.RemoteLayer gateRemotes;
    System.Numerics.Vector3[] gateRemoteAt;      // each player's place, in the packets' coordinates (z up)
    int gateRemoteSkill, gateRemoteFrame;
    long gateRemoteCast;
    GateMark gateSpawn;
    bool gateRemoteChurn;
    // the own player's place every 6 frames of the move window (packet coordinates): where its controller walked, so a
    // record from it is one a live sender's controller could have sent (not a point inside a wall or a building). Sized
    // for the whole window up front, so the move window's bytes do not include the list growing.
    readonly List<System.Numerics.Vector3> gatePath = new(GateIdle / 6 + 1);

    void GateRemotesSpawn()
    {
        GateCollect();
        gatePlayer ??= FindPlayer(this);
        if (GetTree().GetFirstNodeInGroup(Field.Group) is not Field field || gatePlayer == null)
        {
            GD.PushError("perf gate: no field or player for the remotes");
            return;
        }
        var mb = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), field.Packs);
        var player = gatePlayer;
        gateRemotes = new Net.RemoteLayer(field, mb, -1) { Viewer = () => player.GlobalPosition };
        gateRemotes.PcShown += pc => minimapSide?.AddOtherPlayer(MinimapPc(pc));
        gateRemotes.PcLeft += id => minimapSide?.RemoveOtherPlayer(id);
        var actions = assets.Table("table/weaponaction.xml") is { } wa ? Ms2.Ui.Skills.WeaponActionTable.Load(wa) : Ms2.Ui.Skills.WeaponActionTable.Empty;
        gateRemoteSkill = Ms2.Ui.Skills.SkillCast.NormalAttackSkillId(null, actions, 0);
        var g = player.GlobalPosition;
        var home = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        var worn = Look.PlayerLook.OfflineWorn ?? System.Array.Empty<EquipItem>();
        gateRemoteAt = new System.Numerics.Vector3[GateRemoteCount];
        GateCollect();
        gateSpawn = GateMark.Now();
        for (int i = 0; i < GateRemoteCount; i++)
        {
            var at = gateRemoteAt[i] = gatePath.Count > 0 ? gatePath[GatePathAt(i, 0)] : home;
            bool hurt = i % 4 == 0;   // a quarter in battle stance and hurt: their HP bars show
            var u = new FieldAddUser
            {
                ObjectId = GateRemoteBase + i,
                User = new UserBlock
                {
                    CharacterId = 0x70000000L + i, Name = "Remote" + i, Gender = (byte)Look.PlayerLook.OfflineGender, Level = 50,
                    JobCode = Look.PlayerLook.OfflineJob, Job = Look.PlayerLook.OfflineJob, GuildName = "", Motto = "", Picture = "",
                },
                Position = at, InBattle = hurt, Picture = "",
            };
            for (int k = 0; k < 3; k++) u.Stats[k] = (k == 2 && hurt ? 700 : 1000, 100, 100, 100, 100);
            u.Equips.AddRange(worn);
            gateRemotes.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        }
    }

    void GateRemotesSpawnEnd()
    {
        var o = GateDelta(gateSpawn, GateMark.Now());
        o["remotes"] = GateRemoteCount;
        o["shown"] = gateRemotes?.UserCount ?? 0;
        gateOut["remotes_spawn"] = o;
    }

    void GateRemotesStart()
    {
        GateCollect();
        GateIdleStart("remotes");
        gateRemoteFrame = 0;
        gateRemoteChurn = gateRemotes != null;
        if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_ATTRIB") == "1") WrapperAttrib.Install(GetTree().Root);
    }

    void GateRemotesEnd()
    {
        gateRemoteChurn = false;
        GateIdleEnd();
        ((JsonObject)gateOut["remotes"])["remotes"] = GateRemoteCount;
    }

    /// <summary>Player <paramref name="i"/>'s point of the recorded walk at step <paramref name="step"/>: the players spread
    /// evenly along it, each going round it.</summary>
    int GatePathAt(int i, int step) => (i * gatePath.Count / Math.Max(1, GateRemoteCount) + step) % gatePath.Count;

    /// <summary>One frame of the remotes window's traffic (see the class summary).</summary>
    void GateRemotesTick()
    {
        int f = gateRemoteFrame++;
        var events = gateRemotes.Tracker.Events;
        for (int i = 0; i < gateRemoteAt.Length; i++)
        {
            int id = GateRemoteBase + i, fi = f + i * 7;
            if (fi % 6 == 0 && gatePath.Count > 1)
            {
                bool jump = fi % 300 == 150;
                // the next point of the own player's walk (each player at its own place on it); a jump goes up where it stands
                int k = GatePathAt(i, f / 6 + 1);
                var p = jump ? gateRemoteAt[i] : gatePath[k];
                var step = p - gateRemoteAt[i];
                var d = step.X * step.X + step.Y * step.Y > 1 ? System.Numerics.Vector2.Normalize(new(step.X, step.Y)) : new System.Numerics.Vector2(1, 0);
                gateRemoteAt[i] = p;
                float yaw = MathF.Atan2(d.Y, d.X) * 180 / MathF.PI;
                int ax = MathF.Abs(d.X) < 0.5f ? 1 : d.X > 0 ? 2 : 0, ay = MathF.Abs(d.Y) < 0.5f ? 2 : d.Y > 0 ? 3 : 1;
                var rec = new StateSync
                {
                    State = (byte)(jump ? 6 : 2), SubState = (byte)(jump ? 0x21 : 0x18),
                    Position = new Vector3S((short)p.X, (short)p.Y, (short)p.Z), Rotation = (short)(yaw * 10), Rotation2 = (short)(yaw * 10),
                    Speed = new Vector3S(57, 90, 0), Unknown1 = 2, SyncNumber = 0x7fffffff, Animation = (byte)(jump ? 0 : ax + 4 * ay),
                };
                events.Enqueue(new UserMovedEvent(0, id, false, p, yaw, rec));
            }
            if (fi % 120 == 60 && gateRemoteSkill != 0)
            {
                var p = gateRemoteAt[i];
                events.Enqueue(new SkillUsedEvent(0, new SkillUse(++gateRemoteCast, 0, id, gateRemoteSkill, 1, 0,
                    new Vector3S((short)p.X, (short)p.Y, (short)p.Z), new System.Numerics.Vector3(1, 0, 0), default, 1f, false, false, 0, null)));
            }
        }
    }
}
