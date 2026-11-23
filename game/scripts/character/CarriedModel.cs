using System;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Items;

namespace Ms2Godot.Look;

/// <summary>
/// The cube craft's second guide, the object a pc carries in cube mode 2: Unk_PCCubeCraft::FUN_1410a9630 (@0x1410a9630,
/// cubecraft_2.cpp:467-520) from RebuildGuides (:523-531). The craft item's install model (CNifCubeGuide::LoadAsset of
/// install.asset, :492-496) at scale 0.5 (vfunc_9, :504) hangs on the owner's "Extra_Point01" node (:505-506); a
/// rebuild releases the old one first (ReleaseGuides, :469, :525). It is a node under the body's skeleton, so it goes with
/// the body.
/// STILL-OPEN: a KFM preset (CKfmCubeGuide, :497-501) and a fitting doll (FUN_1410ad6a0, :763-776: an install objCode
/// that FUN_1407072e0 accepts; CFittingDollCubeGuide, :484-489) are not built; the UGC look of a texture item (:509-519);
/// the alpha and draw flags vfunc_12 sets.
/// </summary>
public static class CarriedModel
{
    /// <summary>The body node the model hangs on (GetObjectByName("Extra_Point01"), :505).</summary>
    public const string Point = "Extra_Point01";
    /// <summary>The model's scale (m_spGuide2-&gt;vfunc_9(0.5f), :504).</summary>
    public const float Scale = 0.5f;
    const string AttachName = "CubeGuide2_attach";
    static readonly NodePath SkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    /// <summary>
    /// Rebuilds the carried model of <paramref name="craft"/> on the body <paramref name="visual"/> (RebuildGuides'
    /// ReleaseGuides, then FUN_1410a9630 when <see cref="CubeCraftState.Craft.CarriesItemModel"/>). True when a model hangs
    /// on the body afterwards.
    /// </summary>
    public static bool Sync(Node3D visual, CubeCraftState.Craft craft, ModelBuilder builder, PackSet packs, Func<int, ItemData> itemData)
    {
        if (visual == null || !GodotObject.IsInstanceValid(visual)) return false;
        var skel = visual.GetNodeOrNull<Skeleton3D>(SkeletonPath);
        if (skel == null) return false;
        Release(skel);
        if (craft is not { CarriesItemModel: true } || builder == null || packs == null) return false;
        var data = itemData?.Invoke(craft.Item.ItemId);
        if (data == null) return false;                                  // :478-479
        if (!string.IsNullOrEmpty(data.InstallPreset))
        {
            GD.Print($"carried model: item {craft.Item.ItemId} has the preset {data.InstallPreset}, not built");
            return false;
        }
        int bone = skel.FindBone(Point);
        if (bone < 0)
        {
            GD.Print($"carried model: the body has no {Point}");
            return false;
        }
        if (ItemData.ModelPackEntry(data.InstallAsset) is not var (pack, file)) return false;
        string entry = packs.Exists(pack, file.ToLowerInvariant()) ? file.ToLowerInvariant() : packs.FindByFileName(pack, System.IO.Path.GetFileName(file));
        if (entry == null) return false;                                 // LoadAsset fails: no guide
        if (Array.IndexOf(builder.TextureArchives, pack) < 0) builder.TextureArchives = [pack, .. builder.TextureArchives];
        Node3D root;
        try { root = builder.Build(builder.LoadModel(pack, entry), "CubeGuide2"); }
        catch (Exception e) { GD.PushWarning($"carried model {data.InstallAsset}: {e.Message}"); return false; }
        var att = new BoneAttachment3D { Name = AttachName, BoneName = Point };
        skel.AddChild(att);
        // the body skeleton is in file space (Z-up), so the built model's own Z-up turn is undone, then the half scale
        root.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2).Inverse().Scaled(new Vector3(Scale, Scale, Scale)), Vector3.Zero);
        att.AddChild(root);
        return true;
    }

    /// <summary>ReleaseGuides' part for m_spGuide2: the carried model leaves the body.</summary>
    static void Release(Skeleton3D skel)
    {
        if (skel.GetNodeOrNull(AttachName) is { } old)
        {
            skel.RemoveChild(old);
            old.QueueFree();
        }
    }

    /// <summary>Whether a carried model hangs on the body.</summary>
    public static bool Carries(Node3D visual) =>
        visual != null && GodotObject.IsInstanceValid(visual) && visual.GetNodeOrNull<Skeleton3D>(SkeletonPath)?.GetNodeOrNull(AttachName) != null;
}
