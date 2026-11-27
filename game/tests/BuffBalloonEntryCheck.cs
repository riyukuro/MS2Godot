using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Ui.Hud;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/buff_balloon_entry.tscn, game data) of a remote player's entry-list balloon: OnAddUser makes
/// and registers the PC (OnObjectCreated, scenestategame.cpp:11214) before DecodeListPacket reads its buffs (:11278), so a
/// balloon buff in the FieldAddUser list (SpeedUp 70000112, iconBalloonOnOff 1) finds the PC's balloon (PC::vfunc_84) and
/// shows: the field's balloon controller holds an entity for that object id, with the buff's key and the icon.
/// Quits with 0 when it holds, else 1.
/// </summary>
public partial class BuffBalloonEntryCheck : Node3D
{
    const int SpeedUp = 70000112, Key = 9001;
    readonly System.Collections.Generic.List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var fx = new RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new RemoteLayer(field, builder, 1);
        var tables = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        var level = tables.Additional.GetLevel(SpeedUp, 1);
        Check(level != null && level.IconBalloonOnOff, $"{SpeedUp} level 1 has no balloon (iconBalloonOnOff): the check proves nothing");

        var u = new FieldAddUser { ObjectId = 5, User = new UserBlock { Name = "r5", CharacterId = 105, Gender = 1 } };
        u.Buffs.Add(new Buff(5, Key, 5, 0, int.MaxValue, SpeedUp, 1, 1, 1, 0));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, u, false));
        layer._Process(0);

        var c = LiveBuffBalloons.Live?.Controller;
        Check(c != null, "no live balloon layer / controller");
        BuffBalloonController.Entity e = null;
        if (c != null) c.Entities.TryGetValue(5, out e);
        Check(e != null, "the entry list's balloon buff made no balloon for the player");
        Check(e == null || (e.Key == Key && (e.Active || e.Held)), $"the balloon is not shown for the entry buff (key {e?.Key}, active {e?.Active}, held {e?.Held})");
        Check(e == null || !string.IsNullOrEmpty(e.Image), "the balloon has no icon");

        GD.Print($"buff balloon entry: entity {(e == null ? "none" : $"key {e.Key} active {e.Active} held {e.Held} image '{e.Image}'")}");
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "buff balloon entry: ok" : $"buff balloon entry: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
