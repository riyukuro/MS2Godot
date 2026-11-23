using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// The owner an effect is inserted for (CEffectService's NoticeMessage target), as CEffectParam::vfunc_7 @0x14119e940
/// (effectservice.cpp:245-259) and CSoundEventEffect::vfunc_3 @0x141776760 (soundeventmgr.cpp:456-494) read it.
/// STILL-OPEN: another PC's or an npc's gender and job code are not passed to the effect plays yet, so for them only the kind
/// and the target are tested (<see cref="EffectPart.SkippedFor"/>).
/// </summary>
public static class EffectOwners
{
    /// <summary>The owner of an effect of caster kind <paramref name="casterKind"/> (SoundMusicEffectState: 1 the own PC) on
    /// <paramref name="actor"/> (null for one at a place): the own PC's gender and job code when it is the caster; the target
    /// is the own PC when the actor is its body or its visual root.</summary>
    public static EffectOwner Of(int casterKind, Node3D actor)
    {
        var live = LiveSession.Instance;
        var me = live?.OwnPlayer;
        bool targetMine = actor != null && me != null && GodotObject.IsInstanceValid(me) && (actor == me || actor == me.Visual);
        if (casterKind == EffectOwner.KindMyPc && live?.Me.User is { } u) return new EffectOwner(casterKind, u.Gender, u.JobCode, targetMine);
        return EffectOwner.OfKind(casterKind, targetMine);
    }
}
