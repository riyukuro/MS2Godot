using Godot;
using Ms2.Formats.Options;

namespace Ms2Godot;

/// <summary>
/// The client's sound categories as Godot audio buses: one bus per CSoundEventMgr category slot (s_apszCategory,
/// soundeventmgr.cpp:884), one for the music system and one for the movie sound, all sending to Master. A player takes
/// the bus of its sound's category (<see cref="Ms2.Audio.SoundClip.Category"/>) when it starts; the bus carries the
/// category's volume and mute from <see cref="SoundMix.Shared"/>, so a change reaches the sounds already playing at once,
/// as FMOD's EventCategory::setVolume / setMute do. Buses rather than a gain per player: a change is 13 engine calls
/// whatever is playing, a sound start costs one bus assignment, and nothing runs per frame.
/// The music players keep their own gain (cue volume, crossfade and fade times the bgm volume, FieldAudio); their bus
/// carries only the music system's mute (SetMusicMute). The movie bus carries the movie volume (GetMovieVolume,
/// gameoptionstruct.cpp:2786) and the movie mute (UIVideoContents::SetVideoMuted from SetAllMute).
/// </summary>
public static class SoundBuses
{
    static readonly StringName MasterBus = "Master";
    static readonly StringName[] CategoryBus = BuildNames();
    /// <summary>The music system's bus.</summary>
    public static readonly StringName Music = "Music";
    /// <summary>The movie sound's bus (cut scene and hud videos).</summary>
    public static readonly StringName Movie = "Movie";
    static int appliedVersion = -1;
    static float appliedMovieVolume = -1f;

    static StringName[] BuildNames()
    {
        var n = new StringName[Ms2.Audio.SoundCategories.Count];
        for (int i = 0; i < n.Length; i++) n[i] = Ms2.Audio.SoundCategories.Names[i];
        return n;
    }

    /// <summary>The bus a sound of <paramref name="category"/> plays on: its category's, or Master for a sound in none.</summary>
    public static StringName Of(int category)
    {
        if (appliedVersion < 0) Sync();
        return (uint)category < (uint)CategoryBus.Length ? CategoryBus[category] : MasterBus;
    }

    /// <summary>Puts <see cref="SoundMix.Shared"/> on the buses when it changed since the last call (makes them on the
    /// first). Main thread only.</summary>
    public static void Sync()
    {
        var mix = SoundMix.Shared;
        float movie = GameOptions.Current?.SoundOption?.MovieVolume ?? 1f;
        if (appliedVersion == mix.Version && appliedMovieVolume == movie && AudioServer.GetBusIndex(Movie) >= 0) return;
        for (int i = 0; i < CategoryBus.Length; i++)
        {
            int b = Ensure(CategoryBus[i]);
            AudioServer.SetBusVolumeDb(b, Db(mix.Volume(i)));
            AudioServer.SetBusMute(b, mix.Muted(i));
        }
        AudioServer.SetBusMute(Ensure(Music), mix.MusicMuted);
        int m = Ensure(Movie);
        AudioServer.SetBusVolumeDb(m, Db(movie));
        AudioServer.SetBusMute(m, mix.MovieMuted);
        appliedVersion = mix.Version;
        appliedMovieVolume = movie;
    }

    /// <summary>The gain a bus applies now (0 when muted): for the live check's log.</summary>
    public static float BusGain(StringName bus)
    {
        int b = AudioServer.GetBusIndex(bus);
        if (b < 0) return 1f;
        return AudioServer.IsBusMute(b) ? 0f : Mathf.DbToLinear(AudioServer.GetBusVolumeDb(b));
    }

    static float Db(float linear) => linear <= 1e-6f ? -80f : Mathf.LinearToDb(linear);

    static int Ensure(StringName name)
    {
        int b = AudioServer.GetBusIndex(name);
        if (b >= 0) return b;
        b = AudioServer.BusCount;
        AudioServer.AddBus(b);
        AudioServer.SetBusName(b, name);
        AudioServer.SetBusSend(b, MasterBus);
        return b;
    }

    /// <summary>One line with every bus's gain, for the live check.</summary>
    public static string Describe()
    {
        Sync();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < CategoryBus.Length; i++) sb.Append(CategoryBus[i]).Append('=').Append(BusGain(CategoryBus[i]).ToString("F2")).Append(' ');
        sb.Append("Music=").Append(BusGain(Music).ToString("F2")).Append(" Movie=").Append(BusGain(Movie).ToString("F2"));
        return sb.ToString();
    }
}
