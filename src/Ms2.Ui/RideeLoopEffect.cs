namespace Ms2.Ui;

/// <summary>
/// A ridee's loop effect: the riding row's basic loopEffect (RideeData::Basic, unit_140781d80_ridingtable.cpp:380,
/// m_basic.m_strLoopEffect). Ridee::vfunc_12 (@0x141704c80, ridee.cpp:527-534) starts it once the mount's model is in,
/// with the ridee as the actor (SetActor(GetObjectId()) and the ridee's object kind), through CEffectService::FUN_1411a49c0,
/// and keeps the handle as m_nIdleFx (0 in the ctor, ridee.cpp:356). ~Ridee (@0x141704240, ridee.cpp:402-406) stops it
/// (FUN_1411a6060) and clears it, so it lasts while the mount does: to the ride off (PCBase::vfunc_121 releases the
/// ridee) or until its PC leaves. A row without one starts nothing. AvatarRidee keeps the same effect on the UI model
/// (avatarpc.cpp:1365-1370, m_nEffect).
/// A handle belongs to the effect layer it came from (<paramref name="effects"/> of <see cref="Start"/>): once the field's
/// effects are gone it is not stopped in the next field's, where the number may name another effect.
/// </summary>
public sealed class RideeLoopEffect
{
    object scope;

    /// <summary>m_nIdleFx: the kept effect's handle, 0 without one.</summary>
    public int Handle { get; private set; }

    /// <summary>Ridee::vfunc_12's start: nothing for an empty effect or without an effect layer; else the handle
    /// <paramref name="spawn"/> returns (0 when the effect did not spawn). A handle still kept is stopped first, as the
    /// mount it belonged to goes before a new one comes in.</summary>
    public bool Start(string effect, object effects, Func<string, int> spawn, Action<int> stop)
    {
        Stop(effects, stop);
        if (string.IsNullOrEmpty(effect) || effects == null || spawn == null) return false;
        Handle = spawn(effect);
        scope = Handle != 0 ? effects : null;
        return Handle != 0;
    }

    /// <summary>~Ridee's stop: the kept handle is stopped when it came from <paramref name="effects"/>, and cleared either
    /// way. False when nothing was stopped.</summary>
    public bool Stop(object effects, Action<int> stop)
    {
        int h = Handle;
        object s = scope;
        Handle = 0;
        scope = null;
        if (h == 0 || stop == null || effects == null || !ReferenceEquals(s, effects)) return false;
        stop(h);
        return true;
    }
}
