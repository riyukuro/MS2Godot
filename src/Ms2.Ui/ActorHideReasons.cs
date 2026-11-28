namespace Ms2.Ui;

/// <summary>What <see cref="ActorHideReasons.Apply{TView}"/> sets on an actor: its visible property (vfunc_23), its name tag
/// (UIHudEntity::Entity::vfunc_8 reads the hidden flag, uihudentity.cpp:200) and its simple shadow (SetShadowVisible).</summary>
public interface IHideReasonView
{
    void SetShown(bool shown);
    void SetTagHidden(bool hidden);
    void SetShadowVisible(bool visible);
}

/// <summary>
/// PCBase's hide reasons (m_unk_c08, one bit per reason): AddHideReason @0x14166ca20 / RemoveHideReason @0x14166ca80
/// (pcbase.cpp:1155-1171) change one bit and, when it changed, ApplyHideReasons @0x14166cb30 (:1188-1203) hides the actor
/// (vfunc_23(1)) and its shadow while any reason is set, or shows both when none is.
/// The reasons seen on npcs and pets: 1 the own pc's pet while the own pc is hidden (scenestategame.cpp:9716-9717), 5 a failed
/// quest mask (npcactorandvibrate.cpp:1534), 10 the spawn state (statespawn.cpp:72).
/// The ridee branch (only reason 2 set: the ridee shown again) is the riding pc's (PlayerRide / RemotePc); npcs and pets have no ridee.
/// </summary>
public static class ActorHideReasons
{
    /// <summary>
    /// AddHideReason (<paramref name="on"/>) / RemoveHideReason of <paramref name="reason"/> on <paramref name="reasons"/>:
    /// false when the bit already had that value (FUN_14166cb00, nothing applied), else ApplyHideReasons on the view.
    /// </summary>
    public static bool Apply<TView>(ref int reasons, int reason, bool on, ref TView view) where TView : struct, IHideReasonView
    {
        if (!NpcQuestVisibility.SetHideReason(ref reasons, reason, on)) return false;
        bool shown = reasons == 0;    // PCBase::IsHidden @0x14166cad0: m_unk_c08.Any()
        view.SetShown(shown);
        view.SetTagHidden(!shown);
        view.SetShadowVisible(shown);
        return true;
    }
}
