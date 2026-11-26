using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// Click to move (the client's mouse controller, option interface/EnableMouseControl, default off; it also needs feature
/// 0xaf). Releasing the left button on a cube sets a target; the direction to it is taken once and walked straight, with
/// no path finding. The move ends when the distance starts growing within 75 of the target, after 15 of growth in
/// total, when a probe (every 100 ms from 75 up along the facing, 50 long) hits a cube, or on any keyboard direction.
/// With feature 0xe2 "MouseInteract" a release first tries the click picks (CMouseController::FUN_140ad3550,
/// <see cref="Ms2.Ui.MouseInteract"/>) in the client's order: talk cancel, taxi, ugc banner, weapon object, portal, interact
/// object, npc (<see cref="World.NpcTalk.FindClickTarget"/>). The first hit is kept and walked to. Each frame (UpdateHover) the
/// kept pick is checked against the action key's current action (the picks' vfunc_3). Once it matches, the walk stops and that
/// action runs (vfunc_4, ActivateCurrentAction): the talk, the taxi, the portal or the object, as the action key would. A
/// click during the npc talk ends it (SendNpcTalkMode0). The kept pick is cleared by the next click or once it runs
/// (FUN_140ad40b0), not when the walk ends.
/// STILL-OPEN: the ugc banner pick is walked to but never runs (RegistUgcAction, action key 0x10, is not ported); the weapon
/// object pick (CubeMapInformationService's weapon cubes, PullObjectWeaponAction 0xd) is not ported; holding (action 6) and the
/// held npc's hint (s_mouse_interact_hold).
/// Every 100 ms the same picks under the mouse, within MouseInteractLimitDistance of the player, set the cursor movie's
/// interact cursor and the pick's hint beside it (UpdateHover, GetCursorKind, <see cref="Ui.MouseCursorLayer"/>).
/// </summary>
public partial class MouseMove : Node
{
    readonly RayQuery ray = new();
    readonly Player player;
    readonly Camera3D camera;
    Vector3 dir;                  // world, flat, unit; zero when not moving
    Vector3 target;
    float lastDist, growth, probeT;
    // m_spCurrent: the kept pick and what its vfunc_3 compares
    Ms2.Ui.MousePick pick;
    World.FieldNpcs.Placed clickedNpc;    // CMouseInteractNpc m_nNpcId
    bool clickedNpcHold;                  // CMouseInteractNpc m_nAction: 6 (HoldAction) for an npc with hold nodes, else 0x14
    int pickPortalTarget;                 // CMouseInteractPortal m_nPortalId (the portal's target map, FUN_14169d280)
    string pickObject;                    // CMouseInteractObject m_strObjectId
    int pickObjectAction;                 // CMouseInteractObject m_nAction
    readonly bool interact = Ms2Godot.GameOptions.FeatureEnabled(0xe2);
    /// <summary>The last pick a click kept and the last that ran (logged; the scripted live runs read them).</summary>
    public static Ms2.Ui.MousePick LastKept, LastRan;
    /// <summary>The last hover cursor kind set (logged; the scripted live runs read it).</summary>
    public static int LastHoverKind;
    /// <summary>The last hint the hover set (the scripted live runs read it).</summary>
    public static string LastHint;
    double hoverT;
    Ms2.Ui.GameTables hintTables;
    int? limitDistance;                   // Common::Constants MouseInteractLimitDistance (m_pickRange)

    public MouseMove(Player player, Camera3D camera)
    {
        this.player = player;
        this.camera = camera;
        ProcessPriority = -5;      // before the player reads its input (Player.ProcessPriority -1)
    }

    readonly Ms2.Ui.MouseReleaseGate gate = new();

    /// <summary>
    /// CMouseController::OnMouseMessage (@0x140ad1da0): the press and the release read the UI movie's sampled flag
    /// (<see cref="Ui.As3View.AnyMouseOverUi"/>), not what any view's event handler did, so this sees the event before the views
    /// (_Input runs before every _UnhandledInput): a press over a dialog's sample is ignored, with its release (m_nIgnoreRelease);
    /// a press taken in the field walks on its release even when it ends over a dialog (<see cref="Ms2.Ui.MouseReleaseGate"/>).
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        bool over = Ui.As3View.AnyMouseOverUi(GetViewport());
        if (mb.Pressed) { gate.Press(over); return; }
        if (gate.Release(over)) Release(mb);
    }

    void Release(InputEventMouseButton mb)
    {
        if (Ms2.Ui.FieldMoveCover.Up) return;       // no field to click under a move's loading screen
        var from = camera.ProjectRayOrigin(mb.Position);
        var normal = camera.ProjectRayNormal(mb.Position);
        if (interact && TryPicks(from, normal, out var picked))
        {
            LastKept = pick;
            GD.Print($"mouse: pick {pick} kept, walk to {World.FieldPortals.ToMs2(picked)}");
            Go(picked);
            return;
        }
        var to = from + normal * 20000f;
        using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray.Set(from, to, player.GetRid()));
        if (hit.Count == 0) return;
        var p = (Vector3)hit[PhysicsKeys.Position];
        var n = (Vector3)hit[PhysicsKeys.Normal];
        if (player.Cubes.CubeAt(p - n) == null) return;     // only cube blocks are picked
        Go(p);
    }

    /// <summary>Starts a move to a point (Godot space).</summary>
    public void Go(Vector3 point)
    {
        target = point;
        var d = point - player.GlobalPosition;
        d.Y = 0;
        if (d.LengthSquared() < 1f) return;
        dir = d.Normalized();
        lastDist = d.Length();
        growth = 0;
        probeT = 0;
    }

    public bool Moving => dir != Vector3.Zero;

    public override void _Process(double delta)
    {
        if (interact) UpdateHoverCursor(delta);
        if (interact && pick != Ms2.Ui.MousePick.None)
        {
            var kept = pick;
            if (RunPickIfCurrent()) { LastRan = kept; GD.Print($"mouse: pick {kept} runs"); return; }
        }
        if (!Moving) { player.MouseInput = Vector2.Zero; return; }
        if (player.DeathHoldsMove) { Stop(); return; }   // StateDead::vfunc_4: CPushControl::Reset (statedead.cpp:152)
        if (MoveInput.Read().Move != Vector2.Zero) { Stop(); return; }
        var d = target - player.GlobalPosition;
        d.Y = 0;
        float dist = d.Length();
        if (dist > lastDist)
        {
            if (dist < MoveConfig.MouseArrive) { Stop(); return; }
            growth += dist - lastDist;
            if (growth > MoveConfig.MouseGiveUp) { Stop(); return; }
        }
        lastDist = dist;
        probeT += (float)delta;
        if (probeT >= MoveConfig.MouseProbeEvery && player.State is Player.MoveState.Walk or Player.MoveState.Idle)
        {
            probeT = 0;
            var from = player.GlobalPosition + new Vector3(0, MoveConfig.MouseProbeUp, 0);
            using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray.Set(from, from + dir * MoveConfig.MouseProbeLen, player.GetRid()));
            if (hit.Count > 0 && player.Cubes.CubeAt((Vector3)hit[PhysicsKeys.Position] - (Vector3)hit[PhysicsKeys.Normal]) != null) { Stop(); return; }
        }
        // the world direction as camera relative input
        var fwd = new Vector3(-Mathf.Sin(player.CameraYaw), 0, -Mathf.Cos(player.CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        player.MouseInput = new Vector2(dir.Dot(right), dir.Dot(fwd));
    }

    /// <summary>
    /// FUN_140ad3550 (unit_140ad1860_mousecontroller.cpp:806-830): the kept pick is cleared (FUN_140ad40b0). Each pick's vfunc_2
    /// is then tried in order and the first hit is kept; <paramref name="point"/> is its pick point (vPick, Godot space).
    /// </summary>
    bool TryPicks(Vector3 from, Vector3 normal, out Vector3 point)
    {
        ClearPick();
        point = default;
        var o = World.FieldPortals.ToMs2(from);
        var d = World.FieldPortals.ToMs2(normal);
        var ls = Net.LiveSession.Instance;
        foreach (var p in Ms2.Ui.MouseInteract.Order)
        {
            if (!Hit(p, from, normal, o, d, ls, out point, out var target)) continue;
            pick = p;
            switch (target)
            {
                case Ms2.Ui.LivePortal portal: pickPortalTarget = portal.TargetMap; break;
                case Ms2.Ui.LiveInteractObject obj:
                    pickObject = obj.Name;
                    pickObjectAction = Ms2.Ui.MouseInteract.ObjectActionKey(obj.Kind);
                    break;
                case World.FieldNpcs.Placed npc:
                    // CMouseInteractNpc::vfunc_2 (mouseinteract.cpp:270-274): m_nAction 6 for an npc with hold nodes
                    clickedNpc = npc;
                    clickedNpcHold = ls != null && ls.NpcHoldable(npc.ObjectId);
                    break;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// One pick's hit (its vfunc_2, which vfunc_1 calls before it makes the hint): <paramref name="point"/> is the pick point
    /// (Godot space) and <paramref name="target"/> the portal, interact object or npc hit (null for the others).
    /// </summary>
    bool Hit(Ms2.Ui.MousePick p, Vector3 from, Vector3 normal, Ms2.Formats.Model.Vec3 o, Ms2.Formats.Model.Vec3 d,
        Net.LiveSession ls, out Vector3 point, out object target)
    {
        point = default;
        target = null;
        switch (p)
        {
            case Ms2.Ui.MousePick.TalkCancel:
                // CMouseInteractTalkCancel::vfunc_2 (mouseinteract.cpp:607-620): the player's own point
                if (World.NpcTalk.Current is not { CinematicLoaded: true }) return false;
                point = player.GlobalPosition;
                return true;
            case Ms2.Ui.MousePick.Taxi:
                return ls != null && Pick(ls.TaxiAction.Stations, Ms2.Ui.MouseInteract.TaxiPickSize, o, d, ref point);
            case Ms2.Ui.MousePick.UgcBanner:
                return ls != null && Pick(ls.UgcBanners, Ms2.Ui.MouseInteract.UgcBannerPickSize, o, d, ref point);
            case Ms2.Ui.MousePick.Portal:
                if (Ms2.Ui.MouseInteract.FindPortal(ls?.Portals?.All, o, d) is not { } portal) return false;
                point = FieldBuilder.ToGodotPoint(portal.Position);     // CPortal::GetPosition
                target = portal;
                return true;
            case Ms2.Ui.MousePick.Object:
                if (ls?.InteractObjects is not { } objects
                    || Ms2.Ui.MouseInteract.FindObject(objects.All, objects.CanUse, o, d) is not { } obj) return false;
                point = FieldBuilder.ToGodotPoint(obj.Position);        // CInteractObject::GetPos
                target = obj;
                return true;
            case Ms2.Ui.MousePick.Npc:
                if (World.NpcTalk.Current?.FindClickTarget(from, normal) is not { } npc) return false;
                point = npc.Position;
                target = npc;
                return true;
            default:
                return false;                   // the weapon object pick is not ported
        }
    }

    /// <summary>
    /// GetCursorKind @0x140ad2ac0 (unit_140ad1860_mousecontroller.cpp:636-664): the picks in order under the mouse; a hit whose
    /// point is farther from the player than MouseInteractLimitDistance is passed over for the next pick. Returns the first
    /// near one's cursor kind (vfunc_6) and its hint (vfunc_1's strCursor), or 0.
    /// </summary>
    int CursorKind(Vector2 mouse, out string hint)
    {
        hint = "";
        var from = camera.ProjectRayOrigin(mouse);
        var normal = camera.ProjectRayNormal(mouse);
        var o = World.FieldPortals.ToMs2(from);
        var d = World.FieldPortals.ToMs2(normal);
        var ls = Net.LiveSession.Instance;
        var me = World.FieldPortals.ToMs2(player.GlobalPosition);
        double limit = limitDistance ??= World.FieldContents.Constants(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))
            .Int(Ms2.Ui.MouseInteract.LimitDistanceKey, Ms2.Ui.MouseInteract.DefaultLimitDistance);
        foreach (var p in Ms2.Ui.MouseInteract.Order)
        {
            if (!Hit(p, from, normal, o, d, ls, out var point, out var target)) continue;
            if (!Ms2.Ui.MouseInteract.InHoverRange(me, World.FieldPortals.ToMs2(point), limit)) continue;
            hint = Hint(p, target);
            return Ms2.Ui.MouseInteract.CursorKind(p);
        }
        return 0;
    }

    string Hint(Ms2.Ui.MousePick p, object target)
    {
        var t = hintTables ??= Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
        return target switch
        {
            Ms2.Ui.LivePortal portal => Ms2.Ui.MouseInteract.PortalHint(t, portal.Type, portal.TargetMap),
            Ms2.Ui.LiveInteractObject obj => Ms2.Ui.MouseInteract.ObjectHint(t, obj.RowId),
            World.FieldNpcs.Placed npc => npc.Row == null ? ""
                : Ms2.Ui.MouseInteract.NpcHint(t, npc.Row.Id, Net.LiveSession.Instance is { } hl && hl.NpcHoldable(npc.ObjectId)),
            _ => Ms2.Ui.MouseInteract.FixedHint(t, p),
        };
    }

    /// <summary>
    /// UpdateHover's cursor part (unit_140ad1860_mousecontroller.cpp:672-699), every 100 ms (m_tmPick.Check(100)): while the
    /// mouse is in the window, over no UI (GFxManager::FUN_1422aeed0(2)) and the player is in the field, the kind under it
    /// (<see cref="CursorKind"/>); then, only while the cursor shows the hand or interact, it becomes interact over a pick and
    /// the hand otherwise, and a hint that is not empty goes beside it.
    /// </summary>
    void UpdateHoverCursor(double delta)
    {
        hoverT += delta;
        if (hoverT < 0.1) return;
        hoverT = 0;
        var vp = GetViewport();
        var mouse = vp.GetMousePosition();
        if (!vp.GetVisibleRect().HasPoint(mouse)) return;
        string hint = "";
        int kind = 0;
        if (!Ui.As3View.AnyMouseOverUi(vp) && IsInstanceValid(player)) kind = CursorKind(mouse, out hint);
        ApplyCursor(kind, hint);
    }

    static void ApplyCursor(int kind, string hint)
    {
        if (Ui.MouseCursorLayer.Cursor is not { } cursor) return;
        int current = cursor.GetCursorType();
        if (Ms2.Ui.MouseCursorController.HoverType(current, kind) is not { } wanted) return;
        if (current != wanted) cursor.ChangeType(wanted);
        if (!string.IsNullOrEmpty(hint)) { cursor.SetText(hint); LastHint = hint; }
        if (kind != LastHoverKind) { LastHoverKind = kind; GD.Print($"mouse: hover kind {kind} hint \"{hint}\""); }
    }

    /// <summary>With mouse control off GetCursorKind gives 0 (m_nMode 0), so the next hover sets the hand back.</summary>
    public override void _ExitTree() => ApplyCursor(0, "");

    static bool Pick(System.Collections.Generic.IReadOnlyList<Ms2.Formats.Model.Vec3> at, float size, Ms2.Formats.Model.Vec3 o,
        Ms2.Formats.Model.Vec3 d, ref Vector3 point)
    {
        int i = Ms2.Ui.MouseInteract.FindModel(at, size, o, d);
        if (i < 0) return false;
        point = FieldBuilder.ToGodotPoint(at[i]);      // the entity's position (0xfd95ca52)
        return true;
    }

    /// <summary>
    /// UpdateHover's run (unit_140ad1860_mousecontroller.cpp:705-715): once the kept pick's vfunc_3 holds, the move stops, its
    /// vfunc_4 runs (the current action, ActivateCurrentAction) and the pick is cleared. True when it ran.
    /// </summary>
    bool RunPickIfCurrent()
    {
        var fc = World.FieldContents.Current;
        var ls = Net.LiveSession.Instance;
        switch (pick)
        {
            case Ms2.Ui.MousePick.TalkCancel:
                // vfunc_3 is always true; vfunc_4 SendNpcTalkMode0 (mouseinteract.cpp:623-632)
                Stop(); ClearPick();
                World.NpcTalk.Current?.SendEnd();
                return true;
            case Ms2.Ui.MousePick.Taxi:
                // CMouseInteractDynamicModel::vfunc_3 (:518-526): the current action is the taxi's (8)
                if (fc == null || ls == null || fc.CurrentAction != Ms2.Ui.ActionKeyTarget.Interact || !ls.TaxiPolled) return false;
                break;
            case Ms2.Ui.MousePick.Portal:
                // CMouseInteractPortal::vfunc_3 (:375-386): the current action is PortalAction (0x13) on a portal with the
                // kept target (PortalAction::FUN_140956110)
                if (fc == null || ls == null || fc.CurrentAction != Ms2.Ui.ActionKeyTarget.Portal
                    || ls.PortalAction.Current is not { } current || current.TargetMap != pickPortalTarget) return false;
                break;
            case Ms2.Ui.MousePick.Object:
                if (fc == null || ls?.InteractObjects is not { } objects || !ObjectIsCurrent(fc, ls, objects)) return false;
                break;
            case Ms2.Ui.MousePick.Npc:
                if (clickedNpc == null || !IsInstanceValid(clickedNpc.Root)) { ClearPick(); return false; }
                if (clickedNpcHold)
                {
                    // CMouseInteractNpc::vfunc_3 (:290-296): the current action is HoldAction with the kept npc id
                    if (fc == null || ls == null || fc.CurrentAction != Ms2.Ui.ActionKeyTarget.Hold
                        || ls.Hold.NpcId != clickedNpc.Row?.Id) return false;
                    break;
                }
                if (World.NpcTalk.Current is not { } talk || !talk.TalkIfCurrent(clickedNpc)) return false;
                Stop(); ClearPick();
                return true;
            default:
                return false;                   // the ugc banner's action (0x10) is not ported
        }
        Stop(); ClearPick();
        fc.ActivateCurrentAction();
        return true;
    }

    /// <summary>CMouseInteractObject::vfunc_3 (mouseinteract.cpp:455-468): the current action is the kept one (gathering 4,
    /// interact 0x12 or billboard 0x18), the pc's state allows it (MyPC::FUN_141527160(0x19)) and the action's object is the
    /// kept one.</summary>
    bool ObjectIsCurrent(World.FieldContents fc, Net.LiveSession ls, Ms2.Ui.LiveInteractObjects objects)
    {
        bool current = pickObjectAction switch
        {
            4 => fc.CurrentAction == Ms2.Ui.ActionKeyTarget.Interact && objects.IsGatheringCurrent,
            0x18 => fc.CurrentAction == Ms2.Ui.ActionKeyTarget.BillBoard,
            _ => fc.CurrentAction == Ms2.Ui.ActionKeyTarget.Interact && !objects.IsGatheringCurrent && !ls.TaxiPolled
                 && !ls.InteractNpcPolled,
        };
        return current && Ms2.Ui.MouseInteract.ObjectStateAllows(ls.OwnCurStateIdNow) && objects.Current?.Name == pickObject;
    }

    /// <summary>FUN_140ad40b0: no kept pick.</summary>
    void ClearPick()
    {
        pick = Ms2.Ui.MousePick.None;
        clickedNpc = null;
        clickedNpcHold = false;
        pickPortalTarget = 0;
        pickObject = null;
        pickObjectAction = 0;
    }

    void Stop()
    {
        dir = Vector3.Zero;
        player.MouseInput = Vector2.Zero;
    }
}
