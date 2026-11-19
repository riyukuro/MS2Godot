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

    /// <summary>Scripted proof run state (--sandbox-shots): which leg of the walk runs next.</summary>
    public static int ShotLeg;
}
