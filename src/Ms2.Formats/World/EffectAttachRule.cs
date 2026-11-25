namespace Ms2.Formats.World;

/// <summary>What a nif part of an effect on an actor hangs on (<see cref="EffectAttachRule.Where"/>).</summary>
public enum EffectHang
{
    /// <summary>No part: the client returns from the attach with no target (vfunc_3, nifeffectparam.cpp:735-736).</summary>
    None,
    /// <summary>Type 0: the world effect root, at the owner's node taken once (AttachToOwnerNode, :1044).</summary>
    World,
    /// <summary>Type 1: the bone named parentNode of the actor's model.</summary>
    Bone,
    /// <summary>Type 1: the actor's effect dummy named parentNode (a node the client attaches to the model at spawn,
    /// <see cref="EffectDummy"/>).</summary>
    Dummy,
    /// <summary>Type 1: the actor's model root (m_hNode), for an empty parentNode or a name no node of the model has.</summary>
    ModelRoot,
    /// <summary>Type 2: the parent of the worn item's mesh named parentNode, the part's root taking that mesh's local transform.</summary>
    ItemMesh,
    /// <summary>Types 3 and 4: the Point01 node of the weapon worn in the left (3) or right (4) hand.</summary>
    WeaponPoint,
}

/// <summary>
/// The attach switch of CNifEffect::vfunc_3 @0x1415496f0 (nifeffectparam.cpp:717-735) for a nif part of an effect on an
/// actor, by the part's attach type (<see cref="EffectPart.AttachKind"/>):
/// - 0, AttachToOwnerNode @0x14154ba40 (:1044-1072): the world effect root;
/// - 1, AttachToModelNode @0x14154bdd0 (:1079-1112): GetObjectByName(parentNode) under the actor's model root (m_hNode),
///   kept when it is a NiNode, else m_hNode itself; no m_hNode makes no part. The model's NiNodes are its skeleton's bones
///   and the effect dummies attached to it (Eff_Body_World, Eff_Foot_World, Eff_UI ...);
/// - 2, FUN_14154c260 (:1118-1137): the parent of the mesh FUN_14154d410 finds among the owner's worn items (none for an
///   owner that is not a PC, :1284-1286), the root taking that mesh's local transform in place of the part's own; none found
///   makes no part;
/// - 3 and 4, AttachToEquipSlot @0x14154c440 (:1142-1151, equip slots 4 and 5): the Point01 node of the weapon worn there
///   (CEquipItem::FindPoint01Node @0x1411dd700, equipitem.cpp:604); an owner that is not a PC, no weapon or no Point01 node
///   makes no part.
/// Types 1-4 then go through FUN_14154c580 (:1158-1172): <see cref="Separate"/> and <see cref="Relative"/>.
/// Type 0's parentNode is looked up the same way (:1048-1053): a bone, a dummy, else the model root.
/// STILL-OPEN: type 1's worn item check (FUN_1411e2610, then FUN_14154d410 on a kind mismatch, :1088-1106) and type 2's
/// fallback to m_hNode for a target of mode 6 (:1129) need the target's mode, which is not passed here; a type 1 name of a
/// worn item's own node (not a body bone) takes the model root.
/// </summary>
public static class EffectAttachRule
{
    /// <summary>The node a type 3 / 4 part hangs on in the weapon's model (FindPoint01Node, equipitem.cpp:609).</summary>
    public const string WeaponPointNode = "Point01";

    /// <summary>The equip slot a type 3 / 4 part reads (vfunc_3, :728-733): 4 (LH) for type 3, 5 (RH) for type 4, else 0.</summary>
    public static int WeaponSlot(int attachKind) => attachKind == 3 ? 4 : attachKind == 4 ? 5 : 0;

    /// <summary>Where a nif part of an effect on an actor hangs.</summary>
    /// <param name="attachKind">The part's attach type.</param>
    /// <param name="hasModel">The actor has a model root (m_hNode).</param>
    /// <param name="parentIsBone">The part's parentNode names a bone of that model.</param>
    /// <param name="parentIsDummy">The part's parentNode names one of the actor's effect dummies.</param>
    /// <param name="ownerIsPc">The effect's owner is a PC.</param>
    /// <param name="itemMeshFound">A worn item has a mesh named parentNode.</param>
    /// <param name="weaponPointFound">The weapon in <see cref="WeaponSlot"/> has a Point01 node.</param>
    public static EffectHang Where(int attachKind, bool hasModel, bool parentIsBone, bool parentIsDummy, bool ownerIsPc, bool itemMeshFound, bool weaponPointFound) =>
        attachKind switch
        {
            1 => !hasModel ? EffectHang.None : parentIsBone ? EffectHang.Bone : parentIsDummy ? EffectHang.Dummy : EffectHang.ModelRoot,
            2 => hasModel && ownerIsPc && itemMeshFound ? EffectHang.ItemMesh : EffectHang.None,
            3 or 4 => ownerIsPc && weaponPointFound ? EffectHang.WeaponPoint : EffectHang.None,
            _ => EffectHang.World,
        };

    /// <summary>FUN_14154c580 (:1160-1165): the part's root hangs from a SeparateNode (SeparateNode::UpdateWorldData
    /// @0x1417514f0: identity, then the target world's translate / rotation / scale per update bit) when the update bits are
    /// not all set.</summary>
    public static bool Separate(EffectPart part) => !(part.UpdateTranslate && part.UpdateRotation && part.UpdateScale);

    /// <summary>FUN_14154c580 (:1166-1171): without applyNodeTransform the root's local transform becomes the inverse of the
    /// target's world times its own, so it starts at its own transform in the world. With a SeparateNode (<see cref="Separate"/>)
    /// the root is that node, whose world ignores its local (separatenode.cpp:56-70), so the part stays at the node.</summary>
    public static bool Relative(EffectPart part) => !part.ApplyNodeTransform;
}
