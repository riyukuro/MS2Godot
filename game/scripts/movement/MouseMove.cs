using Godot;

namespace Ms2Godot.Movement;

/// <summary>
/// Click to move (the client's mouse controller, option interface/EnableMouseControl, default off; it also needs feature
/// 0xaf). Releasing the left button on a cube sets a target; the direction to it is taken once and walked straight, with
/// no path finding. The move ends when the distance starts growing within 75 of the target, after 15 of growth in
/// total, when a probe (every 100 ms from 75 up along the facing, 50 long) hits a cube, or on any keyboard direction.
/// With feature 0xe2 "MouseInteract" a release first tries the click picks (CMouseController::FUN_140ad3550): of them only the
/// npc one (CMouseInteractNpc, <see cref="World.NpcTalk.FindClickTarget"/>) is here. A picked npc is walked to, and each frame
/// (UpdateHover) once the talk key's npc is that npc the walk stops and the talk starts. The picked npc is kept until the next
/// click or the talk (FUN_140ad40b0), not cleared when the walk ends.
/// STILL-OPEN: the other picks (talk cancel, taxi, ugc banner, weapon object, portal, interact object; tried before the npc,
/// first hit wins), the hover cursor and its hint (GetCursorKind, s_mouse_interact_talk) and holding (action 6).
/// </summary>
public partial class MouseMove : Node
{
    readonly RayQuery ray = new();
    readonly Player player;
    readonly Camera3D camera;
    Vector3 dir;                  // world, flat, unit; zero when not moving
    Vector3 target;
    float lastDist, growth, probeT;
    World.FieldNpcs.Placed clickedNpc;    // m_spCurrent's npc (CMouseInteractNpc m_nNpcId)
    readonly bool interact = Ms2Godot.GameOptions.FeatureEnabled(0xe2);

    public MouseMove(Player player, Camera3D camera)
    {
        this.player = player;
        this.camera = camera;
        ProcessPriority = -5;      // before the player reads its input (Player.ProcessPriority -1)
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb) return;
        if (Ms2.Ui.FieldMoveCover.Up) return;       // no field to click under a move's loading screen
        var from = camera.ProjectRayOrigin(mb.Position);
        var normal = camera.ProjectRayNormal(mb.Position);
        if (interact)
        {
            clickedNpc = World.NpcTalk.Current?.FindClickTarget(from, normal);
            if (clickedNpc != null) { Go(clickedNpc.Position); return; }
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
        if (interact && clickedNpc != null && World.NpcTalk.Current is { } talk)
        {
            if (!IsInstanceValid(clickedNpc.Root)) clickedNpc = null;
            else if (talk.TalkIfCurrent(clickedNpc)) { clickedNpc = null; Stop(); return; }
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

    void Stop()
    {
        dir = Vector3.Zero;
        player.MouseInput = Vector2.Zero;
    }
}
