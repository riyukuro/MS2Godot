using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The Pvp packet's field part (CSceneStateGame::OnPvp @0x141732350, scenestategame.cpp:4986) and PlayerKillNotice
/// (OnPlayerKillNotice @0x141733850, :4344, shown by the HUD).
/// <para>STILL-OPEN: 'e' (the own PC's win / lose pose, MyPC::vfunc_59(0x28, 0, 0x73 / 0x74, 0)), 'f' (CPVPManager::Clear:
/// the arena timers and entries, which are not ported) and the arena, rank duel, team and guild modes are read and not applied.</para>
/// </summary>
public partial class LiveSession
{
    /// <summary>A PlayerKillNotice (the kill log dialog, its chat lines and the streak notice).</summary>
    public event System.Action<PlayerKillNotice> PlayerKillNoticeReceived;

    /// <summary>The own PC's CPvPInfo (FieldAddUser's, then Pvp 'd').</summary>
    public Ms2.Ui.Skills.PvpInfo OwnPvp => new(Me.PvpPoint, Me.PvpTeam, Me.PvpMode);

    /// <summary>The local player as a hit candidate (the HUD's: id, party, PvP point and team, buff bits), for the other
    /// players' relation to it (<see cref="RemoteLayer"/>'s name tag colour); null without a HUD.</summary>
    public System.Func<Ms2.Ui.Skills.HitCandidate> OwnPcRelation;

    void OnPvp(PvpMessage m)
    {
        if (m is not PvpPointInfo d) return;
        // 'd': WorldService::FindPCById (the own PC or another player of the field), nothing when it is none (:4995-4997)
        if (d.ObjectId == selfObject) { Me.PvpPoint = d.Point; Me.PvpTeam = d.Team; Me.PvpMode = d.PvpMode; }
        else remote?.SetPvpInfo(d.ObjectId, new Ms2.Ui.Skills.PvpInfo(d.Point, d.Team, d.PvpMode));
        if (LiveCheck) GD.Print($"live: pvp point {d.ObjectId} {d.Point} team {d.Team} mode {d.PvpMode}");
    }
}
