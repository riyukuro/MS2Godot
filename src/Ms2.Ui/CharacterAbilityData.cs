using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CCharacterAbilityManager (a singleton, uicharacterabilitydialog.cpp:192-328): the learned ability ids (m_abilities, a
/// CAtlList in arrival order), the last reset time (m_nResetTime) and the extra points (m_nPoints). It outlives a field
/// change; a live session owns one and applies every CharacterAbility packet to it on arrival (<see cref="Apply"/>).
/// </summary>
public sealed class CharacterAbilityData
{
    /// <summary>CFeatureManager::FUN_14036de00: the level the abilities open at (0x32).</summary>
    public const ushort StartLevel = 0x32;

    public readonly List<int> Ids = new();
    public long ResetTime;
    public int Points;

    /// <summary>
    /// OnCharacterAbility (@0x140f29ad0): mode 0 (FUN_140f29b70) replaces the ids, the reset time and the points; mode 2
    /// (FUN_140f29cc0) adds the id when the table has it (<paramref name="known"/>), else nothing; mode 1 does nothing.
    /// True when the data changed and the dialog and the game menu are told (the client tells them only then).
    /// </summary>
    public bool Apply(CharacterAbilityMessage m, Func<int, bool> known)
    {
        switch (m)
        {
            case CharacterAbilityMessage.Loaded l:
                Ids.Clear();
                Ids.AddRange(l.Ids);
                ResetTime = l.ResetTime;
                Points = l.Points;
                return true;
            case CharacterAbilityMessage.Learned a:
                if (!known(a.Id)) return false;
                Ids.Add(a.Id);
                return true;
        }
        return false;
    }

    /// <summary>FUN_140f2a0b0: the id is learned (m_abilities.Find).</summary>
    public bool IsLearned(int id) => Ids.Contains(id);

    /// <summary>FUN_140f2a210: any ability learned.</summary>
    public bool Any => Ids.Count > 0;

    /// <summary>FUN_1401e0800: feature 0x2d is on and the level is at least <see cref="StartLevel"/>.</summary>
    public static bool IsOpen(bool feature, ushort level) => feature && !(StartLevel > level);

    /// <summary>FUN_140f29e40: 0 without a local player (<paramref name="level"/> null), else FUN_1401e0790: the level less
    /// <see cref="StartLevel"/>, plus CharacterAbilityDefaultPoint and the points, as an unsigned.</summary>
    public uint Total(int? level, uint defaultPoint) =>
        level is not { } lv ? 0u : (uint)((ushort)lv - StartLevel + (int)defaultPoint + Points);

    /// <summary>FUN_140f29e00: the points left, (int)(<see cref="Total"/> - the learned count).</summary>
    public int Remain(int? level, uint defaultPoint) => (int)(Total(level, defaultPoint) - (uint)Ids.Count);

    /// <summary>
    /// FUN_140f29ea0: the reset cooldown left in seconds: 0 without a reset; else the time since it against
    /// CharacterAbilityResetCoolTimeMinute minutes (CTimeSpan(unsigned(minutes * 60))): 0 once the whole minutes passed
    /// reach the cooldown's, else the cooldown less the time passed.
    /// </summary>
    public long CooldownLeft(long serverNow, uint coolMinutes)
    {
        if (ResetTime == 0) return 0;
        long elapsed = serverNow - ResetTime;
        long cooldown = (uint)(coolMinutes * 60);
        if (elapsed / 60 >= cooldown / 60) return 0;    // GetTotalMinutes
        return cooldown - elapsed;
    }
}
