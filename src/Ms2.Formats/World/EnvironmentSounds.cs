namespace Ms2.Formats.World;

/// <summary>
/// Which field entities get an environment sound when the field loads (WorldService's entity add, worldservice_1.cpp:10336-10350):
/// an entity of model MS2Sound (0x7dc80d88) that is no trigger object (MS2TriggerObject, 0x0e8dd99d), whose timeShowEnabled
/// (0x60a5b623, from the MS2TimeAmbSetting mixin) is not set, and whose soundEventID (0xed530c5f) is not empty
/// (CSoundEventMgr::CreateEnvironmentSound). A trigger object's sound starts on its Trigger packet event instead.
/// An entity with timeShowEnabled set gets no sound at all: no other client code reads timeShowEnabled, dayAmb, nightAmb,
/// dayVisibleTiming or nightVisibleTiming, so there is no day or night ambience to play. No MS2Sound entity of the KMS2 or
/// GMS2 fields sets it; the flat models that do are visual effects (EnvironmentSoundsTests).
/// </summary>
public static class EnvironmentSounds
{
    public const string Model = "MS2Sound";
    public const string TriggerModel = "MS2TriggerObject";
    public const string TimeShow = "timeShowEnabled";

    /// <summary>Whether the field load creates the entity's environment sound (its event name aside).</summary>
    public static bool MadeAtLoad(FieldEntity e) => e.HasMixin(Model) && !e.HasMixin(TriggerModel) && !e.Bool(TimeShow);
}
