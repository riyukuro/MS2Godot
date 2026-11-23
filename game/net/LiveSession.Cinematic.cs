using Ms2.Net.Packets;

namespace Ms2Godot.Net;

public partial class LiveSession
{
    /// <summary>UICutSceneCinematic::SendTrigger (@0x141909230): the Trigger packet of a skipped cinematic.</summary>
    public void SendCinematicSkipTrigger() { if (game != null) Send(new CinematicSkipTrigger()); }
}
