namespace Ms2Godot.World;

/// <summary>
/// A field move in the offline sandbox, kept across the scene reload: the field the next field scene loads and the portal the
/// player arrives at. It stands in for the server's answer to the client's move request (RequestMoveField mode 0: target map
/// and portal id), which sends the field entry for the target map with the player at that portal.
/// </summary>
public static class Travel
{
    public sealed record Move(int FieldId, string XBlock, int PortalId, int FromFieldId)
    {
        /// <summary>A server-given arrival (live mode): the player stands at this point (ms2 coordinates), facing RotZDeg; EntryPortal is the entry's portal (RequestFieldEnter's last int, answered by ResponseFieldEnter).</summary>
        public Ms2.Formats.Model.Vec3? At { get; init; }
        public double RotZDeg { get; init; }
        public int EntryPortal { get; init; }
    }

    /// <summary>The move in progress; consumed by the field scene that loads it.</summary>
    public static Move Pending;

    /// <summary>The loading picture the transition shows (Image pack entry), kept over the reload.</summary>
    public static string LoadingImage;

    /// <summary>The loading tip the transition shows, kept over the reload.</summary>
    public static string LoadingTip;

    /// <summary>
    /// The map the transition's loading screen was started for (UILoadingProgress m_74, StartProgress's argument): the map
    /// WorldService FUN_141bbc580 passes to CUIService FUN_141adb900 (worldservice_1.cpp:10019). It is the entry's field at
    /// OnLoginToGame and OnGameToGame and 2 at OnGameToLogin (networkservice.cpp:913, 953, 935), and 0 for a field move
    /// (WorldService::LoadMap -> FUN_141bbc1d0 -> FUN_141bbc580(arg, 0), worldservice_1.cpp:3353) and RelocateWorld (:969).
    /// Not 0: the screen is a server move's (FUN_1419e0630 -> showMove), whose line reads $s_loading_movechannel_message
    /// in place of the tip.
    /// </summary>
    public static int ProgressMap;

    /// <summary>Scripted proof run state (--sandbox-shots): which leg of the walk runs next.</summary>
    public static int ShotLeg;
}
