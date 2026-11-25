using System;
using System.Collections.Generic;
using Ms2.Formats.Model;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>The click picks of the mouse controller (CMouseInteract subclasses, mouseinteract.cpp), in its try order.</summary>
public enum MousePick
{
    None,
    /// <summary>CMouseInteractTalkCancel: a click anywhere while the npc talk shows ends it.</summary>
    TalkCancel,
    /// <summary>CMouseInteractTaxi: a taxi station (the MS2TaxiStation model, 0x85359406).</summary>
    Taxi,
    /// <summary>CMouseInteractUgcBanner: a ugc banner or prop (the MS2UccProp model, 0xc7f9dd65).</summary>
    UgcBanner,
    /// <summary>CMouseInteractWeaponObject: a weapon cube lying in the field.</summary>
    WeaponObject,
    /// <summary>CMouseInteractPortal.</summary>
    Portal,
    /// <summary>CMouseInteractObject: an interact object.</summary>
    Object,
    /// <summary>CMouseInteractNpc (<see cref="NpcMousePick"/>).</summary>
    Npc,
}

/// <summary>
/// CMouseController's picks (unit_140ad1860_mousecontroller.cpp). The ctor (:281-311) adds them in the order of
/// <see cref="Order"/>. A release with feature 0xe2 tries them in that order and the first that hits is kept (FUN_140ad3550,
/// :806-830), with no distance limit. The player walks to the pick's point. Each frame UpdateHover (:668-716) checks the
/// kept pick against the action key's current action (vfunc_3). Once it matches, the move stops and the current action runs
/// (vfunc_4, ActivateCurrentAction), so a pick fires only once the player is where the action key would use the same thing.
/// The hover cursor (GetCursorKind, :636-664) tries the same order every 100 ms. A hit farther from the player than
/// MouseInteractLimitDistance is passed over for the next pick.
/// </summary>
public static class MouseInteract
{
    /// <summary>CMouseController::CMouseController's m_arrInteract order (:283-310).</summary>
    public static readonly MousePick[] Order =
    {
        MousePick.TalkCancel, MousePick.Taxi, MousePick.UgcBanner, MousePick.WeaponObject, MousePick.Portal, MousePick.Object,
        MousePick.Npc,
    };

    /// <summary>The picks' vfunc_6, the cursor kind GetCursorKind returns (unit_140ad1860_mousecontroller.cpp:1064-1095,
    /// mouseinteract.cpp:562, 575): npc 1, portal 2, object 3, taxi 4, ugc banner 5, talk cancel 6, weapon object 7.</summary>
    public static int CursorKind(MousePick p) => p switch
    {
        MousePick.Npc => 1,
        MousePick.Portal => 2,
        MousePick.Object => 3,
        MousePick.Taxi => 4,
        MousePick.UgcBanner => 5,
        MousePick.TalkCancel => 6,
        MousePick.WeaponObject => 7,
        _ => 0,
    };

    /// <summary>Common::Constants MouseInteractLimitDistance (constantsblocks.cpp:1560, clientconstants.h:828 +0xe668), the
    /// hover cursor's pick range.</summary>
    public const string LimitDistanceKey = "MouseInteractLimitDistance";
    public const int DefaultLimitDistance = 2000;

    /// <summary>The taxi pick: model 0x85359406 (MS2TaxiStation), pick size 150, action key 8 (CMouseInteractTaxi ctor,
    /// mouseinteract.cpp:554-560).</summary>
    public const float TaxiPickSize = 150f;
    public const int TaxiActionKey = 8;
    /// <summary>The ugc banner pick: model 0xc7f9dd65 (the MS2UccProp mixin that MS2UccAD, MS2ADBalloon and the other ucc
    /// models take), pick size 225, action key 0x10 (RegistUgcAction) (CMouseInteractUgcBanner ctor, :567-573).</summary>
    public const string UgcBannerMixin = "MS2UccProp";
    public const float UgcBannerPickSize = 225f;
    public const int UgcBannerActionKey = 0x10;
    /// <summary>CMouseInteractWeaponObject::FindTarget's size (mouseinteract.cpp:690) and its action key, PullObjectWeaponAction
    /// 0xd (vfunc_3, :663).</summary>
    public const float WeaponPickSize = 150f;

    /// <summary>
    /// The shape every pick but the npc one tests (FindTarget in mouseinteract.cpp:415, 492, 537, 692): a sphere standing on
    /// <paramref name="basePoint"/> (MS2 axes, Z up), centre half <paramref name="size"/> above it, radius half the size,
    /// that the ray hits (FUN_14147ad10, <see cref="NpcMousePick.RayHitsSphere"/>).
    /// </summary>
    public static bool HitsStanding(Vec3 origin, Vec3 dir, Vec3 basePoint, double size)
    {
        double half = size * 0.5;
        return NpcMousePick.RayHitsSphere(ToNum(origin), ToNum(dir), ToNum(basePoint + new Vec3(0, 0, half)), (float)half);
    }

    static System.Numerics.Vector3 ToNum(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// CMouseInteractPortal::FindTarget (mouseinteract.cpp:393-421): over the field objects in id order, a registered portal
    /// with an entity, whose enabled property (0x14969c28, <see cref="LivePortal.Glow"/>) is set and whose action kind
    /// (0x1b8b1161) is not 1. Its sphere stands on its position and is as tall as its PortalDimension Z (+0x44, read from
    /// 0xfb190620 as the portal's size, portal.cpp:115, 167). The portal's entity props are kept only for spawned portals.
    /// <see cref="LivePortal.Kind"/> is null for a bound one, which is never kind 1.
    /// </summary>
    public static LivePortal FindPortal(IReadOnlyList<LivePortal> portals, Vec3 origin, Vec3 dir)
    {
        if (portals == null) return null;
        LivePortal found = null;
        for (int i = 0; i < portals.Count; i++)
        {
            var p = portals[i];
            if (p == null || !p.HasEntity || !p.Glow || p.Kind == 1) continue;
            if (found != null && p.Id >= found.Id) continue;       // the field object map is keyed by id
            if (HitsStanding(origin, dir, p.Position, p.Dimension.Z)) found = p;
        }
        return found;
    }

    /// <summary>
    /// CMouseInteractObject::FindTarget (mouseinteract.cpp:476-497): over the interact objects in their list order, the first
    /// one the local player may use (CInteractObject::vfunc_4, <see cref="LiveInteractObjects.CanUse"/>) with a row whose
    /// mesh shapeDimension Z (CInteractObjectData +0x90, m_mesh.m_shapeDimension.z) is not 0, and whose sphere of that size
    /// the ray hits.
    /// </summary>
    public static LiveInteractObject FindObject(IReadOnlyList<LiveInteractObject> objects, Func<LiveInteractObject, bool> canUse,
        Vec3 origin, Vec3 dir)
    {
        if (objects == null) return null;
        for (int i = 0; i < objects.Count; i++)
        {
            var o = objects[i];
            if (o == null || !canUse(o) || o.Row == null) continue;
            double size = o.Row.ShapeDimension.Z;
            if (Math.Abs(size) < 1e-6) continue;                    // IsNearlyEqual(fSize, 0)
            if (HitsStanding(origin, dir, o.Position, size)) return o;
        }
        return null;
    }

    /// <summary>CMouseInteractDynamicModel::FindTarget (mouseinteract.cpp:528-543): the first of the model's entities, by
    /// their position (0xfd95ca52), whose sphere of the pick's size the ray hits; -1 when none.</summary>
    public static int FindModel(IReadOnlyList<Vec3> positions, float size, Vec3 origin, Vec3 dir)
    {
        if (positions == null) return -1;
        for (int i = 0; i < positions.Count; i++)
            if (HitsStanding(origin, dir, positions[i], size)) return i;
        return -1;
    }

    /// <summary>CMouseInteractObject::vfunc_2 (mouseinteract.cpp:434-453): the action key the object pick waits for, by the
    /// object's kind (vfunc_5): a billboard (8) BillBoardInteractAction 0x18, a gathering object (6) GatheringAction 4, any
    /// other InteractAction 0x12.</summary>
    public static int ObjectActionKey(int kind) => kind switch
    {
        InteractObjectMessage.KindBillBoard => 0x18,
        InteractObjectMessage.KindGathering => 4,
        _ => 0x12,
    };

    /// <summary>MyPC::FUN_141527160(0x19) (mypc.cpp:1873-1881), which the object pick's vfunc_3 asks (mouseinteract.cpp:462):
    /// the pc is in state 1, 0xb, 0xf, 0x21 or 0x3a.</summary>
    public static bool ObjectStateAllows(int stateId) => stateId is 1 or 0xb or 0xf or 0x21 or 0x3a;

    /// <summary>GetCursorKind's range test (unit_140ad1860_mousecontroller.cpp:656-658): a hit whose pick point is farther from
    /// the player than the limit is passed over.</summary>
    public static bool InHoverRange(Vec3 player, Vec3 pick, double limit)
    {
        var d = pick - player;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z) <= limit;
    }

    /// <summary>FUN_14028dfa0 (commonutil.cpp:868-882): the common string of the key formatted with the args, "" when empty.</summary>
    static string Common(GameTables t, string key, params object[] args) => ClientFormat.Format(t.Common(key) ?? "", args);

    /// <summary>CMouseInteractNpc::vfunc_1's hint (mouseinteract.cpp:252-259): an npc someone holds (PCBase::vfunc_117, m_b88 not
    /// empty) gives s_mouse_interact_hold with its name; any other s_mouse_interact_talk with its action name (string table
    /// 0xe, actionName, by npc id, or common 0x62d when it has none) and its name (CNpc::GetName, npcName).</summary>
    public static string NpcHint(GameTables t, int npcId, bool held)
    {
        string name = t.ReservedWords.Lookup("npcname", npcId);
        if (held) return Common(t, "s_mouse_interact_hold", name);
        string action = t.ReservedWords.Lookup("actionname", npcId);
        if (action.Length == 0) action = t.Common(0x62d);
        return Common(t, "s_mouse_interact_talk", action, name);
    }

    /// <summary>CMouseInteractPortal::vfunc_1's hint (mouseinteract.cpp:358-361): s_mouse_interact_portal with the target map's
    /// name (string table 3, mapName), or the portal's action text (<see cref="PortalActionText"/>) when it has no target.</summary>
    public static string PortalHint(GameTables t, int type, int targetMap) =>
        targetMap == 0 ? PortalActionText(t, type) : Common(t, "s_mouse_interact_portal", t.ReservedWords.Lookup("mapname", targetMap));

    /// <summary>CPortal::GetActionText @0x14169c3b0 (portal.cpp:332-352), by the portal's type.</summary>
    public static string PortalActionText(GameTables t, int type) => type switch
    {
        8 => t.Common("s_action_portal_DungeonLobby") ?? "",
        9 => t.Common("s_action_portal_DungeonRoomIn") ?? "",
        0xc => t.Common("s_action_portal_ChaosDungeon") ?? "",
        0xd => t.Common("s_action_portal_MoveBackupField") ?? "",
        0xe => t.Common("s_action_portal_ReverseRaid") ?? "",
        0x12 => t.Common("s_action_portal_RandomTarget") ?? "",
        _ => t.Common(0x632),
    };

    /// <summary>CMouseInteractObject::vfunc_1's hint (mouseinteract.cpp:430): s_mouse_interact_object with the object's action
    /// (string table 0xb, stringInteractObjectAction, by its row id).</summary>
    public static string ObjectHint(GameTables t, int rowId) =>
        Common(t, "s_mouse_interact_object", t.ReservedWords.Lookup("stringinteractobjectaction", rowId));

    /// <summary>The fixed hints: CMouseInteractDynamicModel::vfunc_1 (:505) gives its m_strCursor, s_mouse_interact_taxi (:559) or
    /// s_mouse_interact_ugcbanner (:572); CMouseInteractTalkCancel s_mouse_interact_talk_end (:603).</summary>
    public static string FixedHint(GameTables t, MousePick p) => p switch
    {
        MousePick.Taxi => t.Common("s_mouse_interact_taxi") ?? "",
        MousePick.UgcBanner => t.Common("s_mouse_interact_ugcbanner") ?? "",
        MousePick.TalkCancel => t.Common("s_mouse_interact_talk_end") ?? "",
        _ => "",
    };
}
