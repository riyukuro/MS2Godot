using System.Collections.Generic;
using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The popup menu on a player clicked in the field: OnRightMouseDown (@0x141a8a010, uipartydialog.cpp:3619) and
/// FUN_141a8ab90 (:3760). A right press the HUD's movies did not take picks the field under the cursor; the PCs hit are
/// sorted by their screen distance to the cursor (Unk_PdCursor, :3604) and the first one opens the menu in mode 1 with
/// flags 0x3f3fe (<see cref="PopupMenuController.OpenOnFieldPc"/>). Nothing picked closes the menu (ClosePopupMenu).
/// MS2-FUNCTIONAL: the client picks the scene's meshes (CameraMgr::FUN_140af0850, NiPick); here the ray meets the PCs'
/// controller capsules (collision layer PcLayer). STILL-OPEN: the pet branch of FUN_141a8ab90 (flags 0xc00 / 0x440), the
/// hits without a game object (FUN_141a8b460, FUN_141a8b670: the cube and housing objects) and the scene entity property
/// lookup (:3678-3685) open nothing here.
/// </summary>
public partial class HudScene
{
    readonly RayQuery popupRay = RayQuery.CastRay(Ms2Godot.Movement.MoveConfig.PcLayer);
    readonly List<(float Distance, ulong Id)> popupHits = new();

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mb) return;
        if (!OnFieldRightMouseDown(mb.Position)) return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>OnRightMouseDown at a window position; true when a menu was opened.</summary>
    bool OnFieldRightMouseDown(Vector2 at)
    {
        if (hudModel == null || Ms2.Ui.FieldMoveCover.Up) return false;
        if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player me) return false;
        // FUN_141a8b910 (:3923): not in states 0x3f and 0xe
        if (me.ClientStateId is 0x3f or 0xe) return false;
        var camera = GetViewport().GetCamera3D();
        if (camera == null) return false;
        var live = Ms2Godot.Net.LiveSession.Instance;
        // the sub-scene 0x620001f opens nothing (:3633)
        if (live?.Guide?.World.SubSceneId == 0x620001f) return false;
        var space = me.GetWorld3D().DirectSpaceState;
        var from = camera.ProjectRayOrigin(at);
        var to = from + camera.ProjectRayNormal(at) * 20000f;
        popupHits.Clear();
        popupRay.Set(from, to, default);
        popupRay.Ex.Clear();
        for (int i = 0; i < 16; i++)
        {
            popupRay.Query.Exclude = popupRay.Ex;
            using var hit = space.IntersectRay(popupRay.Query);
            if (hit.Count == 0) break;
            var rid = (Rid)hit[PhysicsKeys.Rid];
            ulong id = (ulong)hit[PhysicsKeys.ColliderId];
            popupRay.Ex.Add(rid);
            var screen = camera.UnprojectPosition(InstancePosition(id));
            popupHits.Add((screen.DistanceTo(at), id));
        }
        popupHits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var menu = EnsurePopupMenu();
        PlaceFieldPopup(menu, camera);
        foreach (var (_, id) in popupHits)
            if (OpenFieldPcPopup(menu, me, id)) return true;
        menu.ClosePopup();                                          // ClosePopupMenu (:3690)
        return false;
    }

    static Vector3 InstancePosition(ulong id) => GodotObject.InstanceFromId(id) is Node3D n ? n.GlobalPosition : default;

    /// <summary>FUN_141a8ab90 for a PC: the own PC (GetTargetKind 1) or another player's (2); false for anything else.</summary>
    bool OpenFieldPcPopup(PopupMenuController menu, Player me, ulong id)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (me.GetInstanceId() == id)
        {
            var self = hudModel.Player;
            var p = me.GlobalPosition;
            long own = live?.CharacterId ?? 0;
            menu.OpenOnFieldPc((long)id, 1, new PopupMenuTarget
            {
                World = new System.Numerics.Vector3(p.X, -p.Z, p.Y), Name = self.Name ?? "", CharacterId = own,
                Level = (ushort)self.Level, Alive = !(live?.Me.Dead ?? self.HpCur <= 0), JobGroup = self.JobGroup,
                JobCode = self.SubJob, Trophy = (int)(live?.AchieveSum ?? 0),
                Image = Ms2Godot.Net.ProfileImageService.Images.Path(own, "", true),
            });
            return true;
        }
        if (live?.PopupTargetOf(id, (cid, picture) => Ms2Godot.Net.ProfileImageService.Images.Path(cid, picture, true)) is not { } t)
            return false;
        menu.OpenOnFieldPc(t.ObjectId, 2, t.Target);
        return true;
    }

    /// <summary>Open's mode 1 inputs: CameraMgr::FUN_140af0700 at the movie's stage units, GetCameraHeightOffset of the
    /// field camera (nearest m_58, farthest m_54, distance m_50) in stage units, and OffsetPcPopupMenu.</summary>
    void PlaceFieldPopup(PopupMenuController menu, Camera3D camera)
    {
        slotConstants ??= Ms2.Ui.FeatureElements.Constants(assets.Packs);
        menu.OffsetPcPopupMenu = slotConstants.Float("OffsetPcPopupMenu", 0f);
        menu.WorldToMovie = w =>
        {
            var g = new Vector3(w.X, w.Z, -w.Y);
            if (camera.IsPositionBehind(g)) return null;
            var p = camera.UnprojectPosition(g);
            return (p.X / u, p.Y / u);
        };
        menu.MenuHeightOffset = () =>
        {
            if (GetTree().GetFirstNodeInGroup(Field.Group) is not Field f) return 0;
            uint screenH = (uint)GetViewport().GetVisibleRect().Size.Y;
            return PopupMenuController.CameraHeightOffset(Field.MinCameraDistance, f.CameraFarthest, f.CameraDistance, screenH) / u;
        };
    }
}
