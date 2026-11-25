using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot;

/// <summary>
/// The client's sound categories as Godot audio buses: one bus per CSoundEventMgr category slot (s_apszCategory,
/// soundeventmgr.cpp:884), one for the music system and one for the movie sound, all sending to Master. The buses are
/// the project's default layout (default_bus_layout.tres, bus 1 + slot, then Music and Movie), so the engine makes them at
/// startup. A player takes the bus of its sound's category (<see cref="Ms2.Audio.SoundClip.Category"/>) when it starts;
/// the bus carries the category's volume and mute from <see cref="SoundMix.Shared"/>, so a change reaches the sounds
/// already playing at once, as FMOD's EventCategory::setVolume / setMute do. Buses rather than a gain per player: a
/// change is 26 engine calls whatever is playing, a sound start costs one bus assignment, and nothing runs per frame.
/// The music players keep their own gain (cue volume, crossfade and fade times the bgm volume, FieldAudio); their bus
/// carries only the music system's mute (SetMusicMute). The movie bus carries the movie volume (GetMovieVolume,
/// gameoptionstruct.cpp:2786) and the movie mute (UIVideoContents::SetVideoMuted from SetAllMute).
/// </summary>
public static class SoundBuses
{
    /// <summary>The bus of each category slot, then Music and Movie, in the layout's order from bus 1.</summary>
    static readonly StringName[] Bus = System.Array.ConvertAll(Ms2.Audio.SoundCategories.BusNames, n => new StringName(n));
    const int MusicSlot = SoundMix.CategoryCount, MovieSlot = SoundMix.CategoryCount + 1;
    static readonly StringName MasterBus = "Master";
    /// <summary>The music system's bus.</summary>
    public static StringName Music => Bus[MusicSlot];
    /// <summary>The movie sound's bus (cut scene and hud videos).</summary>
    public static StringName Movie => Bus[MovieSlot];
    static int appliedVersion = -1;
    static float appliedMovieVolume = -1f;

    /// <summary>The bus a sound of <paramref name="category"/> plays on: its category's, or Master for a sound in none.</summary>
    public static StringName Of(int category) => (uint)category < SoundMix.CategoryCount ? Bus[category] : MasterBus;

    /// <summary>Puts <see cref="SoundMix.Shared"/> (with its movie volume) on the buses when it changed since the last call.
    /// Main thread only.</summary>
    public static void Sync()
    {
        var mix = SoundMix.Shared;
        float movie = mix.MovieVolume;
        if (appliedVersion == mix.Version && appliedMovieVolume == movie) return;
        if (AudioServer.BusCount <= MovieSlot + 1)
        {
            GD.PushWarning("audio: the sound category buses are missing (default_bus_layout.tres)");
            return;
        }
        for (int i = 0; i <= MovieSlot; i++)
        {
            float v = i < MusicSlot ? mix.Volume(i) : i == MovieSlot ? movie : 1f;
            AudioServer.SetBusVolumeDb(i + 1, v <= 1e-6f ? -80f : Mathf.LinearToDb(v));
            AudioServer.SetBusMute(i + 1, i < MusicSlot ? mix.Muted(i) : i == MusicSlot ? mix.MusicMuted : mix.MovieMuted);
        }
        appliedVersion = mix.Version;
        appliedMovieVolume = movie;
    }

    /// <summary>The window's focus (WM_ACTIVATEAPP, windowservice.cpp:801-831, from GameFlow's notification): the categories
    /// mute or come back unless "play in the background" is on (<see cref="SoundMix.AppActivated"/>).</summary>
    public static void AppFocus(bool active)
    {
        var mix = SoundMix.Shared;
        if (active == mix.AppActive) return;
        var options = GameOptions.Current?.SoundOption;
        if (active) mix.AppActivated(options); else mix.AppDeactivated(options);
        Sync();
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"audio: app {(active ? "active" : "inactive")}: {Describe()}");
    }

    /// <summary>The gain a bus applies now (0 when muted): for the live check's log.</summary>
    public static float BusGain(StringName bus)
    {
        int b = AudioServer.GetBusIndex(bus);
        if (b < 0) return 1f;
        return AudioServer.IsBusMute(b) ? 0f : Mathf.DbToLinear(AudioServer.GetBusVolumeDb(b));
    }

    /// <summary>One line with every bus's gain, for the live check.</summary>
    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in Bus) sb.Append(b).Append('=').Append(BusGain(b).ToString("F2")).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
