namespace Ms2.Formats.Options;

/// <summary>
/// The volume and mute of each sound category as CSoundEventMgr holds them (m_apCategory[11] and the music system,
/// soundeventmgr.cpp:1667-1735), with the movie sound UIVideoContents::SetVideoMuted follows. One instance for the game
/// (<see cref="Shared"/>): the options dialog's save (<see cref="Apply"/>), the startup apply and the window activation
/// (<see cref="AppActivated"/>, <see cref="AppDeactivated"/>) change it, and every player reads its category's gain from it.
/// The slots are the client's: 0 effect, 1 environment, 2 bgm, 3 UISound, 4 npcVoice, 5 SystemVoice, 6 mobVoice,
/// 7 EmotionVoice, 8 SystemAlert, 9 effectSpecialSkill, 10 PCVoice (s_apszCategory, soundeventmgr.cpp:884).
/// The main volume is already in each category's volume (GetCategoryVolume, gameoptionstruct.cpp:2757: step * main / 100);
/// the sound service's own SetVolume (soundservice.cpp:349) reaches only the net stream sound, which is not played here.
/// </summary>
public sealed class SoundMix
{
    public const int CategoryCount = 11;
    public static readonly SoundMix Shared = new();

    readonly float[] volume = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    readonly bool[] mute = new bool[CategoryCount];

    /// <summary>Counts every change, so a reader can tell it must reapply.</summary>
    public int Version { get; private set; }
    /// <summary>The music system's mute (SetMusicMute): SetAllMute and SetCategoryMute(2) set it (soundeventmgr.cpp:1700, 1715).</summary>
    public bool MusicMuted { get; private set; }
    /// <summary>The movie sound's mute: SetAllMute calls CUIService::FUN_141adbf70 -> UIVideoContents::SetVideoMuted
    /// (soundeventmgr.cpp:1721, uiservice.cpp:1241).</summary>
    public bool MovieMuted { get; private set; }
    /// <summary>The music volume of the last apply (SetCategoryVolume(2): m_pMusicSystem->setVolume, soundeventmgr.cpp:1673).</summary>
    public float MusicVolume => volume[2];
    /// <summary>The movie volume of the last apply (<see cref="SoundVolumes.MovieVolume"/>).</summary>
    public float MovieVolume { get; private set; } = 1f;
    /// <summary>The app has the focus (g_bAppActive, windowservice.cpp:801-831).</summary>
    public bool AppActive { get; private set; } = true;

    public float Volume(int category) => (uint)category < CategoryCount ? volume[category] : 1f;
    public bool Muted(int category) => (uint)category < CategoryCount && mute[category];
    /// <summary>What a sound of <paramref name="category"/> plays at on top of its own volume: 0 while muted. A sound in no
    /// category (-1) is under no option: 1.</summary>
    public float Gain(int category) => (uint)category >= CategoryCount ? 1f : mute[category] ? 0f : volume[category];

    /// <summary>FUN_14125fe20 (gameoptionstruct.cpp:2727-2752) after <see cref="SoundVolumes.From"/>: each slot's volume,
    /// then SetAllMute (the inactive-window mute is in <paramref name="v"/>.AllMute already).</summary>
    public void Apply(SoundVolumes v)
    {
        for (int i = 0; i < CategoryCount; i++) volume[i] = v.Category[i];
        MovieVolume = v.MovieVolume;
        SetAllMute(v.AllMute);
    }

    /// <summary>CSoundEventMgr::SetAllMute (soundeventmgr.cpp:1711-1723).</summary>
    public void SetAllMute(bool on)
    {
        MusicMuted = on;
        for (int i = 0; i < CategoryCount; i++) mute[i] = on;
        MovieMuted = on;
        Version++;
    }

    /// <summary>CSoundEventMgr::SetCategoryMute (soundeventmgr.cpp:1697-1706).</summary>
    public void SetCategoryMute(int category, bool on)
    {
        if ((uint)category >= CategoryCount) return;
        if (category == 2) MusicMuted = on;
        mute[category] = on;
        Version++;
    }

    /// <summary>The window got the focus (WM_ACTIVATEAPP, windowservice.cpp:801-811): unless "play in the background" is
    /// on, CSoundService::FUN_1417ae840(0) (soundservice.cpp:370-391) sets every category's mute to the mute-all option.</summary>
    public void AppActivated(GameOption.Sound options)
    {
        AppActive = true;
        if (options == null || options.PlayAll) return;
        foreach (int c in RestoreOrder) SetCategoryMute(c, options.MuteAll);
    }

    /// <summary>The window lost the focus (windowservice.cpp:819-831): unless "play in the background" is on,
    /// CSoundService::FUN_1417ae840(1) mutes all.</summary>
    public void AppDeactivated(GameOption.Sound options)
    {
        AppActive = false;
        if (options == null || options.PlayAll) return;
        SetAllMute(true);
    }

    /// <summary>The order FUN_1417ae840(0) unmutes in (soundservice.cpp:378-388).</summary>
    static readonly int[] RestoreOrder = { 0, 9, 1, 2, 3, 4, 7, 5, 8, 6, 10 };
}
