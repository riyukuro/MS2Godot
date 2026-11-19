using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The HUD player's numbers from the live session's own character (<see cref="MyCharacter"/>), field by field where the
/// client takes them:
/// <list type="bullet">
/// <item>name and level: the own FieldAddUser's user block (CSceneStateGame::OnAddUser, scenestategame.cpp:11143-11144,
/// vfunc_131 level and vfunc_76 name), the level then from LevelUp (OnLevelUp, :9511, vfunc_131);</item>
/// <item>job: the user block's job group and sub job (PC::GetJobGroupId / GetSubJob, pc.cpp:1269-1277, the job system
/// OnAddUser reads), as the character info record copies them (pc.cpp:4120-4121);</item>
/// <item>HP, SP, stamina: the Stat attributes 4, 7 and 10, current = stat row 2 and total = row 0 (OnStat, :9424, with
/// PCBase::FUN_141670a00 / FUN_141670a30, pcbase.cpp:1967-1976); before the first Stat, the user block's HP;</item>
/// <item>exp and rest exp: DAT_143ab28a0 / DAT_143ab28a8 from ServerEnter (:2085-2086) and ExpUp (:4140-4141);</item>
/// <item>channel: the user block's channel;</item>
/// <item>adventure level: the user block's m_530 (PC vfunc_141), then Prestige kind 2 (OnPrestige, :8836 vfunc_140, which
/// the session writes back to the user block).</item>
/// </list>
/// </summary>
public static class PlayerStateLive
{
    const int Health = 4, Spirit = 7, Stamina = 10;

    /// <summary>Copies <paramref name="me"/> into <paramref name="p"/>; returns the status events whose values changed (the
    /// events the client's handlers send, UIStatusEventDispatcher::NotifyStatusChanged). Before the own FieldAddUser the
    /// user block is <paramref name="chosen"/>, the character list entry the session entered with (the client's PC is built
    /// from it at ServerEnter); nothing when neither is known.</summary>
    public static List<StatusEvent> Apply(this PlayerState p, MyCharacter me, UserBlock chosen = null)
    {
        var changed = new List<StatusEvent>();
        if ((me?.User ?? chosen) is not { } u) return changed;
        void Set<T>(ref T field, T value, StatusEvent e)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            if (!changed.Contains(e)) changed.Add(e);
        }
        Set(ref p.Name, u.Name ?? "", StatusEvent.Name);
        Set(ref p.Level, (int)(ushort)u.Level, StatusEvent.Level);
        Set(ref p.SubJob, u.Job, StatusEvent.Job);
        Set(ref p.JobGroup, u.JobCode, StatusEvent.Job);
        Set(ref p.AdventureLevel, u.PrestigeLevel, StatusEvent.AdventureLevel);
        me ??= new MyCharacter();
        bool stats = me.Stats0[Health] > 0;
        if (stats || u.HpMax > 0)
        {
            Set(ref p.HpMax, stats ? me.Stats0[Health] : u.HpMax, StatusEvent.Hp);
            Set(ref p.HpCur, stats ? me.Stats2[Health] : u.HpCurrent, StatusEvent.Hp);
        }
        if (me.Stats0[Spirit] > 0)
        {
            Set(ref p.SpMax, me.Stats0[Spirit], StatusEvent.Sp);
            Set(ref p.SpCur, me.Stats2[Spirit], StatusEvent.Sp);
        }
        if (me.Stats0[Stamina] > 0)
        {
            Set(ref p.EpMax, me.Stats0[Stamina], StatusEvent.Ep);
            Set(ref p.EpCur, me.Stats2[Stamina], StatusEvent.Ep);
        }
        Set(ref p.Exp, me.Exp, StatusEvent.Exp);
        Set(ref p.RestExp, me.RestExp, StatusEvent.Exp);
        p.Channel = u.Channel;
        return changed;
    }
}
