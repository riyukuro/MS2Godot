using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_react.tscn, game data) of another PC's reaction to a cast's damage record:
/// OnSkillDamage mode 1 (scenestategame.cpp:5607) hands each record to PC::vfunc_33 (pc.cpp:885), which runs for every PC.
/// - a record of an attack that pushes, of a total below 0, on a shown player starts its push and puts it in StateHit;
/// - a record whose skill is not in the table (CDamageRecord::Init fails) changes nothing;
/// - a dot's record (mode 3) of the same attack goes to vfunc_34, not this reaction: the player stays idle.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteReactCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var floor = new StaticBody3D { CollisionLayer = 1, Position = new Vector3(0, -50, 0) };
        floor.SetMeta(FieldBuilder.GroupMeta, 0);
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4000, 100, 4000) } };
        FieldBuilder.TagGround(cs, "stone");
        floor.AddChild(cs);
        field.AddChild(floor);
        layer = new RemoteLayer(field, builder, 1);
        FieldAddUser User(int id)
        {
            // alive: the add's current hp (stat row 2) above 0 (RemoteLayer's hp 0 check)
            var u = new FieldAddUser { ObjectId = id, User = new UserBlock { Name = "r" + id, CharacterId = 100 + id, Gender = 1 } };
            u.Stats[0].Health = 100; u.Stats[2].Health = 100;
            return u;
        }
        foreach (int id in new[] { 5, 6, 7 }) layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(id), false));
        layer._Process(0);
    }

    RemoteLayer layer;
    int frame;

    public override void _PhysicsProcess(double delta)
    {
        // the shown players land on the floor first
        if (++frame < 60) { layer._Process(1f / 60f); return; }
        if (frame > 60) return;
        var packs = PackSet.Shared(Settings.DataDir);
        foreach (int id in new[] { 5, 6, 7 })
            Check(PcOf(layer, id)?.ClientStateId == RemotePcStates.StateIdle, $"player {id} did not land: state {PcOf(layer, id)?.ClientStateId}");

        // the first attack in the table that pushes away from the record's position (kind 1) with a lift off a ground field
        // (a lifting hit plays Jump_FlyDamg_A; the body's own hit action is not in the action table, so a hit without one
        // goes idle on its next frame)
        int skill = 0, motion = 0, attack = 0;
        foreach (uint id in Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).SkillDamage.SkillIds)
        {
            for (int m = 0; m < 4 && skill == 0; m++)
                for (int a = 0; a < 4 && skill == 0; a++)
                    if (layer.AttackDamage((int)id, 1, m, a) is { } d && d.GetPush(false) == 1 && d.PushDuration > 0f && d.PushDistance > 0f && d.PushUpDistance > 0f)
                        (skill, motion, attack) = ((int)id, m, a);
            if (skill != 0) break;
        }
        Check(skill != 0, "no pushing attack in the table");

        SkillDamage Record(byte mode, int skillId, int target) => new()
        {
            Mode = mode, OwnerId = 99, SkillId = skillId, Level = 1, Motion = (byte)motion, AttackPoint = (byte)attack,
            Position = new Vector3S(-200, 0, 0), DirectionShort = new Vector3S(1, 0, 0),
            Targets = { new HitTarget(target, new[] { new Hit(0, -10) }, default, default) },
        };
        layer.Tracker.Events.Enqueue(new SkillDamageEvent(0, Record(1, skill, 5)));
        layer.Tracker.Events.Enqueue(new SkillDamageEvent(0, Record(1, 999999999, 6)));
        layer.Tracker.Events.Enqueue(new SkillDamageEvent(0, Record(3, skill, 7)));
        // a frame with a delta: a tick of 0 moves the push by 0, which ends it (CPushControl::vfunc_1)
        layer._Process(1f / 60f);
        var p5 = PcOf(layer, 5); var p6 = PcOf(layer, 6); var p7 = PcOf(layer, 7);
        Check(p5 != null && p6 != null && p7 != null, "a shown player has no body");
        Check(p5 is { ClientStateId: RemotePcStates.StateHit, Pushed: true },
            $"the pushing record left the player in state {p5?.ClientStateId}, pushed {p5?.Pushed}");
        Check(p6 is { ClientStateId: RemotePcStates.StateIdle, Pushed: false },
            $"a record of an unknown skill moved the player to state {p6?.ClientStateId}, pushed {p6?.Pushed}");
        Check(p7 is { ClientStateId: RemotePcStates.StateIdle, Pushed: false },
            $"a dot's record moved the player to state {p7?.ClientStateId}, pushed {p7?.Pushed}");

        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? $"remote react: skill {skill}, ok" : $"remote react: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>A shown player's body: the private Remote record's Pc.</summary>
    static RemotePc PcOf(RemoteLayer layer, int id)
    {
        var users = (IDictionary)typeof(RemoteLayer).GetField("users", Private).GetValue(layer);
        var r = users[id];
        return r == null ? null : (RemotePc)r.GetType().GetField("Pc").GetValue(r);
    }
}
