using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// CEffectService::PlaySystemSound's handle (effectservice.cpp:1288) and its stop by handle (FUN_1411a6060): a UI sound a dialog stops again
/// (the gacha machine's rolling sound, UIItemGachaBoxDialog's OnChangeState and unload). Each plays on its own player, so a
/// stop ends that copy only; a sound that ends frees its player. An event with a looping sound (<see cref="Ms2.Audio.SoundClip.Loops"/>,
/// the FEV sound's loop mode; FMOD_EVENT_INFO.lengthms is -1 for it) plays until its handle is stopped, as FMOD keeps it.
/// </summary>
public partial class FieldAudio
{
    readonly Dictionary<long, (AudioStreamPlayer Player, long StartMs)> uiHandles = new();
    long lastUiHandle;

    /// <summary>The sound of <paramref name="key"/> played, and its handle (0 when nothing plays).</summary>
    public long PlayUiHandle(string key, string arg = "")
    {
        if (UiSoundPath(key, arg) is not { } path || library?.ResolveEvent(path) is not { } clip) return 0;
        var pcm = SafeDecode.Decode(clip);
        if (pcm == null) return 0;
        var player = new AudioStreamPlayer { Stream = Stream(pcm, pcm.ToBytes(), clip.Loops), VolumeDb = Mathf.LinearToDb(clip.Volume), Bus = SoundBuses.Of(clip.Category) };
        AddChild(player);
        long handle = ++lastUiHandle;
        uiHandles[handle] = (player, System.Environment.TickCount64);
        player.Finished += () => StopUi(handle);
        player.Play();
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: ui sound {key} handle {handle} -> {clip.Name}{(clip.Loops ? " loops" : "")}");
        return handle;
    }

    /// <summary>The stop by handle (FUN_1411a6060): the handle's sound stopped and its player freed; nothing for 0 or a sound already ended.</summary>
    public void StopUi(long handle)
    {
        if (handle == 0 || !uiHandles.Remove(handle, out var h)) return;
        var player = h.Player;
        if (!IsInstanceValid(player)) return;
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"audio: ui sound handle {handle} stopped after {(System.Environment.TickCount64 - h.StartMs) / 1000.0:F1} s "
                + $"(its sound {player.Stream?.GetLength() ?? 0:F1} s, playing {player.Playing})");
        player.Stop();
        player.QueueFree();
    }

    /// <summary>The UI sounds playing by handle (a check's count).</summary>
    public int UiHandlesPlaying => uiHandles.Count;
}
