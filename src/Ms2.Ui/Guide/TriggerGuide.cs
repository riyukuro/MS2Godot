using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2.Ui.Guide;

/// <summary>
/// The guide part of a Trigger packet (RecvOp 0x4E), type 9: CTriggerService::FUN_1418627c0 (@0x1418627c0,
/// triggerservice.cpp:631-672) reads a u8 sub. Sub 2 (FUN_141863cc0 @0x141863cc0, :986-995) reads three i32, the guide
/// entity, the text and the duration in ms, and calls the guide event pane's (UIGuideEvent 0x4e53) ShowSummary with
/// them; sub 3 (FUN_141863d80 @0x141863d80, :997-1004) reads one i32 and calls HideSummary. Without the pane
/// (FindGuideEvent gives 0) the packet does nothing.
/// STILL-OPEN: the other subs: 1 AddGuideTrigger, 4 and 5 the cut scene movie, 7 and 8 the emotion state, 9 the face
/// emotion, 0xa and 0xb the own pc's turn and look, 0xc the typing game.
/// </summary>
public static class TriggerGuide
{
    public const byte ShowSummary = 2, HideSummary = 3;

    /// <summary>
    /// Applies a Trigger packet's body if its type is the guide type (<see cref="TriggerObjects.Guide"/>, after
    /// <see cref="TriggerObjects.ClientType"/>), returning whether it was; a packet of another type is left to the
    /// field's trigger objects.
    /// </summary>
    public static bool Apply(byte[] body, ProtocolProfile profile, IGuideEventPane pane)
    {
        var r = new ByteReader(body);
        if (r.Available < 1 || TriggerObjects.ClientType(r.ReadByte(), profile) != TriggerObjects.Guide) return false;
        switch (r.ReadByte())
        {
            case ShowSummary:
            {
                int entity = r.ReadInt(), text = r.ReadInt(), duration = r.ReadInt();
                pane?.ShowSummary(entity, text, duration);
                break;
            }
            case HideSummary:
            {
                int entity = r.ReadInt();
                pane?.HideSummary(entity);
                break;
            }
        }
        return true;
    }
}
