namespace Ms2.Audio;

/// <summary>CSoundEventEffectParam::vfunc_2 @0x141775cb0 (soundeventmgr.cpp:310-345) over the FMOD project.</summary>
public static class EffectSoundParam
{
    /// <summary>
    /// True when a soundEvent part's param is made: no group, or the group found (CSoundEventMgr::GetEventGroup,
    /// <see cref="FevProject.EventsInGroup"/>) with its event in it (getEvent FMOD_EVENT_INFOONLY), each job's event of
    /// jobEvent with byJob. The base test (CEffectParam::vfunc_2) never refuses a soundEvent part.
    /// </summary>
    /// <param name="jobEvents">jobEvent's events with byJob on, else null.</param>
    public static bool Made(FevProject fev, string soundGroup, string soundEvent, IEnumerable<string> jobEvents)
    {
        if (string.IsNullOrEmpty(soundGroup)) return true;
        var group = fev.EventsInGroup(soundGroup);
        if (group.Count == 0) return false;
        if (jobEvents != null)
        {
            foreach (var ev in jobEvents) if (!Has(group, ev)) return false;
            return true;
        }
        return Has(group, soundEvent);
    }

    static bool Has(IReadOnlyList<FevEvent> group, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var e in group) if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
