using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_land.tscn) of OnAddUser's spawn point (scenestategame.cpp:11138-11181) and the
/// landing after it: a player added on a floor's top (a GMS2 server sends such positions) whose top is a hair over the
/// packet's z. The client raises the position by 25 before its down ray, so the ray starts over the floor and hits it;
/// a ray from the bare packet z starts inside the floor and misses it, and the body then fell through the floor for good.
/// After 3 s the added player must be out of StateFall (it leaves StateFall when the body touches ground) and not under
/// the floor's top.
/// The own PC's ray (Player.Ray) with a zero direction, from inside the floor and from its top, must give no hit, as
/// CastRay returns false for a direction that normalizes to zero (physxhelper.cpp:207-209). FieldBuilder.ZupToYup must
/// map an MS2 point exactly as ToGodotPoint does (x, z, -y), with no float error from a rotation by an angle.
/// With MS2_REC set to a recording (.msb), the players it adds on the first field it enters are spawned on that field, built from MS2_DATA, instead of the floor.
/// Quits with 0 when it holds, else 1.
/// </summary>
public partial class RemoteLandCheck : Node3D
{
    readonly List<string> fails = new();
    readonly List<(FieldAddUser U, bool Hit, float HitY)> adds = new();
    RemoteLayer layer;
    int frame;
    Player own;
    readonly Ms2Godot.Movement.NxController.FaceGrids faceGrids = new();

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); Done(); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        string path = System.Environment.GetEnvironmentVariable("MS2_REC");
        if (string.IsNullOrEmpty(path))
        {
            // a floor whose top is y 0.01, a player added at z 0: a ray from the packet's z starts inside the floor
            var body = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, -74.99f, 0) };
            body.SetMeta(FieldBuilder.GroupMeta, 0);
            var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3000, 150, 3000) } };
            FieldBuilder.TagGround(cs, "stone");
            body.AddChild(cs);
            field.AddChild(body);
            var u = new FieldAddUser { ObjectId = 501, User = new UserBlock { Name = "l", CharacterId = 601, Gender = 1 } };
            u.Stats[0].Health = 100; u.Stats[2].Health = 100;
            u.Position = new System.Numerics.Vector3(-600, 450, 0);
            adds.Add((u, false, 0));
            own = Player.Create(new Node3D(), null, faceGrids);
            AddChild(own);
            own.SetProcess(false);
            own.SetPhysicsProcess(false);
            own.GlobalPosition = new Vector3(600, 200, 0);
        }
        else
        {
            var file = MsbFile.Read(path);
            var profile = ProtocolProfile.ForVersion(file.Build) ?? ProtocolProfile.Gms2;
            int mapId = 0;
            var users = new List<FieldAddUser>();
            foreach (var p in file.Packets)
            {
                if (!p.ServerToClient || !profile.TryInbound(p.Opcode, out var op)) continue;
                var r = new Maple2.PacketLib.Tools.ByteReader(p.Data);
                if (op == Inbound.RequestFieldEnter)
                {
                    if (mapId != 0) break;
                    var e = RequestFieldEnter.Read(r);
                    if (e.Error == 0) mapId = e.MapId;
                }
                else if (op == Inbound.FieldAddUser && mapId != 0)
                {
                    try { users.Add(FieldAddUser.Read(r, profile)); }
                    catch (Exception ex) { GD.Print($"remote land: add unread: {ex.Message}"); }
                }
            }
            var fd = FieldData.FindById(packs, mapId) ?? throw new InvalidOperationException($"no field {mapId}");
            var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
            var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
            foreach (var _ in fb.BuildSteps(map)) { }
            field.AddChild(fb.World);
            GD.Print($"remote land: {System.IO.Path.GetFileName(path)} profile {profile.Name}, map {mapId} xblock {fd.XBlock}, {users.Count} adds");
            foreach (var u in users.GroupBy(u => u.ObjectId).Select(g => g.First()).Take(12)) adds.Add((u, false, 0));
        }
        layer = new RemoteLayer(field, builder, 1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (layer == null) return;
        frame++;
        if (frame == 2)
        {
            // the colliders are in the space now: the spawn ray of each add, then the adds
            var space = GetWorld3D().DirectSpaceState;
            for (int i = 0; i < adds.Count; i++)
            {
                var u = adds[i].U;
                var at = new Vector3(u.Position.X, u.Position.Z, -u.Position.Y);
                using var q = PhysicsRayQueryParameters3D.Create(at, at + Vector3.Down * Movement.MoveConfig.UnlimitedRay, Movement.MoveConfig.DownRayGroups);
                using var hit = space.IntersectRay(q);
                adds[i] = (u, hit.Count > 0, hit.Count > 0 ? ((Vector3)hit[PhysicsKeys.Position]).Y : 0);
                layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
            }
            if (own != null)
            {
                var ray = typeof(Player).GetMethod("Ray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(uint) }, null);
                foreach (var y in new[] { -10f, 0.01f })
                {
                    var got = ((Vector3 pos, Vector3 normal)?)ray.Invoke(own, new object[] { new Vector3(600, y, 0), Vector3.Zero, 100f, uint.MaxValue });
                    GD.Print($"remote land: own zero-direction ray from y {y}: {got?.pos.ToString() ?? "none"}");
                    if (got != null) fails.Add($"the own PC's zero-direction ray from y {y} hit {got.Value.pos}");
                }
                var zup = FieldBuilder.ZupToYup * new Vector3(0, -3000, 100);
                GD.Print($"remote land: ZupToYup of ms2 (0, -3000, 100): ({zup.X:R}, {zup.Y:R}, {zup.Z:R})");
                if (zup != new Vector3(0, 100, 3000)) fails.Add($"ZupToYup maps ms2 (0, -3000, 100) to ({zup.X:R}, {zup.Y:R}, {zup.Z:R}), not (0, 100, 3000)");
            }
        }
        if (frame < 2 + 180) return;
        var users = (System.Collections.IDictionary)typeof(RemoteLayer)
            .GetField("users", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(layer);
        foreach (var (u, hit, hy) in adds)
        {
            if (!users.Contains(u.ObjectId)) { fails.Add($"{u.ObjectId} not spawned"); continue; }
            var r = users[u.ObjectId];
            var pc = (RemotePc)r.GetType().GetField("Pc").GetValue(r);
            int st = pc.States.Cur;
            string line = $"{u.ObjectId} '{u.User.Name}' add {u.Position} plain ray {(hit ? $"hit y {hy:F1}" : "none")}, state {st}, body y {pc.Body.Position.Y:F1}";
            GD.Print("remote land: " + line);
            if (st == RemotePcStates.StateFall || pc.Body.Position.Y < hy - 1) fails.Add("not landed: " + line);
        }
        Done();
    }

    void Done()
    {
        GD.Print(fails.Count == 0 ? "remote land: ok" : "remote land: FAIL\n  " + string.Join("\n  ", fails));
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
        layer = null;
    }
}
