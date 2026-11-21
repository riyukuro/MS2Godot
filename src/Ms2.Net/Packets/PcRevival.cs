namespace Ms2.Net.Packets;

/// <summary>
/// Another PC's revival bookkeeping: the confirm end tick (PC m_n10e8, FUN_1416619d0), the confirm count (m_n10ec,
/// FUN_141661a60) and the revival flag (m_n10f0, FUN_141661ba0). The own player keeps the same three in
/// <see cref="CharacterInfo"/>.
/// </summary>
public sealed class PcRevival
{
    public uint EndTick;
    public int Count;
    public bool Flag;

    /// <summary>OnAddUser (scenestategame.cpp:11280-11281): FUN_141661b20 (the end tick and the count), then FUN_141661ba0.</summary>
    public void Apply(FieldAddUser u)
    {
        EndTick = u.RevivalEndTick; Count = u.RevivalCount; Flag = u.RevivalFlag;
    }

    /// <summary>OnRevivalConfirm (scenestategame.cpp:7356) for any PC: FUN_141661a60(count), then FUN_1416619d0(end tick).</summary>
    public void Confirm(uint endTick, int count)
    {
        Count = count; EndTick = endTick;
    }

    /// <summary>PC::vfunc_137 (pc.cpp:4598-4599): a death with a confirm count sets the flag.</summary>
    public void Died()
    {
        if (Count > 0) Flag = true;
    }

    /// <summary>
    /// PC::FUN_141661ac0 (pc.cpp:2043), run by every PC's PC::vfunc_53 frame (pc.cpp:4470): with an end tick, once the server tick reaches it,
    /// FUN_141661a60(0) then FUN_1416619d0(0). The flag stays. Returns whether they were cleared.
    /// </summary>
    public bool Expire(uint serverTick)
    {
        if (EndTick == 0 || serverTick < EndTick) return false;
        Count = 0; EndTick = 0;
        return true;
    }

    /// <summary>PC::IsRevivalBlocked (pc.cpp:2009) without the buff test: a confirm count above 0 or the flag.</summary>
    public bool Blocked => Count > 0 || Flag;
}
