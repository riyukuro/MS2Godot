using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/own_redress.tscn) of PlayerLook.Redress, the own body's path for a put-on / put-off
/// (MyPC::vfunc_127 / vfunc_128 then PC::FUN_141656710, pc.cpp:1400-1459), on the body the field builds (PlayerLook.Build,
/// the offline starter look):
/// <list type="bullet">
/// <item>a cap put on hangs its model under the skeleton, and the body shows what a fresh Apply of the same look shows;</item>
/// <item>the cap taken off leaves no node of it, and the body is the starting look again;</item>
/// <item>the player takes the rebuilt weapon rig (the one EquipApplier keeps for the body), and in the battle stance the
/// weapons stay in the hands across the change;</item>
/// <item>a body PlayerLook did not build is not touched (false).</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class OwnRedressCheck : Node3D
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        try { await Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"own redress: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    // the crown of the client capture (map 2000110), else the first cap model the data has
    const int Crown = 11300000;

    async System.Threading.Tasks.Task Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        // the item models as EquipApplier.FindModel reads them (GMS2: one file per preset)
        var models = packs.Exists("Xml", "itemmodel/113.xml") ? PackXml.Parse(packs.Read("Xml", "itemmodel/113.xml"))
            : Ms2.Formats.World.PerFileTable.ItemModels(packs, 113);
        var caps = (models?.Descendants("ItemModel") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
            .Select(e => (int)e.Attribute("id")).Where(i => i / 100000 == 113).ToList();
        if (caps.Count == 0) { fails.Add("no cap models"); return; }
        int capId = caps.Contains(Crown) ? Crown : caps[0];

        var (visual, anim, md, bodyDir) = Look.PlayerLook.Build(builder, packs);   // Field.SpawnPlayer's body
        var pc = Player.Create(visual, anim, new Movement.NxController.FaceGrids());
        pc.Gender = bodyDir == "male" ? 0 : 1;
        pc.Weapons = EquipApplier.RigOf(visual);
        AddChild(pc);
        pc.SetProcess(false); pc.SetPhysicsProcess(false);
        var start = Look.PlayerLook.OfflineWorn.Where(i => i.Slot != EquipSlot.CP).ToList();
        if (!Look.PlayerLook.Redress(pc, start)) fails.Add("Redress refused the body PlayerLook built");
        await Settle();
        string startLook = Describe(visual);
        pc.SetBattleStance(true);
        if (!pc.WeaponsHeld) fails.Add($"the starter look holds no weapon in the battle stance (items {string.Join(" ", start.Select(i => i.Id))})");

        var cap = new EquipItem { Id = capId, Uid = 9001, Slot = EquipSlot.CP };
        var worn = start.Append(cap).ToList();
        var rigBefore = pc.Weapons;
        if (!Look.PlayerLook.Redress(pc, worn)) fails.Add("put on: Redress returned false");
        await Settle();
        if (!Nodes(visual).Any(n => n.StartsWith($"eq{capId}_"))) fails.Add($"put on: no eq{capId} node under the skeleton");
        if (pc.Weapons != EquipApplier.RigOf(visual)) fails.Add("put on: the player keeps the old weapon rig");
        if (pc.Weapons == rigBefore) fails.Add("put on: the rig was not rebuilt (nothing to check the stance on)");
        if (!pc.WeaponsHeld) fails.Add("put on: the weapons left the hands in the battle stance");
        SameAsFresh(fails, builder, packs, md, pc.Gender, visual, "put on", worn);

        if (!Look.PlayerLook.Redress(pc, start)) fails.Add("put off: Redress returned false");
        await Settle();
        if (Nodes(visual).Any(n => n.StartsWith($"eq{capId}_"))) fails.Add($"put off: an eq{capId} node is still under the skeleton");
        if (Describe(visual) != startLook) fails.Add($"put off: not the starting look\n    want: {startLook}\n    got:  {Describe(visual)}");
        if (!pc.WeaponsHeld) fails.Add("put off: the weapons left the hands in the battle stance");

        var other = builder.Build(md, "Other");     // Player.Create parents it
        var stranger = Player.Create(other, other.GetNode<AnimationPlayer>("Model/AnimationPlayer"), new Movement.NxController.FaceGrids());
        if (Look.PlayerLook.Redress(stranger, worn)) fails.Add("a body PlayerLook did not dress was redressed");
        stranger.Free();
    }

    static void SameAsFresh(List<string> fails, ModelBuilder builder, PackSet packs, Ms2.Formats.Model.ModelData md, int gender,
        Node3D v, string what, IEnumerable<EquipItem> look)
    {
        var f = builder.Build(md, "Fresh");
        new EquipApplier(packs, builder) { Palettes = new Look.ColorPalettes(packs) }.Apply(f, md, gender, look);
        string want = Describe(f), got = Describe(v);
        if (want != got) fails.Add($"{what}: differs from a fresh Apply\n    fresh: {want}\n    got:   {got}");
        f.Free();
    }

    async System.Threading.Tasks.Task Settle()
    {
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static IEnumerable<string> Nodes(Node3D v) =>
        v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName).FindChildren("*", "", true, false)
            .Where(n => !n.IsQueuedForDeletion()).Select(n => n.Name.ToString());

    /// <summary>The mesh nodes under the skeleton (without the unique-name suffix Godot adds) and their visibility, sorted
    /// (no materials: the fresh Apply has no skin colour).</summary>
    static string Describe(Node3D v) =>
        string.Join(",", v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName).FindChildren("*", "MeshInstance3D", true, false)
            .Cast<MeshInstance3D>().Where(m => !m.IsQueuedForDeletion())
            .Select(m => $"{System.Text.RegularExpressions.Regex.Replace(m.Name.ToString(), "@.*", "")}:{(m.Visible ? 1 : 0)}")
            .OrderBy(s => s, StringComparer.Ordinal));
}
