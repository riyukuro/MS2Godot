using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>
/// The vibrate parts' camera shakes (CVibrateEffect, vibrateeffectparam.cpp) on the one camera manager's effect queue
/// (<see cref="Effects"/>); the field runs the queue on its follow camera each frame.
/// STILL-OPEN: the gamepad rumble (feature 0xc3, StartRumbleFor) is not made.
/// </summary>
public static class CameraShakes
{
    /// <summary>CameraMgr's camera effects; one for the game, as the client's singleton.</summary>
    public static readonly CameraEffects Effects = new();

    /// <summary>The game time the effects run on (CMainSystem::GetGameTime), in seconds.</summary>
    public static double Now => Time.GetTicksUsec() / 1e6;

    /// <summary>The camera option's vibrateEffect (CGameOption camera m_b01).</summary>
    public static bool Option => GameOptions.Current.CameraOption.VibrateEffect;

    /// <summary>
    /// Whether CreateEffect makes a vibrate part for <paramref name="owner"/>: CVibrateEffectParam::vfunc_7 (:96-104)
    /// refuses an effect of kind 3 (another PC's) besides CEffectParam::vfunc_7's filters (the caller's
    /// <see cref="EffectPart.SkippedFor"/>), and CVibrateEffect::vfunc_3 (:154-157) is false while the camera effects are off
    /// (FUN_140aed0f0). A param the service makes is assumed (<see cref="EffectRandom.ParamMade"/>).
    /// </summary>
    public static bool Makes(IReadOnlyList<EffectPart> parts, EffectPart part, in EffectOwner owner) =>
        part.TypeId == EffectSkipTable.TypeVibrate && owner.Kind != 3 && Effects.On(Option) && EffectRandom.ParamMade(parts, part);

    /// <summary>CVibrateEffect::vfunc_3 (:158-164): when <see cref="Makes"/>, and the camera is set up in a field
    /// (FUN_140a18070, m_1b0: a field's camera, <see cref="Field.Active"/>), queues the part's shake from now. True when the
    /// part is made.</summary>
    public static bool Play(IReadOnlyList<EffectPart> parts, EffectPart part, in EffectOwner owner)
    {
        if (!Makes(parts, part, owner)) return false;
        if (Field.Active != null) Effects.Add(CameraVibrate.For(part, Now), Option);
        Played++;
        return true;
    }

    /// <summary>
    /// CameraMgr::Update's effects after the mode's update (FUN_140af1de0, camera.cpp:1687-1706) on <paramref name="camera"/>
    /// at game time <paramref name="now"/>: the first queued shake moves the shown eye (client space, the camera's position
    /// turned there and back) and the camera takes the moved eye, its rotation kept. True when it moved.
    /// </summary>
    public static bool Apply(Camera3D camera, double now)
    {
        if (Effects.Count == 0) return false;
        var g = camera.GlobalPosition;
        var eye = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        if (!Effects.Update(now, ref eye)) return false;
        camera.GlobalPosition = new Vector3(eye.X, eye.Z, -eye.Y);
        return true;
    }

    /// <summary>Vibrate parts made (<see cref="Play"/>).</summary>
    public static int Played;
}
