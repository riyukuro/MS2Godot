using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/remote_cube.tscn, game data) of other pcs' cube modes and held items in RemoteLayer:
/// - FieldAddUser's cube mode, craft mode and held object (OnAddUser's PC::vfunc_135, PC::vfunc_12) are the new pc's; a dead
///   pc's add gives no mode;
/// - SetCraftMode for a pc not in the cube mode puts it in StateIdle and sets the mode and the craft (PC::vfunc_135); mode 0
///   leaves it; a dead or missing pc takes nothing;
/// - ResponseCube 0x11 for the pc holds the item when the cube has its weapon, 0x12 lets it go; an error or another id
///   changes nothing.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class RemoteCubeCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var layer = new RemoteLayer(field, builder, 1) { ItemExists = id => id != 999 };
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }

        FieldAddUser User(int id, long hp, byte cubeMode, int held)
        {
            var u = new FieldAddUser { ObjectId = id, User = new UserBlock { Name = "r" + id, CharacterId = 100 + id, Gender = 1 } };
            u.Stats[0].Health = 100; u.Stats[2].Health = hp;
            u.CubeMode = cubeMode; u.CraftMode = 1; u.CubeItem = new CubeItemInfo { ItemId = 50200001 };
            u.HeldObject = held;
            return u;
        }
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(5, 100, 1, 30000001), false));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(6, 0, 1, 0), false));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(7, 100, 0, 999), false));
        layer._Process(0);
        var s5 = layer.CubeStateOf(5);
        Check(s5.CubeMode == 1 && s5.Craft && s5.HeldItem == 30000001, $"the add's cube state {s5}");
        Check(layer.CubeStateOf(6).CubeMode == 0, $"a dead pc's add set the cube mode {layer.CubeStateOf(6).CubeMode}");
        var s7 = layer.CubeStateOf(7);
        Check(s7.CubeMode == 0 && !s7.Craft && s7.HeldItem == 0, $"an add without a mode and an unknown held item gave {s7}");

        // SetCraftMode: into the cube mode from another state goes to StateIdle
        Check(s7.State != RemotePcStates.StateIdle, $"pc 7 starts in state {s7.State}");
        Check(layer.SetCraftMode(7, 2, 0, new CubeItemInfo { ItemId = 50200001 }), "SetCraftMode for a live pc not taken");
        s7 = layer.CubeStateOf(7);
        Check(s7.CubeMode == 2 && s7.Craft && s7.State == RemotePcStates.StateIdle, $"after SetCraftMode 2: {s7}");
        Check(layer.SetCraftMode(7, 0, 0, null) && layer.CubeStateOf(7) is { CubeMode: 0, Craft: false }, $"mode 0 left {layer.CubeStateOf(7)}");
        Check(!layer.SetCraftMode(6, 1, 0, null) && layer.CubeStateOf(6).CubeMode == 0, "a dead pc took SetCraftMode");
        Check(!layer.SetCraftMode(42, 1, 0, null), "a missing pc took SetCraftMode");

        // ResponseCube held items
        Check(!layer.ResponseCube(new ResponseCubeHeldItem(ResponseCubeHeldItem.ModeHold, 0, 7, 1, 30000002), null) && layer.CubeStateOf(7).HeldItem == 0,
            "0x11 held an item with the cube weapon unknown");
        Check(layer.ResponseCube(new ResponseCubeHeldItem(ResponseCubeHeldItem.ModeHold, 0, 7, 1, 30000002), _ => true) && layer.CubeStateOf(7).HeldItem == 30000002,
            $"0x11 with the cube's weapon gave {layer.CubeStateOf(7).HeldItem}");
        Check(!layer.ResponseCube(new ResponseCubeHeldItem(ResponseCubeHeldItem.ModeRelease, 3, 7), null) && layer.CubeStateOf(7).HeldItem == 30000002,
            "an error answer let the item go");
        Check(layer.ResponseCube(new ResponseCubeHeldItem(ResponseCubeHeldItem.ModeRelease, 0, 7), null) && layer.CubeStateOf(7).HeldItem == 0,
            "0x12 did not let the item go");
        Check(layer.CubeStateOf(5).HeldItem == 30000001, "pc 7's answers touched pc 5");
        Check(!layer.ResponseCube(new ResponseCubeHeldItem(ResponseCubeHeldItem.ModeRelease, 0, 42), null), "a missing pc took 0x12");

        GD.Print($"remote cube: {(fails.Count == 0 ? "ok" : "FAIL " + string.Join("; ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
