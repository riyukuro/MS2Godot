namespace Ms2Godot;

/// <summary>
/// The ground material a PCBase keeps for its footsteps and landings (m_str958). Only PCBase::vfunc_93(true)
/// (0x141671130, pcbase.cpp:2458-2483) rewrites it, from the entity in the controller's hit report. Its callers are
/// PC::vfunc_53 (pc.cpp:4441-4445) and Ridee::vfunc_53 (ridee.cpp:1147-1152), when the 200 ms timer m_timer970 has expired
/// or is not running; StateLand::vfunc_4 (stateland.cpp:93); and the StateDash stop (state.cpp:1860). The steps
/// (pcbase.cpp:1004/1008) and the landing's PlayEffects (groundeffectdata.cpp:275, through FUN_141671480) read the stored
/// value, so a step 100 ms after moving from stone onto water still plays stone.
/// </summary>
public sealed class GroundMaterialSnapshot
{
    /// <summary>m_timer970's period: Timer::Start(200) in PC::vfunc_53 (pc.cpp:4443).</summary>
    public const double RefreshMs = 200;

    /// <summary>The hit report entity's MapAttribute (0xde5dd580): null when the report holds no entity, "" when the entity
    /// has none.</summary>
    public string Entity { get; set; }

    /// <summary>m_str958: "" until the first refresh, and after a refresh with no entity in the report.</summary>
    public string Material { get; private set; } = "";

    double elapsedMs;
    bool started;

    /// <summary>
    /// PC::vfunc_53 (pc.cpp:4441-4445): if m_timer970 has expired (Timer::IsExpired, elapsed &gt;= period, timer.cpp:167)
    /// or is not running, it restarts the timer and calls vfunc_93(true); otherwise vfunc_93(false) leaves the material alone.
    /// Before its first Start the timer has no period, so it is not running and the first frame refreshes.
    /// </summary>
    /// <returns>true when this frame refreshed</returns>
    public bool Frame(double dtSeconds, bool onOrAbove)
    {
        elapsedMs += dtSeconds * 1000.0;
        if (started && elapsedMs < RefreshMs) return false;
        started = true;
        elapsedMs = 0;
        Refresh(onOrAbove);
        return true;
    }

    /// <summary>
    /// vfunc_93(true): the material is emptied. With an entity in the report, PCBase::UpdateGroundMaterial (0x141670ed0,
    /// pcbase.cpp:2441-2454) sets "default", then the entity's MapAttribute when it has one and m_unk_9a8 is set (the
    /// actor is on the ground, FUN_141671a30, or the down ray from the feet hit something).
    /// </summary>
    public void Refresh(bool onOrAbove)
    {
        Material = Entity == null ? "" : onOrAbove && Entity.Length > 0 ? Entity : "default";
    }
}
