using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Items;
using Ms2Godot.Look;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/carried_model.tscn) of the carried model (Look.CarriedModel, Unk_PCCubeCraft::FUN_1410a9630):
/// on the male and the female body, a lift craft (cube mode 2, craft mode 2) of the tutorial lift items 30000856 (an npc
/// pack NIF) and 30000441 (a map pack NIF) hangs the item's install model, with meshes, at half scale on the body's
/// Extra_Point01 bone; a craft of mode 1 and a craft dropped (cube mode 0) leave the body without it. With MS2_SHOT_DIR
/// set and a window, saves carried.png (the female body carrying 30000856). Quits with 0 when all hold, else 1.
/// </summary>
public partial class CarriedModelCheck : Node3D
{
    readonly List<string> fails = new();
    static readonly string ShotDir = System.Environment.GetEnvironmentVariable("MS2_SHOT_DIR");
    int frames;
    PackSet packs;
    ModelBuilder builder;
    ItemDataTable items;
    Node3D female;

    public override void _Ready()
    {
        packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        builder.TextureArchives = builder.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        items = new ItemDataTable(packs);
    }

    void Expect(bool ok, string what) { if (!ok) fails.Add(what); }

    static CubeCraftState Craft(int cubeMode, int craftMode, int itemId)
    {
        var c = new CubeCraftState();
        c.NewPc();
        c.ModelAttached();
        c.Set(cubeMode, craftMode, itemId == 0 ? null : new CubeItemInfo { ItemId = itemId, Count = 1 }, dead: false);
        return c;
    }

    Node3D Body(string dir, string nif)
    {
        var body = builder.Build(builder.LoadModel($"{dir}/{nif}"), dir);
        AddChild(body);
        return body;
    }

    void Check(string bodyName, Node3D body)
    {
        var skel = body.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        Expect(skel.FindBone(CarriedModel.Point) >= 0, $"{bodyName}: no {CarriedModel.Point} bone");
        foreach (int id in new[] { 30000856, 30000441 })
        {
            bool built = CarriedModel.Sync(body, Craft(2, 2, id).Current, builder, packs, items.Get);
            var att = skel.GetNodeOrNull<BoneAttachment3D>("CubeGuide2_attach");
            var root = att?.GetChildOrNull<Node3D>(0);
            int meshes = root == null ? 0 : root.FindChildren("*", nameof(MeshInstance3D), true, false).Count;
            float scale = root?.Transform.Basis.Scale.X ?? 0;
            GD.Print($"carried model: {bodyName} item {id} ({items.Get(id)?.InstallAsset}): built {built}, bone {att?.BoneName}, {meshes} meshes, scale {scale}");
            Expect(built && CarriedModel.Carries(body) && meshes > 0 && Mathf.IsEqualApprox(scale, CarriedModel.Scale) && att.BoneName == CarriedModel.Point,
                $"{bodyName} item {id}: built {built}, {meshes} meshes, scale {scale}");
            Expect(skel.FindChildren("CubeGuide2_attach", "", false, false).Count == 1, $"{bodyName} item {id}: the rebuild left one model");
        }
        Expect(!CarriedModel.Sync(body, Craft(2, 1, 30000856).Current, builder, packs, items.Get) && !CarriedModel.Carries(body), $"{bodyName}: craft mode 1 carries nothing");
        CarriedModel.Sync(body, Craft(2, 2, 30000856).Current, builder, packs, items.Get);
        Expect(!CarriedModel.Sync(body, Craft(0, 0, 0).Current, builder, packs, items.Get) && !CarriedModel.Carries(body), $"{bodyName}: the put-down drops it");
    }

    public override void _Process(double delta)
    {
        frames++;
        try
        {
            switch (frames)
            {
                case 2:
                    Check("male", Body("male", "m_body.nif"));
                    female = Body("female", "f_body.nif");
                    Check("female", female);
                    CarriedModel.Sync(female, Craft(2, 2, 30000856).Current, builder, packs, items.Get);
                    if (ShotDir != null)
                    {
                        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0) });
                        var cam = new Camera3D { Position = new Vector3(0, 120, 300) };
                        AddChild(cam);
                        cam.LookAt(new Vector3(0, 90, 0));
                        cam.Current = true;
                    }
                    break;
                case 10:
                    if (ShotDir != null && DisplayServer.GetName() != "headless")
                    {
                        RenderingServer.ForceDraw(false);
                        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(ShotDir, "carried.png"));
                    }
                    Finish();
                    break;
            }
        }
        catch (System.Exception e) { fails.Add($"{e.InnerException ?? e}"); Finish(); }
    }

    void Finish()
    {
        SetProcess(false);
        GD.Print($"carried model: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
