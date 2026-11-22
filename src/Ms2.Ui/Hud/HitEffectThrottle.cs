namespace Ms2.Ui.Hud;

/// <summary>
/// The hit effect throttle of CMagicService::FUN_14146c990 @0x14146c990 (magicdata.cpp:2991) for a local owner (the
/// optimizer never hides it, FUN_141bd19d0 kind 1: no sight range check, and IsUpdateEnabled's check passes). The state is
/// the looked-up object's: its last play tick (GameObject +0x11c, 0 at first) and the last tick per effect key (m_map120,
/// GameObject::FUN_14123dc30 / FUN_14123dcb0 @0x14123dcb0, which sets both).
/// </summary>
/// <remarks>
/// The client's key is the damage's m_nNameIndex, SkillDataTable::IndexSensors' number for the effectHit name (one per
/// distinct name); here the name itself is the key. An empty name has no number (0): FUN_14146c990 returns without a play.
/// The numbering runs over the skill files in the parallel load's order, so the one name numbered 0 (never played) is not
/// fixed and is not ported. The client drops the state with the object; here it stays for the object id.
/// </remarks>
public sealed class HitEffectThrottle
{
    /// <summary>FUN_14146c990's nShort and nLong.</summary>
    public const uint ObjectGapMs = 150, KeyGapMs = 300;

    sealed class Times
    {
        public uint Last;
        public readonly Dictionary<string, uint> ByKey = new(StringComparer.Ordinal);
    }

    readonly Dictionary<int, Times> objects = new();

    /// <summary>
    /// Whether the effect <paramref name="key"/> plays on the object <paramref name="objectId"/> at <paramref name="nowMs"/>:
    /// not with an empty key, nor before 150 ms after the object's last play, nor before 300 ms after the key's last play on
    /// it (unsigned compares, as GetTickCountMs2 is); a play sets both.
    /// </summary>
    public bool TryPlay(int objectId, string key, uint nowMs)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!objects.TryGetValue(objectId, out var t)) objects[objectId] = t = new Times();
        if (nowMs < t.Last + ObjectGapMs) return false;
        t.ByKey.TryGetValue(key, out uint last);
        if (nowMs < last + KeyGapMs) return false;
        t.Last = nowMs;
        t.ByKey[key] = nowMs;
        return true;
    }
}
