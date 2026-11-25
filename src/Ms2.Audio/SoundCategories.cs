namespace Ms2.Audio;

/// <summary>
/// The client's sound event categories: CSoundEventMgr::m_apCategory[11] (soundeventmgr.h:279), filled at init with
/// EventSystem::getCategory(s_apszCategory[i]) (soundeventmgr.cpp:884-885, 917-927). FUN_14125fe20 (gameoptionstruct.cpp:2727)
/// sets each slot's volume from the sound options and SetAllMute (soundeventmgr.cpp:1711) mutes all of them. An event plays
/// in the category the FEV project gives it (its first category, a path under "master"); FMOD Ex multiplies a category's
/// volume with its parents', so an event's gain is its own volume, its sound's volume, the authored volumes of the
/// sub-categories below the top-level one, and the top-level category's volume, which the options replace.
/// </summary>
public static class SoundCategories
{
    public const int Count = 11;
    public const int Effect = 0, Environment = 1, Bgm = 2, UiSound = 3, NpcVoice = 4, SystemVoice = 5, MobVoice = 6,
        EmotionVoice = 7, SystemAlert = 8, EffectSpecialSkill = 9, PcVoice = 10;

    /// <summary>s_apszCategory (soundeventmgr.cpp:884): the slot index is the SetCategoryVolume category.</summary>
    public static readonly string[] Names =
    {
        "effect", "environment", "bgm", "UISound", "npcVoice", "SystemVoice", "mobVoice", "EmotionVoice", "SystemAlert",
        "effectSpecialSkill", "PCVoice",
    };

    /// <summary>The audio buses the game plays them on, in the order of the game's bus layout from bus 1 (bus 0 is
    /// Master): one per slot, then the music system's and the movie sound's.</summary>
    public static readonly string[] BusNames =
    {
        "effect", "environment", "bgm", "UISound", "npcVoice", "SystemVoice", "mobVoice", "EmotionVoice", "SystemAlert",
        "effectSpecialSkill", "PCVoice", "Music", "Movie",
    };

    /// <summary>The slot of a top-level category name, -1 for a name the client does not hold (getCategory by a bare name
    /// finds top-level categories only).</summary>
    public static int IndexOf(string topName)
    {
        for (int i = 0; i < Names.Length; i++)
            if (string.Equals(Names[i], topName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>An event category path to the client's slot and the authored volume of its sub-categories below the
    /// top-level one (1 for a top-level category). False when the path names no category of the project or its top-level
    /// category is not one of the client's eleven: such an event's volume is not under any option.</summary>
    public static bool TryResolve(FevProject fev, string path, out int slot, out float belowTop)
    {
        slot = -1;
        belowTop = 1f;
        var c = fev?.FindCategory(path);
        if (c == null) return false;
        for (; c.Parent != null && c.Parent.Parent != null; c = c.Parent) belowTop *= c.Volume;
        slot = IndexOf(c.Name);
        return slot >= 0;
    }
}
