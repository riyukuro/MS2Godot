using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2.Ui.Guide;

/// <summary>
/// The Ui part of a Trigger packet (RecvOp 0x4E), type 9: CTriggerService::FUN_1418627c0 (@0x1418627c0,
/// triggerservice.cpp:631-672) reads a u8 sub (<see cref="TriggerObjects.ReadUi"/>). Sub 1 (AddGuideTrigger @0x141863df0,
/// :1006-1011) adds a guide trigger of type 0x15 (ServerTriggerEvent) with the id. Sub 2 (FUN_141863cc0, :986-995) calls the
/// guide event pane's (UIGuideEvent 0x4e53) ShowSummary with the entity, the text and the duration in ms; sub 3
/// (FUN_141863d80, :997-1004) HideSummary. Without the pane (FindGuideEvent gives 0) they do nothing. Subs 4 and 5
/// (FUN_141863b70, StopCutScene) go to the cut scene movie (<see cref="CutSceneMovie"/>). Sub 0xa (FUN_1418642e0, :1072-1083)
/// turns the own pc to the point's z as its target yaw (PCBase::SetTargetYaw), through the host's hook. Subs 7 and 8 put the
/// own pc in StateTriggerEmotion (0x35, <see cref="TriggerEmotion"/>) through the host's hook: sub 7 (FUN_141863e50
/// @0x141863e50, :1013-1031), the list of names played once (mode 0, flag 0, value 0, vfunc_59 bForce 0); sub 8
/// (FUN_141864010 @0x141864010, :1033-1050), the one name replayed until the duration in ms has passed (mode 1, the flag
/// and the duration read, bForce 1). Without a local player the client reads no more and does nothing. Sub 9
/// (FUN_1418641e0 @0x1418641e0, :1052-1068) is the face emotion of the object of that id, a pc or an npc
/// (<see cref="FaceEmotion.Trigger"/>), through the host's hook.
/// STILL-OPEN: sub 0xa's move to the point (vfunc_21); sub 0xb, the own pc's LookAtPoint; sub 0xc, the typing game.
/// These are read and left to <paramref name="unapplied"/>.
/// </summary>
public static class TriggerGuide
{
    public const byte ShowSummary = TriggerObjects.UiShowSummary, HideSummary = TriggerObjects.UiHideSummary,
        StartMovie = TriggerObjects.UiStartMovie, StopMovie = TriggerObjects.UiStopMovie,
        EmotionList = TriggerObjects.UiEmotions, EmotionTimed = TriggerObjects.UiEmotion, Face = TriggerObjects.UiFaceEmotion;

    /// <summary>
    /// Applies a Trigger packet's body if its type is the Ui type (<see cref="TriggerObjects.Ui"/>, after
    /// <see cref="TriggerObjects.ClientType"/>), returning whether it was; a packet of another type is left to the
    /// field's trigger objects. <paramref name="guide"/> takes sub 1 (null: the session's, <see cref="GuideManager.Current"/>);
    /// <paramref name="turnTo"/> gets sub 0xa's yaw (MS2 Z rotation, degrees); <paramref name="emotion"/> is the own pc's
    /// vfunc_59(0x35) for subs 7 and 8, null without a local player; <paramref name="face"/> gets sub 9's object id and
    /// name; <paramref name="unapplied"/> gets every sub this does not apply.
    /// </summary>
    public static bool Apply(byte[] body, ProtocolProfile profile, IGuideEventPane pane, CutSceneMovie movie = null,
        GuideManager guide = null, System.Action<float> turnTo = null, System.Action<TriggerUiCommand> unapplied = null,
        System.Action<TriggerEmotionData> emotion = null, System.Action<int, string> face = null)
    {
        var r = new ByteReader(body);
        if (r.Available < 1 || TriggerObjects.ClientType(r.ReadByte(), profile) != TriggerObjects.Ui) return false;
        var c = TriggerObjects.ReadUi(r);
        switch (c.Sub)
        {
            case TriggerObjects.UiGuideTrigger:
                (guide ?? GuideManager.Current)?.AddTrigger(GuideTrigger.ServerTriggerEvent, c.Id, 0);
                break;
            case ShowSummary:
                pane?.ShowSummary(c.Id, c.Value, c.Duration);
                break;
            case HideSummary:
                pane?.HideSummary(c.Id);
                break;
            case StartMovie:
                movie?.Start(c.Path, c.Id);
                break;
            case StopMovie:
                movie?.StopIfCutScene(c.Id);   // StopCutScene @0x141863c50
                break;
            case EmotionList when emotion != null:
                emotion(new TriggerEmotionData(c.Emotions, 0, 0, 0, false));
                break;
            case EmotionTimed when emotion != null:
                emotion(new TriggerEmotionData(c.Emotions, 1, c.Flag, c.Value, true));
                break;
            case Face when face != null:
                face(c.Id, c.Text);
                break;
            case TriggerObjects.UiTurn when turnTo != null:
                turnTo(c.Point.Z);
                if (c.Move) unapplied?.Invoke(c);
                break;
            default:
                unapplied?.Invoke(c);
                break;
        }
        return true;
    }
}
