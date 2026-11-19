using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The mastery state the client keeps: the own player's exp per type (PC::GetMastery/SetMastery, the user block's
/// 12-int list: a leading 0, then fishing, instrument, mining, foraging, ranching, farming, smithing, handicrafts,
/// alchemy, cooking, pet taming), the grade rewards received (DAT_143ab31a0, UserEnv mode 9) and the gathering counts
/// (g_143ab3150, UserEnv mode 8). Session-lived; UserEnv 8 and 9 replace their map whole (scenestategame.cpp:4469-4490).
/// </summary>
public sealed class MasteryData
{
    public const int Types = 12;

    /// <summary>The user block's list was read in (once per session).</summary>
    public bool Loaded;
    public readonly uint[] Exp = new uint[Types];
    public readonly Dictionary<uint, byte> Received = new();
    public readonly Dictionary<int, int> Gathering = new();

    /// <summary>The own user block's mastery list.</summary>
    public void Load(int[] userBlock)
    {
        Loaded = true;
        Array.Clear(Exp);
        if (userBlock == null) return;
        for (int i = 0; i < Types && i < userBlock.Length; i++) Exp[i] = (uint)userBlock[i];
    }

    public uint Get(int type) => (uint)type < Types ? Exp[type] : 0;

    /// <summary>OnUserEnv modes 8 and 9: clear, then fill. True when the message was one of them.</summary>
    public bool Apply(UserEnvMessage m)
    {
        switch (m)
        {
            case UserEnvMessage.GatheringCounts g:
                Gathering.Clear();
                foreach (var (k, v) in g.Counts) Gathering[k] = v;
                return true;
            case UserEnvMessage.MasteryRewards r:
                Received.Clear();
                foreach (var (k, v) in r.Received) Received[k] = v;
                return true;
        }
        return false;
    }

    /// <summary>OnMastery mode 1: the reward id is marked received (DAT_143ab31a0.SetAt(id, 1)).</summary>
    public void MarkReceived(int rewardId) => Received[(uint)rewardId] = 1;

    /// <summary>OnMastery mode 0 (scenestategame.cpp:3930-3934): stores the new exp; returns the gain, the old grade and
    /// whether the grade changed. <paramref name="grade"/> is CMasteryTable::Grade.</summary>
    public (uint Gain, int OldGrade, bool GradeUp) SetExp(int type, int exp, Func<int, uint, int> grade)
    {
        uint old = Get(type);
        int oldGrade = grade(type, old);
        uint gain = (uint)exp - old;
        if ((uint)type < Types) Exp[type] = (uint)exp;
        return (gain, oldGrade, grade(type, (uint)exp) != oldGrade);
    }
}
